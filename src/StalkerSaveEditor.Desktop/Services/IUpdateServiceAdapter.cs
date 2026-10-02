using StalkerSaveEditor.Updater;

namespace StalkerSaveEditor.Desktop.Services;

public interface IUpdateServiceAdapter
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(UpdateArtifact artifact, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<UpdateInstallResult> InstallAsync(string downloadedPath, UpdateArtifact artifact, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class UpdateServiceAdapter : IUpdateServiceAdapter, IDisposable
{
    private readonly UpdateService? _service;
    private readonly UpdateInstallation? _installation;
    private readonly string? _unavailableReason;
    private readonly string _downloadDirectory;

    public UpdateServiceAdapter(string? currentVersion = null, string? downloadDirectory = null)
    {
        CurrentVersion = currentVersion
            ?? StalkerSaveEditor.Core.ApplicationVersion.Current;

        try
        {
            _installation = UpdateInstallationDetector.Detect();
            _service = new UpdateService(CurrentVersion, _installation);
        }
        catch (Exception exception) when (exception is UpdateManifestException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The web host, an unknown platform, or an installation that cannot be identified: no self-update.
            // Nothing is guessed (a made-up installation would check, download and then fail to install).
            _installation = null;
            _service = null;
            _unavailableReason = exception.Message;
        }

        _downloadDirectory = downloadDirectory ?? Path.Combine(Path.GetTempPath(), "stalker-save-editor-updates");
    }

    internal UpdateServiceAdapter(UpdateService service, UpdateInstallation installation, string currentVersion, string? downloadDirectory = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _installation = installation ?? throw new ArgumentNullException(nameof(installation));
        CurrentVersion = currentVersion;
        _downloadDirectory = downloadDirectory ?? Path.Combine(Path.GetTempPath(), "stalker-save-editor-updates");
    }

    public string CurrentVersion { get; }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        Service.CheckAsync(cancellationToken);

    private UpdateService Service => _service ?? throw new PlatformNotSupportedException(_unavailableReason is null
        ? "Updates are not available on this platform."
        : "Updates are not available: " + _unavailableReason);

    public Task<string> DownloadAsync(
        UpdateArtifact artifact,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.Combine(_downloadDirectory, artifact.File);
        return Service.DownloadAsync(artifact, destination, progress, cancellationToken);
    }

    public Task<UpdateInstallResult> InstallAsync(
        string downloadedPath,
        UpdateArtifact artifact,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Service.InstallAsync(artifact, downloadedPath, _installation ?? throw new PlatformNotSupportedException("Updates are not available on this platform."), progress, cancellationToken);

    public void Dispose() => _service?.Dispose();
}
