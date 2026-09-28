namespace StalkerSaveEditor.Desktop.Services;

public enum CloudComparison
{
    Unknown,
    Identical,
    RemoteNewer,
    LocalNewer,
    RemoteOnly,
    LocalOnly,
}

public enum CloudWriteStatus
{
    Verified,
    Uncertain,
    Aborted,
}

public sealed record CloudFileModel(
    int AppId,
    string ReleaseId,
    string RemotePath,
    string FileName,
    long FileSizeBytes,
    DateTime? RemoteTimestampUtc,
    string? LocalFilePath,
    CloudComparison Comparison);

public sealed record CloudWriteResult(
    CloudWriteStatus Status,
    string? BackupPath,
    string? RecoveryPath,
    string? OutputSha256,
    string? Message);

public interface ICloudServiceAdapter
{
    bool IsSteamAvailable { get; }
    string? SteamStatusMessage { get; }

    Task<IReadOnlyList<CloudFileModel>> ListCloudFilesAsync(int appId, IEnumerable<string> localSavePaths, CancellationToken cancellationToken = default);
    Task<byte[]> ReadCloudFileAsync(int appId, string remotePath, CancellationToken cancellationToken = default);
    Task<CloudWriteResult> WriteCloudFileAsync(int appId, string remotePath, byte[] data, string backupDirectory, CancellationToken cancellationToken = default);
}
