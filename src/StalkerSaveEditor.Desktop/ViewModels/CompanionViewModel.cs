using System.Collections.ObjectModel;
using System.Windows.Input;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Hotkeys;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class CompanionHotkeyItemViewModel : ObservableViewModel
{
    private string _key;

    public CompanionHotkeyItemViewModel(CompanionHotkey model, Action<string, string> onKeyChanged)
    {
        Action = model.Action;
        _key = model.Key;
        Description = model.Description;
        ChangeKeyCommand = new RelayCommand<string>(newKey =>
        {
            if (!string.IsNullOrWhiteSpace(newKey))
            {
                Key = newKey.Trim().ToUpperInvariant();
                onKeyChanged(Action, Key);
            }
        });
    }

    public string Action { get; }
    public string Description { get; }

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public ICommand ChangeKeyCommand { get; }
}

public sealed class CompanionViewModel : ObservableViewModel
{
    private readonly ICompanionService _service;

    private string _selectedGame = "stalker-cop";
    private CompanionState _state = CompanionState.NotInstalled;
    private string _versionText = "—";
    private string _pingText = "—";
    private string _gamePath = "—";
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private string _installIssues = string.Empty;
    private bool _hotkeysEnabled;
    private string? _hotkeysUnsupportedReason;
    private string _manualGameDir = string.Empty;

    /// <summary>
    /// Production constructor — receives real <see cref="CompanionServiceAdapter"/>.
    /// </summary>
    public CompanionViewModel(ICompanionService service)
    {
        _service = service;
        InitCommands();
        CheckHotkeySupport();
        _ = RefreshStatusAsync();
    }

    /// <summary>
    /// Screenshot / test constructor — uses <see cref="MockCompanionService"/>.
    /// </summary>
    public CompanionViewModel() : this(MockCompanionService.Instance)
    {
    }

    public IReadOnlyList<KeyValuePair<string, string>> AvailableGames { get; } =
    [
        new("stalker-cop", "S.T.A.L.K.E.R. Зов Припяти"),
        new("stalker-cs", "S.T.A.L.K.E.R. Чистое Небо"),
        new("stalker-soc", "S.T.A.L.K.E.R. Тень Чернобыля"),
    ];

    public string SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                // sync manual dir display from adapter
                if (_service is CompanionServiceAdapter adapter)
                {
                    var game = ParseGame(value);
                    ManualGameDir = adapter.GetUserGameDirectory(game) ?? string.Empty;
                }

                _ = RefreshStatusAsync();
            }
        }
    }

    public CompanionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(CanPing));
                InstallCommand.NotifyCanExecuteChanged();
                UninstallCommand.NotifyCanExecuteChanged();
                PingCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusBadgeText => _state switch
    {
        CompanionState.Active => "РАБОТАЕТ (ПОДКЛЮЧЁН)",
        CompanionState.Installed => "УСТАНОВЛЕН (ОЖИДАНИЕ ИГРЫ)",
        CompanionState.NotInstalled => "НЕ УСТАНОВЛЕН",
        _ => "ОШИБКА",
    };

    public string StatusBadgeColor => _state switch
    {
        CompanionState.Active => "#4EC9B0",
        CompanionState.Installed => "#D6A62D",
        CompanionState.NotInstalled => "#7D8B73",
        _ => "#E05252",
    };

    public string VersionText
    {
        get => _versionText;
        private set => SetProperty(ref _versionText, value);
    }

    public string PingText
    {
        get => _pingText;
        private set => SetProperty(ref _pingText, value);
    }

    public string GamePath
    {
        get => _gamePath;
        private set => SetProperty(ref _gamePath, value);
    }

    public string InstallIssues
    {
        get => _installIssues;
        private set => SetProperty(ref _installIssues, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
                UninstallCommand.NotifyCanExecuteChanged();
                PingCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                SetManualDirCommand.NotifyCanExecuteChanged();
                ToggleHotkeysCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>True when the hotkeys toggle is on (sent to game via <c>hotkeys on/off</c>).</summary>
    public bool HotkeysEnabled
    {
        get => _hotkeysEnabled;
        private set => SetProperty(ref _hotkeysEnabled, value);
    }

    /// <summary>Non-null on Wayland-only or other unsupported platforms.</summary>
    public string? HotkeysUnsupportedReason
    {
        get => _hotkeysUnsupportedReason;
        private set
        {
            if (SetProperty(ref _hotkeysUnsupportedReason, value))
            {
                OnPropertyChanged(nameof(HotkeysSupported));
                ToggleHotkeysCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HotkeysSupported => _hotkeysUnsupportedReason is null;

    /// <summary>Manual game folder path typed/pasted by the user.</summary>
    public string ManualGameDir
    {
        get => _manualGameDir;
        set => SetProperty(ref _manualGameDir, value);
    }

    public bool CanInstall => !_isBusy && _state == CompanionState.NotInstalled;
    public bool CanUninstall => !_isBusy && _state != CompanionState.NotInstalled;
    public bool CanPing => !_isBusy && (_state == CompanionState.Active || _state == CompanionState.Installed);

    public ObservableCollection<CompanionHotkeyItemViewModel> Hotkeys { get; } = [];

    public RelayCommand InstallCommand { get; private set; } = null!;
    public RelayCommand UninstallCommand { get; private set; } = null!;
    public RelayCommand PingCommand { get; private set; } = null!;
    public RelayCommand RefreshCommand { get; private set; } = null!;
    public RelayCommand SetManualDirCommand { get; private set; } = null!;
    public RelayCommand ToggleHotkeysCommand { get; private set; } = null!;

    // ── Commands ──────────────────────────────────────────────────────────────

    private void InitCommands()
    {
        InstallCommand = new RelayCommand(
            async () => await InstallAsync(),
            () => !_isBusy && _state == CompanionState.NotInstalled);

        UninstallCommand = new RelayCommand(
            async () => await UninstallAsync(),
            () => !_isBusy && _state != CompanionState.NotInstalled);

        PingCommand = new RelayCommand(
            async () => await PingAsync(),
            () => !_isBusy && CanPing);

        RefreshCommand = new RelayCommand(
            async () => await RefreshStatusAsync(),
            () => !_isBusy);

        SetManualDirCommand = new RelayCommand(
            ApplyManualDir,
            () => !_isBusy);

        ToggleHotkeysCommand = new RelayCommand(
            async () => await ToggleHotkeysAsync(),
            () => !_isBusy && HotkeysSupported);
    }

    // ── Hotkey support check ──────────────────────────────────────────────────

    private void CheckHotkeySupport()
    {
        if (!CompanionServiceAdapter.AreHotkeysSupported(out var reason))
        {
            HotkeysUnsupportedReason = reason;
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            var status = await _service.GetStatusAsync(_selectedGame).ConfigureAwait(false);
            State = status.State;
            VersionText = status.Version;
            GamePath = status.GamePath;
            PingText = status.LastPing.HasValue
                ? $"{DateTime.UtcNow.Subtract(status.LastPing.Value).TotalSeconds:F0} сек назад"
                : "—";

            // Show any installer issues (anchor not found, file changed, etc.)
            if (_service is CompanionServiceAdapter adapter)
            {
                var issues = await adapter.GetInstallIssuesAsync(_selectedGame).ConfigureAwait(false);
                InstallIssues = issues.Count > 0
                    ? string.Join('\n', issues)
                    : string.Empty;

                HotkeysEnabled = adapter.AreHotkeysActive(_selectedGame);
            }
            else
            {
                // Mock: no issues, hotkeys always off
                InstallIssues = string.Empty;
                HotkeysEnabled = false;
            }

            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith("Ошибка", StringComparison.Ordinal))
            {
                StatusMessage = string.Empty;
            }

            // Populate hotkeys list.
            var hotkeys = await _service.GetHotkeysAsync(_selectedGame).ConfigureAwait(false);
            Hotkeys.Clear();
            foreach (var hk in hotkeys)
            {
                Hotkeys.Add(new CompanionHotkeyItemViewModel(hk, async (action, newKey) =>
                {
                    if (!await _service.UpdateHotkeyAsync(_selectedGame, action, newKey))
                    {
                        StatusMessage = "Переназначение клавиш пока не сохраняется — действует раскладка по умолчанию.";
                    }
                }));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка обновления статуса: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Install / Uninstall ───────────────────────────────────────────────────

    public async Task InstallAsync()
    {
        IsBusy = true;
        StatusMessage = "Установка компаньона в gamedata…";
        try
        {
            var ok = await _service.InstallAsync(_selectedGame).ConfigureAwait(false);
            StatusMessage = ok
                ? "Компаньон успешно установлен!"
                : "Не удалось установить компаньон — убедитесь, что папка игры найдена.";
            await RefreshStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка установки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UninstallAsync()
    {
        IsBusy = true;
        StatusMessage = "Удаление компаньона…";
        try
        {
            var ok = await _service.UninstallAsync(_selectedGame).ConfigureAwait(false);
            StatusMessage = ok ? "Компаньон удалён." : "Не удалось удалить компаньон.";
            await RefreshStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка удаления: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Ping ──────────────────────────────────────────────────────────────────

    public async Task PingAsync()
    {
        IsBusy = true;
        StatusMessage = "Проверка связи с модом…";
        try
        {
            var latency = await _service.PingAsync(_selectedGame).ConfigureAwait(false);
            if (latency.HasValue)
            {
                PingText = $"{latency.Value.TotalMilliseconds:F0} мс";
                StatusMessage = $"Мод отвечает. Задержка: {latency.Value.TotalMilliseconds:F0} мс";
            }
            else
            {
                PingText = "Нет ответа";
                StatusMessage = "Компаньон не отвечает. Убедитесь, что игра запущена.";
            }
        }
        catch (Exception ex)
        {
            PingText = "Ошибка";
            StatusMessage = $"Ошибка пинга: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Manual game dir ───────────────────────────────────────────────────────

    private void ApplyManualDir()
    {
        if (_service is not CompanionServiceAdapter adapter) return;
        var dir = ManualGameDir.Trim();
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            StatusMessage = $"Папка не найдена: {dir}";
            return;
        }

        var game = ParseGame(_selectedGame);
        adapter.SetUserGameDirectory(game, string.IsNullOrEmpty(dir) ? null : dir);
        StatusMessage = string.IsNullOrEmpty(dir)
            ? "Папка очищена, используется автообнаружение."
            : $"Папка задана: {dir}";
        _ = RefreshStatusAsync();
    }

    // ── Hotkeys toggle ────────────────────────────────────────────────────────

    private async Task ToggleHotkeysAsync()
    {
        if (_service is not CompanionServiceAdapter adapter) return;
        IsBusy = true;
        var enable = !_hotkeysEnabled;
        StatusMessage = enable ? "Включение горячих клавиш…" : "Отключение горячих клавиш…";
        try
        {
            var (success, error) = await adapter.ToggleHotkeysAsync(
                _selectedGame, enable).ConfigureAwait(false);
            if (success)
            {
                HotkeysEnabled = enable;
                StatusMessage = enable
                    ? "Горячие клавиши активированы (Ctrl+H/R/M/J/S)."
                    : "Горячие клавиши отключены.";
            }
            else
            {
                StatusMessage = $"Не удалось {(enable ? "включить" : "отключить")} горячие клавиши: {error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка горячих клавиш: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static CompanionGame ParseGame(string releaseId) => releaseId switch
    {
        "stalker-soc" => CompanionGame.ShadowOfChernobyl,
        "stalker-cs" => CompanionGame.ClearSky,
        "stalker-cop" => CompanionGame.CallOfPripyat,
        _ => CompanionGame.CallOfPripyat,
    };
}
