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

    public bool IsSteamAvailable => Availability().CanWrite;

    public string? SteamStatusMessage => Availability() is { CanWrite: true }
        ? L.T("Steam доступен.")
        : Availability().Reason;

    private (bool CanWrite, string? Reason) _availability;
    private DateTime _availabilityCheckedUtc;

    /// <summary>libsteam_api lookup (file system), cached for a few seconds: the screen asks on every selection.</summary>
    private (bool CanWrite, string? Reason) Availability()
    {
        if (DateTime.UtcNow - _availabilityCheckedUtc < TimeSpan.FromSeconds(10)) return _availability;
        try
        {
            var availability = _remoteStorageWriterFactory().CheckAvailability(41700);
            _availability = (availability.CanWrite, availability.Reason);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _availability = (false, L.T("Steam недоступен: {0}", exception.Message));
        }

        _availabilityCheckedUtc = DateTime.UtcNow;
        return _availability;
    }

    public async Task<IReadOnlyList<CloudFileModel>> ListCloudFilesAsync(
        int appId,
        IEnumerable<string> localSavePaths,
        CancellationToken cancellationToken = default)
    {
        // A cloud file is linked to a local one only when exactly one local save has that name: two profiles (or two
        // folders) with the same file name are ambiguous, and a wrong link would let a download replace the wrong save.
        var localDict = localSavePaths
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Steam offline or the game not owned: an empty list, the reason goes to the log.
            Core.Diagnostics.AppLog.Warn($"cloud list {appId} failed: {exception.Message}");
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
                L.T("Запись S.T.A.L.K.E.R. 2 в облако выключена до проверки в игре."));
        }

        try
        {
            // The local file goes up as it is; the transaction re-reads the cloud copy and refuses to
            // write when it changed after this read (the hash below).
            var current = await _readClientFactory(appId).ReadAsync(remotePath, cancellationToken).ConfigureAwait(false);
            var prepared = PreparedEdit.Replacing(Convert.ToHexString(SHA256.HashData(current)).ToLowerInvariant(), data);
            var result = await _remoteStorageWriterFactory().WriteAsync(
                appId,
                prepared,
                remotePath,
                backupDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var verified = result.Status == SteamRemoteStorageWriteStatus.Verified;
            return new CloudWriteResult(
                verified ? CloudWriteStatus.Verified : CloudWriteStatus.Uncertain,
                result.BackupPath,
                result.RecoveryPath,
                result.OutputSha256,
                verified
                    ? L.T("Записано в Steam Cloud и прочитано обратно.")
                    : L.T("Steam не подтвердил запись: {0}. Повтор не выполняется, облачный бэкап: {1}", result.Reason, result.BackupPath ?? "—"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new CloudWriteResult(
                CloudWriteStatus.Aborted,
                null,
                null,
                null,
                L.T("Ошибка записи в Steam Cloud: {0}", ex.Message));
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

            // Without a remote time only the size is known, and saves change without changing size: unknown.
            return CloudComparison.Unknown;
        }
        catch
        {
            return CloudComparison.Unknown;
        }
    }

    internal static string GetReleaseId(int appId) => appId switch
    {
        4500 => "stalker-soc",
        20510 => "stalker-cs",
        41700 => "stalker-cop",
        1643320 => "stalker2",
        _ => "unknown"
    };
}
