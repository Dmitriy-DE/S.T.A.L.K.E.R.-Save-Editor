namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Mock/Stub implementation of ICompanionService fulfilling AGENTS.md requirements
/// until Core ICompanionService API is available (Issue #59).
/// </summary>
public sealed class MockCompanionService : ICompanionService
{
    private static readonly Lazy<MockCompanionService> _instance = new(() => new MockCompanionService());
    public static MockCompanionService Instance => _instance.Value;

    private readonly Dictionary<string, CompanionState> _states = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stalker-cop"] = CompanionState.Active,
        ["stalker-cs"] = CompanionState.Installed,
        ["stalker-soc"] = CompanionState.NotInstalled,
    };

    private readonly Dictionary<string, List<CompanionHotkey>> _hotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stalker-cop"] =
        [
            new("quicksave", "F5", "Быстрое сохранение с меткой времени"),
            new("mark", "F6", "Поставить телепорт-метку на текущих координатах"),
            new("jump_last", "F7", "Мгновенный прыжок к последней метке"),
            new("hotkeys on", "F8", "Переключить оверлей горячих клавиш"),
        ],
        ["stalker-cs"] =
        [
            new("quicksave", "F5", "Быстрое сохранение с меткой времени"),
            new("mark", "F6", "Поставить телепорт-метку на текущих координатах"),
            new("jump_last", "F7", "Мгновенный прыжок к последней метке"),
            new("hotkeys on", "F8", "Переключить оверлей горячих клавиш"),
        ],
        ["stalker-soc"] =
        [
            new("quicksave", "F5", "Быстрое сохранение с меткой времени"),
            new("mark", "F6", "Поставить телепорт-метку на текущих координатах"),
            new("jump_last", "F7", "Мгновенный прыжок к последней метке"),
            new("hotkeys on", "F8", "Переключить оверлей горячих клавиш"),
        ],
    };

    private DateTime _lastPingTime = DateTime.UtcNow.AddSeconds(-2);

    public Task<CompanionStatus> GetStatusAsync(string gameReleaseId, CancellationToken ct = default)
    {
        var state = _states.TryGetValue(gameReleaseId, out var s) ? s : CompanionState.NotInstalled;
        var path = $"/home/dmytro/.local/share/Steam/steamapps/common/{GetGameFolder(gameReleaseId)}/gamedata";
        DateTime? ping = state == CompanionState.Active ? _lastPingTime : null;
        return Task.FromResult(new CompanionStatus(state, "1.4.0", ping, path));
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
        if (_states.TryGetValue(gameReleaseId, out var s) && s == CompanionState.Active)
        {
            _lastPingTime = DateTime.UtcNow;
            return Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(42));
        }

        return Task.FromResult<TimeSpan?>(null);
    }

    public Task<IReadOnlyList<CompanionHotkey>> GetHotkeysAsync(string gameReleaseId, CancellationToken ct = default)
    {
        if (!_hotkeys.TryGetValue(gameReleaseId, out var list))
        {
            list =
            [
                new("quicksave", "F5", "Быстрое сохранение"),
                new("mark", "F6", "Поставить метку"),
                new("jump_last", "F7", "Прыжок к метке"),
                new("hotkeys on", "F8", "Оверлей хоткеев"),
            ];
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

    private static string GetGameFolder(string releaseId) => releaseId switch
    {
        "stalker-soc" => "S.T.A.L.K.E.R. Shadow of Chernobyl",
        "stalker-cs" => "S.T.A.L.K.E.R. Clear Sky",
        _ => "S.T.A.L.K.E.R. Call of Pripyat",
    };
}
