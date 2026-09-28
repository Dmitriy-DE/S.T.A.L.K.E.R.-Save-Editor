using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Desktop.Services;

public sealed class CloudServiceAdapter : ICloudServiceAdapter
{
    private readonly Func<int, SteamReadOnlyClient> _readClientFactory;
    private readonly Func<SteamRemoteStorageCloudWriter> _remoteStorageWriterFactory;
    private readonly Func<int, string?> _autoCloudRootFinder;

    public CloudServiceAdapter()
        : this(
            appId => new SteamReadOnlyClient(appId),
            () => new SteamRemoteStorageCloudWriter(),
            SteamAutoCloudRootLocator.FindRoot)
    {
    }

    internal CloudServiceAdapter(
        Func<int, SteamReadOnlyClient> readClientFactory,
        Func<SteamRemoteStorageCloudWriter> remoteStorageWriterFactory,
        Func<int, string?> autoCloudRootFinder)
    {
        _readClientFactory = readClientFactory ?? throw new ArgumentNullException(nameof(readClientFactory));
        _remoteStorageWriterFactory = remoteStorageWriterFactory ?? throw new ArgumentNullException(nameof(remoteStorageWriterFactory));
        _autoCloudRootFinder = autoCloudRootFinder ?? throw new ArgumentNullException(nameof(autoCloudRootFinder));
    }

    public bool IsSteamAvailable
    {
        get
        {
            try
            {
                var writer = _remoteStorageWriterFactory();
                var avail = writer.CheckAvailability(41700);
                return avail.CanWrite;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? SteamStatusMessage
    {
        get
        {
            try
            {
                var writer = _remoteStorageWriterFactory();
                var avail = writer.CheckAvailability(41700);
                return avail.CanWrite
                    ? "Steam подключён и доступен."
                    : avail.Reason;
            }
            catch (Exception ex)
            {
                return $"Steam недоступен: {ex.Message}";
            }
        }
    }

    public async Task<IReadOnlyList<CloudFileModel>> ListCloudFilesAsync(
        int appId,
        IEnumerable<string> localSavePaths,
        CancellationToken cancellationToken = default)
    {
        var localDict = localSavePaths
            .Where(File.Exists)
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var releaseId = GetReleaseId(appId);
        var result = new List<CloudFileModel>();

        if (appId == SteamAutoCloudRootLocator.Stalker2AppId)
        {
            var root = _autoCloudRootFinder(appId);
            if (root is not null)
            {
                var s2Dir = Path.Combine(root, SteamAutoCloudRootLocator.Stalker2DirectoryName);
                if (Directory.Exists(s2Dir))
                {
                    var files = Directory.EnumerateFiles(s2Dir, "*.sav", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var info = new FileInfo(file);
                        var relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
                        var fileName = Path.GetFileName(file);
                        var comparison = CompareWithLocal(fileName, info.Length, info.LastWriteTimeUtc, localDict);

                        result.Add(new CloudFileModel(
                            appId,
                            releaseId,
                            relPath,
                            fileName,
                            info.Length,
                            info.LastWriteTimeUtc,
                            localDict.TryGetValue(fileName, out var localPath) ? localPath : null,
                            comparison));
                    }
                }
            }
            return result;
        }

        // Trilogy via RemoteStorage
        try
        {
            var client = _readClientFactory(appId);
            var cloudFiles = await client.ListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var cf in cloudFiles)
            {
                var fileName = Path.GetFileName(cf.Name);
                if (!fileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) &&
                    !fileName.EndsWith(".scop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTime? remoteTime = cf.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(cf.Timestamp).UtcDateTime
                    : null;

                var comparison = CompareWithLocal(fileName, cf.Size, remoteTime, localDict);

                result.Add(new CloudFileModel(
                    appId,
                    releaseId,
                    cf.Name,
                    fileName,
                    cf.Size,
                    remoteTime,
                    localDict.TryGetValue(fileName, out var localPath) ? localPath : null,
                    comparison));
            }
        }
        catch (Exception)
        {
            // If Steam RemoteStorage listing fails (e.g. Steam offline), return empty collection
        }

        return result;
    }

    public async Task<byte[]> ReadCloudFileAsync(
        int appId,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

        if (appId == SteamAutoCloudRootLocator.Stalker2AppId)
        {
            var root = _autoCloudRootFinder(appId);
            if (root is null) throw new FileNotFoundException("S.T.A.L.K.E.R. 2 Auto-Cloud folder was not found.");
            var fullPath = Path.Combine(root, remotePath.Replace('/', Path.DirectorySeparatorChar));
            return await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }

        var client = _readClientFactory(appId);
        return await client.ReadAsync(remotePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudWriteResult> WriteCloudFileAsync(
        int appId,
        string remotePath,
        byte[] data,
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        if (appId == SteamAutoCloudRootLocator.Stalker2AppId)
        {
            return new CloudWriteResult(
                CloudWriteStatus.Aborted,
                null,
                null,
                null,
                "Запись S.T.A.L.K.E.R. 2 в облако временно отключена до подтверждения полной мутации в игре.");
        }

        try
        {
            var writer = _remoteStorageWriterFactory();
            var releaseId = GetReleaseId(appId);
            var parsed = StalkerSaveEditor.Core.Formats.XRay.XRayTrilogyReader.FromBytes(data);
            var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var plan = new EditPlan(sha, money: parsed.Money);
            var prepared = EditService.PrepareEdit(data, plan, releaseId);

            var result = await writer.WriteAsync(
                appId,
                prepared,
                remotePath,
                backupDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var status = result.Status == SteamRemoteStorageWriteStatus.Verified
                ? CloudWriteStatus.Verified
                : CloudWriteStatus.Uncertain;

            var message = status == CloudWriteStatus.Verified
                ? "Успешно записано в Steam Cloud и подтверждено (Verified)."
                : $"Запись выполнена, но статус неопределён (Uncertain): {result.Reason}. Согласно правилам безопасности, автоматический повтор запрещён.";

            return new CloudWriteResult(
                status,
                result.BackupPath,
                result.RecoveryPath,
                result.OutputSha256,
                message);
        }
        catch (Exception ex)
        {
            return new CloudWriteResult(
                CloudWriteStatus.Aborted,
                null,
                null,
                null,
                $"Ошибка записи в Steam Cloud: {ex.Message}");
        }
    }

    private static CloudComparison CompareWithLocal(
        string fileName,
        long remoteSize,
        DateTime? remoteTime,
        Dictionary<string, string> localDict)
    {
        if (!localDict.TryGetValue(fileName, out var localPath) || !File.Exists(localPath))
        {
            return CloudComparison.RemoteOnly;
        }

        try
        {
            var localInfo = new FileInfo(localPath);
            if (remoteTime.HasValue)
            {
                var diff = (remoteTime.Value - localInfo.LastWriteTimeUtc).TotalSeconds;
                if (Math.Abs(diff) < 2.0 && localInfo.Length == remoteSize)
                {
                    return CloudComparison.Identical;
                }
                if (diff > 2.0)
                {
                    return CloudComparison.RemoteNewer;
                }
                return CloudComparison.LocalNewer;
            }

            return localInfo.Length == remoteSize ? CloudComparison.Identical : CloudComparison.Unknown;
        }
        catch
        {
            return CloudComparison.Unknown;
        }
    }

    private static string GetReleaseId(int appId) => appId switch
    {
        4500 => "stalker-soc",
        20510 => "stalker-cs",
        41700 => "stalker-cop",
        1643320 => "stalker2",
        _ => "unknown"
    };
}
