namespace StalkerSaveEditor.Steam;

/// <summary>Read-only access to Steam RemoteStorage through an isolated worker process.</summary>
public sealed class SteamReadOnlyClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly int _appId;
    private readonly TimeSpan _timeout;
    private readonly ISteamWorkerProcessRunner _runner;

    public SteamReadOnlyClient(int appId, TimeSpan? timeout = null)
        : this(appId, timeout, new SteamWorkerProcessRunner())
    {
    }

    internal SteamReadOnlyClient(int appId, ISteamWorkerProcessRunner runner)
        : this(appId, null, runner)
    {
    }

    internal SteamReadOnlyClient(
        int appId,
        TimeSpan? timeout,
        ISteamWorkerProcessRunner runner)
    {
        if (appId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be positive.");
        }

        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Worker timeout must be positive.");
        }

        _appId = appId;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task<IReadOnlyList<SteamCloudFile>> ListAsync(
        CancellationToken cancellationToken = default) =>
        _runner.ListAsync(_appId, _timeout, cancellationToken);

    public Task<byte[]> ReadAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return _runner.ReadAsync(_appId, fileName, _timeout, cancellationToken);
    }
}
