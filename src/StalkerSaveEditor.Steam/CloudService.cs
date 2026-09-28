using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Steam;

public enum CloudOperationStatus
{
    Verified,
    Uncertain,
    Aborted,
}

public sealed record CloudOperationResult(
    CloudOperationStatus Status,
    int AppId,
    string RemotePath,
    string? BackupPath,
    string? RecoveryPath,
    string? OutputSha256,
    string? Reason);

/// <summary>Single application-facing facade for Steam cloud files, explicit writes, and achievements.</summary>
public sealed class CloudService
{
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<SteamCloudFile>>> _listFiles;
    private readonly Func<int, string, CancellationToken, Task<byte[]>> _readFile;
    private readonly Func<int, PreparedEdit, string, string, CancellationToken, Task<SteamAutoCloudWriteResult>> _writeAutoCloud;
    private readonly Func<int, PreparedEdit, string, string, CancellationToken, Task<SteamRemoteStorageWriteResult>> _writeRemoteStorage;
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<SteamAchievement>>> _listAchievements;
    private readonly Func<int, string, bool, CancellationToken, Task<SteamAchievement>> _setAchievement;

    public CloudService(ISteamCloudWebReader cloudWebReader)
    {
        ArgumentNullException.ThrowIfNull(cloudWebReader);
        _listFiles = (appId, cancellationToken) => appId == SteamAutoCloudRootLocator.Stalker2AppId
            ? cloudWebReader.ListCloudFilesAsync(appId, cancellationToken)
            : new SteamReadOnlyClient(appId).ListAsync(cancellationToken);
        _readFile = (appId, path, cancellationToken) => appId == SteamAutoCloudRootLocator.Stalker2AppId
            ? cloudWebReader.ReadFreshFileAsync(path, cancellationToken)
            : new SteamReadOnlyClient(appId).ReadAsync(path, cancellationToken);
        _writeAutoCloud = async (appId, prepared, path, backupDirectory, cancellationToken) =>
            await new SteamAutoCloudWriter(cloudWebReader).WriteAsync(
                appId, prepared, path, backupDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        _writeRemoteStorage = async (appId, prepared, path, backupDirectory, cancellationToken) =>
            await new SteamRemoteStorageCloudWriter().WriteAsync(
                appId, prepared, path, backupDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        _listAchievements = static (appId, cancellationToken) =>
            new SteamAchievementsClient().ListAsync(appId, cancellationToken);
        _setAchievement = static (appId, apiName, achieved, cancellationToken) =>
            new SteamAchievementsClient().SetAsync(appId, apiName, achieved, cancellationToken);
    }

    internal CloudService(
        Func<int, CancellationToken, Task<IReadOnlyList<SteamCloudFile>>> listFiles,
        Func<int, string, CancellationToken, Task<byte[]>> readFile,
        Func<int, PreparedEdit, string, string, CancellationToken, Task<SteamAutoCloudWriteResult>> writeAutoCloud,
        Func<int, PreparedEdit, string, string, CancellationToken, Task<SteamRemoteStorageWriteResult>> writeRemoteStorage,
        Func<int, CancellationToken, Task<IReadOnlyList<SteamAchievement>>> listAchievements,
        Func<int, string, bool, CancellationToken, Task<SteamAchievement>> setAchievement)
    {
        _listFiles = listFiles ?? throw new ArgumentNullException(nameof(listFiles));
        _readFile = readFile ?? throw new ArgumentNullException(nameof(readFile));
        _writeAutoCloud = writeAutoCloud ?? throw new ArgumentNullException(nameof(writeAutoCloud));
        _writeRemoteStorage = writeRemoteStorage ?? throw new ArgumentNullException(nameof(writeRemoteStorage));
        _listAchievements = listAchievements ?? throw new ArgumentNullException(nameof(listAchievements));
        _setAchievement = setAchievement ?? throw new ArgumentNullException(nameof(setAchievement));
    }

    public Task<IReadOnlyList<SteamCloudFile>> ListFilesAsync(
        int appId,
        CancellationToken cancellationToken = default) =>
        _listFiles(RequireAppId(appId), cancellationToken);

    public Task<byte[]> ReadFileAsync(
        int appId,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        return _readFile(RequireAppId(appId), remotePath, cancellationToken);
    }

    /// <summary>Explicitly writes one prepared S.T.A.L.K.E.R. 2 save through Auto-Cloud.</summary>
    public async Task<CloudOperationResult> WriteStalker2Async(
        PreparedEdit prepared,
        string remotePath,
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        var appId = SteamAutoCloudRootLocator.Stalker2AppId;
        try
        {
            var result = await _writeAutoCloud(appId, prepared, remotePath, backupDirectory, cancellationToken)
                .ConfigureAwait(false);
            return new CloudOperationResult(
                result.Status == SteamAutoCloudWriteStatus.Verified ? CloudOperationStatus.Verified : CloudOperationStatus.Uncertain,
                appId,
                result.RemotePath,
                result.BackupPath,
                result.RecoveryPath,
                result.OutputSha256,
                result.Reason);
        }
        catch (Exception exception) when (IsAborted(exception))
        {
            return Aborted(appId, remotePath, exception);
        }
    }

    /// <summary>Explicitly writes one prepared trilogy save through Steam RemoteStorage.</summary>
    public async Task<CloudOperationResult> WriteTrilogyAsync(
        int appId,
        PreparedEdit prepared,
        string remotePath,
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        appId = RequireAppId(appId);
        try
        {
            var result = await _writeRemoteStorage(appId, prepared, remotePath, backupDirectory, cancellationToken)
                .ConfigureAwait(false);
            return new CloudOperationResult(
                result.Status == SteamRemoteStorageWriteStatus.Verified ? CloudOperationStatus.Verified : CloudOperationStatus.Uncertain,
                appId,
                result.RemotePath,
                result.BackupPath,
                result.RecoveryPath,
                result.OutputSha256,
                result.Reason);
        }
        catch (Exception exception) when (IsAborted(exception))
        {
            return Aborted(appId, remotePath, exception);
        }
    }

    public Task<IReadOnlyList<SteamAchievement>> ListAchievementsAsync(
        int appId,
        CancellationToken cancellationToken = default) =>
        _listAchievements(RequireAppId(appId), cancellationToken);

    /// <summary>Changes one achievement only when the caller explicitly invokes this operation.</summary>
    public Task<SteamAchievement> SetAchievementAsync(
        int appId,
        string apiName,
        bool achieved,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        return _setAchievement(RequireAppId(appId), apiName, achieved, cancellationToken);
    }

    private static bool IsAborted(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException;

    private static CloudOperationResult Aborted(int appId, string remotePath, Exception exception) => new(
        CloudOperationStatus.Aborted,
        appId,
        remotePath,
        null,
        null,
        null,
        exception.Message);

    private static int RequireAppId(int appId) => appId > 0
        ? appId
        : throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be positive.");
}
