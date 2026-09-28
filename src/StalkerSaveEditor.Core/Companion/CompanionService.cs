using System.Diagnostics;
using StalkerSaveEditor.Core.Hotkeys;

namespace StalkerSaveEditor.Core.Companion;

public enum CompanionRuntimeState
{
    NotInstalled,
    Installed,
    Active,
    Outdated,
}

public sealed record CompanionServiceStatus(
    CompanionRuntimeState State,
    CompanionInstallStatus Installation,
    DateTimeOffset? LastPingUtc,
    TimeSpan? PingLatency,
    string? ErrorMessage,
    string? GameModBuild = null,
    string? BundledModBuild = null);

public interface ICompanionService : IAsyncDisposable
{
    HotkeyLayout HotkeyLayout { get; }

    CompanionInstallStatus GetInstallStatus();

    Task<CompanionServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<CompanionInstallerResult> InstallAsync(CancellationToken cancellationToken = default);

    Task<CompanionInstallerResult> UninstallAsync(CancellationToken cancellationToken = default);

    Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default);

    Task<CompanionProtocolReply> MarkAsync(CancellationToken cancellationToken = default);

    Task<CompanionProtocolReply> JumpLastAsync(CancellationToken cancellationToken = default);

    Task<CompanionProtocolReply> QuickSaveAsync(
        string? name = null,
        CancellationToken cancellationToken = default);

    Task<CompanionProtocolReply> SetHotkeyPollingAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    void SetHotkeyLayout(HotkeyLayout layout);

    Task StartHotkeysAsync(CancellationToken cancellationToken = default);

    Task StopHotkeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>Core facade for companion installation, protocol commands, and global hotkeys.</summary>
public sealed class CompanionService : ICompanionService
{
    private const string SupportedModVersion = "v1";
    private readonly CompanionInstaller _installer;
    private readonly CompanionGame _game;
    private readonly string? _selectedGameDirectory;
    private readonly IReadOnlyList<string>? _steamRoots;
    private readonly TimeSpan _protocolTimeout;
    private CompanionHotkeyService? _hotkeyService;
    private HotkeyLayout _hotkeyLayout = HotkeyLayout.Default;

    public CompanionService(
        CompanionInstaller installer,
        CompanionGame game,
        string? selectedGameDirectory = null,
        IReadOnlyList<string>? steamRoots = null,
        TimeSpan? protocolTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(installer);
        _ = CompanionGameDefinition.For(game);
        _installer = installer;
        _game = game;
        _selectedGameDirectory = selectedGameDirectory;
        _steamRoots = steamRoots is null ? null : Array.AsReadOnly(steamRoots.ToArray());
        _protocolTimeout = protocolTimeout ?? TimeSpan.FromSeconds(10);
        if (_protocolTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolTimeout));
        }
    }

    public HotkeyLayout HotkeyLayout => _hotkeyLayout;

    public CompanionInstallStatus GetInstallStatus() =>
        _installer.GetStatus(_game, _selectedGameDirectory, _steamRoots);

    public async Task<CompanionServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var installation = await Task.Run(GetInstallStatus, cancellationToken).ConfigureAwait(false);
        if (!installation.GameFound || !installation.ModInstalled)
        {
            return new CompanionServiceStatus(
                CompanionRuntimeState.NotInstalled,
                installation,
                null,
                null,
                installation.Issues.Count == 0 ? null : string.Join(Environment.NewLine, installation.Issues));
        }

        if (!string.Equals(installation.Version, SupportedModVersion, StringComparison.Ordinal))
        {
            return new CompanionServiceStatus(
                CompanionRuntimeState.Outdated,
                installation,
                null,
                null,
                $"Installed companion protocol version '{installation.Version}' is not supported.");
        }

        var timer = Stopwatch.StartNew();
        try
        {
            var reply = await CreateProtocolClient(installation).SendAsync(
                "ping",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            timer.Stop();
            var isPong = reply.Status == CompanionReplyStatus.Ok &&
                (string.Equals(reply.Text, "pong", StringComparison.Ordinal) ||
                 reply.Text.StartsWith("pong ", StringComparison.Ordinal));
            var gameBuild = isPong && reply.Text.Length > 5 ? reply.Text[5..].Trim() : null;
            var bundledBuild = _installer.BundledModBuild;
            var state = reply.Status switch
            {
                CompanionReplyStatus.Unsupported => CompanionRuntimeState.Outdated,
                CompanionReplyStatus.Ok when isPong && reply.ReplyFileLastWriteTimeUtc.HasValue =>
                    bundledBuild is not null && !string.Equals(gameBuild, bundledBuild, StringComparison.Ordinal)
                        ? CompanionRuntimeState.Outdated
                        : CompanionRuntimeState.Active,
                _ => CompanionRuntimeState.Installed,
            };
            var error = reply.Status == CompanionReplyStatus.Error
                ? $"Companion ping failed: {reply.Text}"
                : state == CompanionRuntimeState.Outdated && reply.Status == CompanionReplyStatus.Ok
                    ? $"The game runs companion build '{gameBuild ?? "unknown"}', the editor ships '{bundledBuild}': reinstall the mod and restart the game."
                    : state == CompanionRuntimeState.Active || state == CompanionRuntimeState.Outdated
                        ? null
                        : "The companion did not return a fresh pong reply.";
            return new CompanionServiceStatus(
                state,
                installation,
                reply.ReplyFileLastWriteTimeUtc,
                reply.ReplyFileLastWriteTimeUtc.HasValue ? timer.Elapsed : null,
                error,
                gameBuild,
                bundledBuild);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return new CompanionServiceStatus(
                CompanionRuntimeState.Installed,
                installation,
                null,
                null,
                exception.Message);
        }
    }

    public Task<CompanionInstallerResult> InstallAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () => _installer.Install(_game, _selectedGameDirectory, _steamRoots),
            cancellationToken);

    public async Task<CompanionInstallerResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        await StopHotkeysAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => _installer.Uninstall(_game, _selectedGameDirectory, _steamRoots),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.PingLatency;
    }

    public Task<CompanionProtocolReply> MarkAsync(CancellationToken cancellationToken = default) =>
        SendInstalledCommand("mark", cancellationToken);

    public Task<CompanionProtocolReply> JumpLastAsync(CancellationToken cancellationToken = default) =>
        SendInstalledCommand("jump_last", cancellationToken);

    public Task<CompanionProtocolReply> QuickSaveAsync(
        string? name = null,
        CancellationToken cancellationToken = default) =>
        SendInstalledCommand("quicksave", cancellationToken, name is null ? [] : [name]);

    public Task<CompanionProtocolReply> SetHotkeyPollingAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendInstalledCommand("hotkeys", cancellationToken, [enabled ? "on" : "off"]);

    public void SetHotkeyLayout(HotkeyLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_hotkeyService?.IsActive == true)
        {
            throw new InvalidOperationException("Stop global hotkeys before replacing their layout.");
        }

        _hotkeyLayout = layout;
    }

    public async Task StartHotkeysAsync(
        CancellationToken cancellationToken = default)
    {
        var installation = GetInstallStatus();
        EnsureInstalled(installation);
        _hotkeyService ??= new CompanionHotkeyService(CreateProtocolClient(installation));
        await _hotkeyService.StartAsync(_hotkeyLayout, cancellationToken).ConfigureAwait(false);
    }

    public Task StopHotkeysAsync(CancellationToken cancellationToken = default) =>
        _hotkeyService?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_hotkeyService is not null)
        {
            await _hotkeyService.DisposeAsync().ConfigureAwait(false);
            _hotkeyService = null;
        }
    }

    private async Task<CompanionProtocolReply> SendInstalledCommand(
        string command,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? arguments = null)
    {
        var installation = await Task.Run(GetInstallStatus, cancellationToken).ConfigureAwait(false);
        EnsureInstalled(installation);
        return await CreateProtocolClient(installation)
            .SendAsync(command, arguments, cancellationToken)
            .ConfigureAwait(false);
    }

    private CompanionProtocolClient CreateProtocolClient(CompanionInstallStatus installation) =>
        new(installation.GameDirectory!, timeout: _protocolTimeout);

    private static void EnsureInstalled(CompanionInstallStatus installation)
    {
        if (!installation.GameFound || !installation.ModInstalled || installation.GameDirectory is null)
        {
            throw new CompanionInstallerException(
                installation.Issues.Count == 0
                    ? "The companion is not installed in a discovered game directory."
                    : string.Join(Environment.NewLine, installation.Issues));
        }
    }
}
