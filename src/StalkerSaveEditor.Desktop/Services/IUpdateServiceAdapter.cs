using System.Reflection;
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
    private readonly UpdateService _service;
    private readonly UpdateInstallation _installation;
    private readonly string _downloadDirectory;

    public UpdateServiceAdapter(string? currentVersion = null, string? downloadDirectory = null)
    {
        CurrentVersion = currentVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "1.0.0";

        try
        {
            _installation = UpdateInstallationDetector.Detect();
            _service = new UpdateService(CurrentVersion, _installation);
        }
        catch
        {
            var fallbackPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            _installation = new UpdateInstallation("linux", "x64", "package", AppContext.BaseDirectory, fallbackPath);
            _service = new UpdateService(CurrentVersion);
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
        _service.CheckAsync(cancellationToken);

    public Task<string> DownloadAsync(
        UpdateArtifact artifact,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.Combine(_downloadDirectory, artifact.File);
        return _service.DownloadAsync(artifact, destination, progress, cancellationToken);
    }

    public Task<UpdateInstallResult> InstallAsync(
        string downloadedPath,
        UpdateArtifact artifact,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _service.InstallAsync(artifact, downloadedPath, _installation, progress, cancellationToken);

    public void Dispose() => _service.Dispose();
}
