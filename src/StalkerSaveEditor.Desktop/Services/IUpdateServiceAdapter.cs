namespace StalkerSaveEditor.Desktop.Services;

public interface IUpdateServiceAdapter
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(UpdateArtifact artifact, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<UpdateInstallResult> InstallAsync(string downloadedPath, UpdateArtifact artifact, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);
}

public enum UpdateState
{
    Current,
    Available,
    Unavailable,
    Invalid,
}

public enum UpdateInstallState
{
    Succeeded,
    Cancelled,
    Failed,
    OpenedExternally,
}

/// <summary>The update models the screen works with. The updater project has its own; the host maps between them.</summary>
public sealed record UpdateArtifact(string Target, string Architecture, string Kind, string File, long Size, string Sha256, string Url);

public sealed record UpdateManifestInfo(string Version, string PublishedAt);

public sealed record UpdateCheckResult(UpdateState State, UpdateManifestInfo? Manifest = null, UpdateArtifact? Artifact = null, string? Error = null);

public sealed record UpdateProgress(string Stage, string Message, long? CompletedBytes = null, long? TotalBytes = null);

public sealed record UpdateInstallResult(UpdateInstallState State, int? ExitCode, string Message);

/// <summary>What a host without self-update (the web edition) offers.</summary>
public sealed class UnavailableUpdateServiceAdapter : IUpdateServiceAdapter
{
    public string CurrentVersion => StalkerSaveEditor.Core.ApplicationVersion.Current;

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new UpdateCheckResult(UpdateState.Unavailable));

    public Task<string> DownloadAsync(UpdateArtifact artifact, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Updates are not available on this platform.");

    public Task<UpdateInstallResult> InstallAsync(string downloadedPath, UpdateArtifact artifact, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Updates are not available on this platform.");
}
