namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Mock/Stub implementation of ICompanionService fulfilling AGENTS.md requirements
/// until Core ICompanionService API is available (Issue #59).
/// Does not fabricate fake connections, pings, or user-specific paths.
/// </summary>
public sealed class MockCompanionService : ICompanionService
{
    private static readonly Lazy<MockCompanionService> _instance = new(() => new MockCompanionService());
    public static MockCompanionService Instance => _instance.Value;

    private readonly Dictionary<string, CompanionState> _states = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stalker-cop"] = CompanionState.NotInstalled,
        ["stalker-cs"] = CompanionState.NotInstalled,
        ["stalker-soc"] = CompanionState.NotInstalled,
    };

    private readonly Dictionary<string, List<CompanionHotkey>> _hotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stalker-cop"] = CreateDefaultHotkeys(),
        ["stalker-cs"] = CreateDefaultHotkeys(),
        ["stalker-soc"] = CreateDefaultHotkeys(),
    };

    private static List<CompanionHotkey> CreateDefaultHotkeys() =>
    [
        new("heal", "Ctrl+H", "Быстрое лечение и снятие радиации"),
        new("repair", "Ctrl+R", "Починка экипированного оружия и брони"),
        new("money", "Ctrl+M", "Пополнение баланса сталкера"),
        new("jump_last", "Ctrl+J", "Мгновенный прыжок к последней телепорт-метке"),
        new("quicksave", "Ctrl+S", "Быстрое сохранение с меткой времени"),
    ];

    public Task<CompanionStatus> GetStatusAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var state = _states.TryGetValue(gameReleaseId, out var s) ? s : CompanionState.NotInstalled;
        return Task.FromResult(new CompanionStatus(state, state != CompanionState.NotInstalled ? "1.4.0" : "—", null, "—"));
    }

    public Task<bool> InstallAsync(string gameReleaseId, CancellationToken ct = default)
    {
        _states[gameReleaseId] = CompanionState.Installed;
        return Task.FromResult(true);
    }

    public Task<bool> UninstallAsync(string gameReleaseId, CancellationToken ct = default)
    {
        _states[gameReleaseId] = CompanionState.NotInstalled;
        return Task.FromResult(true);
    }

    public Task<TimeSpan?> PingAsync(string gameReleaseId, CancellationToken ct = default)
    {
        // No fake ping; returns null when not running a live connection
        return Task.FromResult<TimeSpan?>(null);
    }

    public Task<IReadOnlyList<CompanionHotkey>> GetHotkeysAsync(string gameReleaseId, CancellationToken ct = default)
    {
        if (!_hotkeys.TryGetValue(gameReleaseId, out var list))
        {
            list = CreateDefaultHotkeys();
        }

        return Task.FromResult<IReadOnlyList<CompanionHotkey>>(list);
    }

    public Task<bool> UpdateHotkeyAsync(string gameReleaseId, string action, string newKey, CancellationToken ct = default)
    {
        if (_hotkeys.TryGetValue(gameReleaseId, out var list))
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Action, action, StringComparison.OrdinalIgnoreCase))
                {
                    list[i] = list[i] with { Key = newKey };
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }
}
