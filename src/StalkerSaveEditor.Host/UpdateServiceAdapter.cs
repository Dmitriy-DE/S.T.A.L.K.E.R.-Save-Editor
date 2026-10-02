using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Updater;
using UpdateArtifact = StalkerSaveEditor.Desktop.Services.UpdateArtifact;
using UpdateCheckResult = StalkerSaveEditor.Desktop.Services.UpdateCheckResult;
using UpdateInstallResult = StalkerSaveEditor.Desktop.Services.UpdateInstallResult;
using UpdateInstallState = StalkerSaveEditor.Desktop.Services.UpdateInstallState;
using UpdateProgress = StalkerSaveEditor.Desktop.Services.UpdateProgress;
using UpdateState = StalkerSaveEditor.Desktop.Services.UpdateState;

namespace StalkerSaveEditor.Host;

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

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var result = await Service.CheckAsync(cancellationToken).ConfigureAwait(false);
        return new UpdateCheckResult(
            (UpdateState)(int)result.State,
            result.Manifest is null ? null : new UpdateManifestInfo(result.Manifest.Version, result.Manifest.PublishedAt),
            result.Artifact is null ? null : ToModel(result.Artifact),
            result.Error);
    }

    private UpdateService Service => _service ?? throw new PlatformNotSupportedException(_unavailableReason is null
        ? "Updates are not available on this platform."
        : "Updates are not available: " + _unavailableReason);

    public Task<string> DownloadAsync(
        UpdateArtifact artifact,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.Combine(_downloadDirectory, artifact.File);
        return Service.DownloadAsync(ToUpdater(artifact), destination, Map(progress), cancellationToken);
    }

    public async Task<UpdateInstallResult> InstallAsync(
        string downloadedPath,
        UpdateArtifact artifact,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Service.InstallAsync(
            ToUpdater(artifact),
            downloadedPath,
            _installation ?? throw new PlatformNotSupportedException("Updates are not available on this platform."),
            Map(progress),
            cancellationToken).ConfigureAwait(false);
        return new UpdateInstallResult((UpdateInstallState)(int)result.State, result.ExitCode, result.Message);
    }

    private static UpdateArtifact ToModel(Updater.UpdateArtifact value) =>
        new(value.Target, value.Architecture, value.Kind, value.File, value.Size, value.Sha256, value.Url);

    private static Updater.UpdateArtifact ToUpdater(UpdateArtifact value) =>
        new(value.Target, value.Architecture, value.Kind, value.File, value.Size, value.Sha256, value.Url);

    private static ForwardingProgress? Map(IProgress<UpdateProgress>? progress) =>
        progress is null ? null : new ForwardingProgress(progress);

    // Reports on the caller's thread, like the updater does; Progress<T> would post to a captured context instead.
    private sealed class ForwardingProgress(IProgress<UpdateProgress> target) : IProgress<Updater.UpdateProgress>
    {
        public void Report(Updater.UpdateProgress value) =>
            target.Report(new UpdateProgress(value.Stage, value.Message, value.CompletedBytes, value.TotalBytes));
    }

    public void Dispose() => _service?.Dispose();
}
