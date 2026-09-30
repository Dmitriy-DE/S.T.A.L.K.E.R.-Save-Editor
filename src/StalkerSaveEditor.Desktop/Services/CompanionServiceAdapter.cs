using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Hotkeys;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Real implementation of <see cref="ICompanionService"/> backed by Core APIs:
/// <see cref="CompanionInstaller"/>, <see cref="CompanionProtocolClient"/>, and
/// <see cref="CompanionHotkeyService"/>. Used in the normal application run.
/// <see cref="MockCompanionService"/> remains for --screenshot / test mode.
/// </summary>
public sealed class CompanionServiceAdapter : ICompanionService, IAsyncDisposable
{
    public bool SupportsLiveProtocol => true;
    private readonly string _modSourceRoot;

    // Per-game state: client + hotkey service created lazily when a game dir is known.
    private readonly Dictionary<string, GameRuntime> _runtimes = [];

    // Stores the user-specified or auto-detected game directory per game.
    // Keyed by release id: the retail game and its Enhanced Edition are separate installs.
    private readonly Dictionary<string, string?> _userGameDirs = new(StringComparer.Ordinal);

    // Hotkey toggle state per game (enabled/disabled).
    private readonly Dictionary<string, bool> _hotkeysEnabled = [];

    public CompanionServiceAdapter(string modSourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modSourceRoot);
        _modSourceRoot = modSourceRoot;
    }

    // ── public: game dir ──────────────────────────────────────────────────────

    /// <summary>Sets the manually chosen game folder for the given game.</summary>
    public void SetUserGameDirectory(string gameReleaseId, string? directory)
    {
        _userGameDirs[gameReleaseId] = string.IsNullOrWhiteSpace(directory) ? null : directory;
        // Invalidate any cached runtime when the game dir changes.
        if (_runtimes.Remove(gameReleaseId, out var old))
        {
            BackgroundTask.Run(old.DisposeAsync(), "companion dispose");
        }
    }

    public string? GetUserGameDirectory(string gameReleaseId) => _userGameDirs.GetValueOrDefault(gameReleaseId);

    // ── ICompanionService ─────────────────────────────────────────────────────

    public async Task<CompanionStatus> GetStatusAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var game = ParseGame(gameReleaseId);
        var installer = CreateInstaller();
        await Task.Yield(); // keep async signature; installer is sync
        try
        {
            var status = installer.GetStatus(game, DirectoryFor(gameReleaseId));
            return MapStatus(status);
        }
        catch (Exception ex)
        {
            return new CompanionStatus(CompanionState.Error, "—", null, "—", ex.Message);
        }
    }

    public async Task<bool> InstallAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var game = ParseGame(gameReleaseId);
        var installer = CreateInstaller();
        await Task.Yield();
        var result = installer.Install(game, DirectoryFor(gameReleaseId));
        return result.Success;
    }

    public async Task<bool> UninstallAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var game = ParseGame(gameReleaseId);
        var installer = CreateInstaller();
        await Task.Yield();
        var result = installer.Uninstall(game, DirectoryFor(gameReleaseId));
        return result.Success;
    }

    private readonly Dictionary<string, string?> _buildWarnings = [];

    /// <summary>Set by the last successful ping when the game runs another mod build than the editor ships.</summary>
    public string? ModBuildWarning(string gameReleaseId) => _buildWarnings.GetValueOrDefault(gameReleaseId);

    public async Task<TimeSpan?> PingAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var game = ParseGame(gameReleaseId);
        var gameDir = GetResolvedGameDir(gameReleaseId);
        if (gameDir is null) return null;
        try
        {
            var client = new CompanionProtocolClient(gameDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reply = await client.SendAsync("ping", null, ct).ConfigureAwait(false);
            sw.Stop();
            if (reply.Status != StalkerSaveEditor.Core.Companion.CompanionReplyStatus.Ok) return null;
            var gameBuild = reply.Text.StartsWith("pong ", StringComparison.Ordinal) ? reply.Text[5..].Trim() : null;
            var bundled = CreateInstaller().BundledModBuild;
            _buildWarnings[gameReleaseId] = bundled is not null && !string.Equals(gameBuild, bundled, StringComparison.Ordinal)
                ? L.T("В игре работает мод версии {0}, в редакторе — {1}. Нажмите «Установить / обновить» и перезапустите игру.", gameBuild ?? L.T("старее 2026.09.28"), bundled)
                : null;
            return sw.Elapsed;
        }
        catch
        {
            return null;
        }
    }

    public Task<IReadOnlyList<CompanionHotkey>> GetHotkeysAsync(
        string gameReleaseId, CancellationToken ct = default)
    {
        IReadOnlyList<CompanionHotkey> result = CurrentLayout.Bindings
            .Select(b => new CompanionHotkey(
                ActionToId(b.Action),
                b.Gesture.ToString(),
                ActionDescription(b.Action)))
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<CompanionInspectionResult> InspectAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(gameReleaseId, ct).ConfigureAwait(false);
        if (status.State is not (CompanionState.Installed or CompanionState.Active))
            return new CompanionInspectionResult(false, string.Empty, string.Empty, status.ErrorMessage ?? "Companion is not installed for this game.");
        var gameDirectory = GetResolvedGameDir(gameReleaseId);
        if (gameDirectory is null)
            return new CompanionInspectionResult(false, string.Empty, string.Empty, "The game directory could not be resolved.");

        try
        {
            var client = new CompanionProtocolClient(gameDirectory);
            var info = await client.SendAsync("info", cancellationToken: ct).ConfigureAwait(false);
            if (info.Status != CompanionReplyStatus.Ok)
                return new CompanionInspectionResult(false, string.Empty, string.Empty, info.Text);
            var inventory = await client.SendAsync("list_inventory", cancellationToken: ct).ConfigureAwait(false);
            if (inventory.Status != CompanionReplyStatus.Ok)
                return new CompanionInspectionResult(false, info.Text, string.Empty, inventory.Text);
            return new CompanionInspectionResult(true, info.Text, inventory.Text, "Live Companion data received.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new CompanionInspectionResult(false, string.Empty, string.Empty, exception.Message);
        }
    }

    public async Task<CompanionActionResult> GiveItemAsync(
        string gameReleaseId,
        string section,
        int count = 1,
        CancellationToken ct = default)
    {
        var status = await GetStatusAsync(gameReleaseId, ct).ConfigureAwait(false);
        if (status.State is not (CompanionState.Installed or CompanionState.Active))
            return new CompanionActionResult(false, status.ErrorMessage ?? "Companion is not installed for this game.");
        var gameDirectory = GetResolvedGameDir(gameReleaseId);
        if (gameDirectory is null)
            return new CompanionActionResult(false, "The game directory could not be resolved.");
        try
        {
            var reply = await new CompanionProtocolClient(gameDirectory).SendAsync(
                "give",
                [section, count.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                ct).ConfigureAwait(false);
            return new CompanionActionResult(reply.Status == CompanionReplyStatus.Ok, reply.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new CompanionActionResult(false, exception.Message);
        }
    }


    // ── Extended API (consumed by CompanionViewModel directly) ────────────────

    /// <summary>Returns installer issues for display in the UI (empty = OK).</summary>
    public async Task<IReadOnlyList<string>> GetInstallIssuesAsync(
        string gameReleaseId, CancellationToken ct = default)
    {
        var game = ParseGame(gameReleaseId);
        var installer = CreateInstaller();
        await Task.Yield();
        var status = installer.GetStatus(game, DirectoryFor(gameReleaseId));
        return status.Issues;
    }

    /// <summary>Returns whether the platform supports global hotkeys (false on pure Wayland).</summary>
    public static bool AreHotkeysSupported(out string? reason)
    {
        if (OperatingSystem.IsLinux())
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (string.IsNullOrEmpty(display) && !string.IsNullOrEmpty(wayland))
            {
                reason = L.T("Горячие клавиши требуют X11 или XWayland. ") +
                         L.T("Сеанс Wayland без XWayland не поддерживается.");
                return false;
            }
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            reason = L.T("Горячие клавиши поддерживаются только на Windows и Linux (X11/XWayland).");
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Enables or disables global hotkeys for a given game.
    /// Returns (success, errorMessage).
    /// </summary>
    public async Task<(bool Success, string? Error)> ToggleHotkeysAsync(
        string gameReleaseId, bool enable, HotkeyLayout? layout = null,
        CancellationToken ct = default)
    {
        var game = ParseGame(gameReleaseId);
        var gameDir = GetResolvedGameDir(gameReleaseId);
        if (gameDir is null)
        {
            return (false, L.T("Папка игры не найдена. Укажите путь вручную."));
        }

        try
        {
            if (enable)
            {
                // Create or reuse runtime.
                if (!_runtimes.TryGetValue(gameReleaseId, out var rt))
                {
                    var client = new CompanionProtocolClient(gameDir);
                    var svc = new CompanionHotkeyService(client);
                    rt = new GameRuntime(client, svc);
                    _runtimes[gameReleaseId] = rt;
                }

                if (!rt.HotkeyService.IsActive)
                {
                    await rt.HotkeyService.StartAsync(layout ?? CurrentLayout, ct)
                        .ConfigureAwait(false);
                }

                _hotkeysEnabled[gameReleaseId] = true;
            }
            else
            {
                if (_runtimes.TryGetValue(gameReleaseId, out var rt) && rt.HotkeyService.IsActive)
                {
                    await rt.HotkeyService.StopAsync(ct).ConfigureAwait(false);
                }

                _hotkeysEnabled[gameReleaseId] = false;
            }

            return (true, null);
        }
        catch (PlatformNotSupportedException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public bool AreHotkeysActive(string gameReleaseId)
    {
        var game = ParseGame(gameReleaseId);
        return _runtimes.TryGetValue(gameReleaseId, out var rt) && rt.HotkeyService.IsActive;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        foreach (var rt in _runtimes.Values)
        {
            await rt.DisposeAsync().ConfigureAwait(false);
        }

        _runtimes.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private CompanionInstaller CreateInstaller() => new(_modSourceRoot);

    /// <summary>EXPERIMENTAL S.T.A.L.K.E.R. 2 mod (UE4SS): status, install and removal.</summary>
    public Stalker2CompanionStatus Stalker2Status() => Stalker2CompanionInstaller.GetStatus();

    public Stalker2CompanionStatus InstallStalker2() => new Stalker2CompanionInstaller(_modSourceRoot).Install();

    /// <summary>
    /// Sends one S2 debug command (god / noclip / timespeed) to the running game through the mod's exchange folder.
    /// Returns the game's reply text, or why it could not be sent.
    /// </summary>
    public async Task<(bool Ok, string Text)> SendStalker2Async(string command, IReadOnlyList<string> arguments)
    {
        var status = Stalker2Status();
        if (!status.ModInstalled || status.GameDirectory is not { } game)
            return (false, L.T("Мод S2 не установлен."));
        try
        {
            var client = CompanionProtocolClient.ForDirectory(Stalker2CompanionInstaller.ProtocolDirectory(game), TimeSpan.FromSeconds(6));
            var reply = await client.SendAsync(command, arguments).ConfigureAwait(false);
            return (reply.Status == CompanionReplyStatus.Ok, reply.Text);
        }
        catch (CompanionProtocolTimeoutException)
        {
            return (false, L.T("Игра не ответила: запущена ли S2 с UE4SS и модом?"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CompanionProtocolException)
        {
            return (false, exception.Message);
        }
    }

    private string? GetResolvedGameDir(string gameReleaseId)
    {
        if (DirectoryFor(gameReleaseId) is { } directory) return directory;
        // Retail: the installer's own discovery (fsgame.ltx).
        return CreateInstaller().GetStatus(ParseGame(gameReleaseId)).GameDirectory;
    }

    /// <summary>
    /// The user's folder for this release, else for an Enhanced Edition the install Game Doctor finds (the installer's
    /// own discovery looks for retail fsgame.ltx first and would pick the retail game).
    /// </summary>
    private string? DirectoryFor(string gameReleaseId)
    {
        if (_userGameDirs.TryGetValue(gameReleaseId, out var chosen) && !string.IsNullOrWhiteSpace(chosen)) return chosen;
        if (!gameReleaseId.EndsWith("-ee", StringComparison.Ordinal)) return null;
        var target = StalkerSaveEditor.Core.Diagnostics.GameBuildFingerprints.TargetForFormat(gameReleaseId);
        return target is null
            ? null
            : StalkerSaveEditor.Core.Diagnostics.GameDoctor.DiscoverInstallations().FirstOrDefault(install => install.Target == target)?.Directory;
    }

    private static CompanionGame ParseGame(string releaseId) => releaseId switch
    {
        "stalker-soc" => CompanionGame.ShadowOfChernobyl,
        "stalker-cs" => CompanionGame.ClearSky,
        "stalker-cop" => CompanionGame.CallOfPripyat,
        "stalker-soc-ee" => CompanionGame.ShadowOfChernobyl,
        "stalker-cs-ee" => CompanionGame.ClearSky,
        "stalker-cop-ee" => CompanionGame.CallOfPripyat,
        _ => throw new ArgumentException($"Unknown game release id: '{releaseId}'", nameof(releaseId)),
    };

    private static CompanionStatus MapStatus(CompanionInstallStatus s)
    {
        var state = !s.GameFound
            ? CompanionState.NotInstalled
            : s.ModInstalled && s.Issues.Count == 0
                ? CompanionState.Installed
                : s.ModInstalled
                    ? CompanionState.Error
                    : CompanionState.NotInstalled;

        var issueText = s.Issues.Count > 0 ? string.Join('\n', s.Issues) : null;
        return new CompanionStatus(
            state,
            s.Version ?? "—",
            null,
            s.GameDirectory ?? "—",
            issueText);
    }

    /// <summary>The user's saved layout (hotkeys.txt in the data folder), or the default.</summary>
    public HotkeyLayout CurrentLayout => HotkeyLayout.Load(HotkeyLayoutPath);

    public string HotkeyLayoutPath { get; init; } = HotkeyLayout.DefaultPath;

    /// <summary>Saves the layout and, when hotkeys are running for the game, restarts them with it.</summary>
    public async Task<(bool Success, string? Error)> SaveHotkeyLayoutAsync(string gameReleaseId, HotkeyLayout layout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Save(HotkeyLayoutPath);
        if (!AreHotkeysActive(gameReleaseId)) return (true, null);
        var stopped = await ToggleHotkeysAsync(gameReleaseId, enable: false, ct: ct).ConfigureAwait(false);
        return stopped.Success ? await ToggleHotkeysAsync(gameReleaseId, enable: true, layout, ct).ConfigureAwait(false) : stopped;
    }

    private static string ActionToId(CompanionHotkeyAction action) => action switch
    {
        CompanionHotkeyAction.Heal => "heal",
        CompanionHotkeyAction.RepairEquipped => "repair_equipped",
        CompanionHotkeyAction.Mark => "mark",
        CompanionHotkeyAction.JumpLast => "jump_last",
        CompanionHotkeyAction.QuickSave => "quicksave",
        _ => action.ToString().ToLowerInvariant(),
    };

    private static string ActionDescription(CompanionHotkeyAction action) => action switch
    {
        CompanionHotkeyAction.Heal => L.T("Быстрое лечение и снятие радиации"),
        CompanionHotkeyAction.RepairEquipped => L.T("Починка экипированного оружия и брони"),
        CompanionHotkeyAction.Mark => L.T("Поставить метку на текущем месте"),
        CompanionHotkeyAction.JumpLast => L.T("Мгновенный прыжок к последней телепорт-метке"),
        CompanionHotkeyAction.QuickSave => L.T("Быстрое сохранение с меткой времени"),
        _ => action.ToString(),
    };

    private sealed class GameRuntime(
        CompanionProtocolClient client,
        CompanionHotkeyService hotkeyService) : IAsyncDisposable
    {
        public CompanionProtocolClient Client { get; } = client;
        public CompanionHotkeyService HotkeyService { get; } = hotkeyService;

        public async ValueTask DisposeAsync()
        {
            if (HotkeyService.IsActive)
            {
                try { await HotkeyService.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
            }

            await HotkeyService.DisposeAsync().ConfigureAwait(false);
        }
    }
}
