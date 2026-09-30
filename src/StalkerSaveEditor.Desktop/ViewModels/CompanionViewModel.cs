using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Hotkeys;
using StalkerSaveEditor.Desktop.Services;
using ICompanionService = StalkerSaveEditor.Desktop.Services.ICompanionService;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>One companion hotkey as shown on the screen; the key can be edited and saved to hotkeys.txt.</summary>
public sealed class CompanionHotkeyItemViewModel(string action, string key, string description) : ObservableViewModel
{
    private string _key = key;

    public CompanionHotkeyItemViewModel(CompanionHotkey model)
        : this(model.Action, model.Key, model.Description)
    {
    }

    public string Action { get; } = action;
    public string Description { get; } = description;

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value ?? string.Empty);
    }
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
    private string _inspectorInfo = string.Empty;
    private string _inspectorInventory = string.Empty;
    private string _inspectorStatus = string.Empty;
    private string _inspectorUpdated = string.Empty;

    /// <summary>
    /// Production constructor — receives real <see cref="CompanionServiceAdapter"/>.
    /// </summary>
    public CompanionViewModel(ICompanionService service)
    {
        _service = service;
        InitCommands();
        CheckHotkeySupport();
        BackgroundTask.Run(RefreshStatusAsync(), "companion status");
        BackgroundTask.Run(RefreshGamesAsync(), "companion games");
    }

    /// <summary>
    /// Screenshot / test constructor — uses <see cref="MockCompanionService"/>.
    /// </summary>
    public CompanionViewModel() : this(MockCompanionService.Instance)
    {
    }

    public IReadOnlyList<KeyValuePair<string, string>> AvailableGames { get; } =
    [
        new("stalker-cop", L.T("S.T.A.L.K.E.R. Зов Припяти")),
        new("stalker-cs", L.T("S.T.A.L.K.E.R. Чистое Небо")),
        new("stalker-soc", L.T("S.T.A.L.K.E.R. Тень Чернобыля")),
        new("stalker-cop-ee", L.T("Зов Припяти (Enhanced Edition)")),
        new("stalker-cs-ee", L.T("Чистое Небо (Enhanced Edition)")),
        new("stalker-soc-ee", L.T("Тень Чернобыля (Enhanced Edition)")),
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
                    ManualGameDir = adapter.GetUserGameDirectory(value) ?? string.Empty;
                }

                BackgroundTask.Run(RefreshStatusAsync(), "companion status");
                OnPropertyChanged(nameof(CanInspect));
                OnPropertyChanged(nameof(InspectorDisabledReason));
                InspectCommand.NotifyCanExecuteChanged();
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
                InspectCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanInspect));
                OnPropertyChanged(nameof(InspectorDisabledReason));
            }
        }
    }

    public string StatusBadgeText => _state switch
    {
        CompanionState.Active => L.T("РАБОТАЕТ (ПОДКЛЮЧЁН)"),
        CompanionState.Installed => L.T("УСТАНОВЛЕН (ОЖИДАНИЕ ИГРЫ)"),
        CompanionState.NotInstalled => L.T("НЕ УСТАНОВЛЕН"),
        _ => L.T("ОШИБКА"),
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
                InspectCommand.NotifyCanExecuteChanged();
                InstallCheckedCommand?.NotifyCanExecuteChanged();
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

    public string InspectorInfo { get => _inspectorInfo; private set => SetProperty(ref _inspectorInfo, value); }
    public string InspectorInventory { get => _inspectorInventory; private set => SetProperty(ref _inspectorInventory, value); }
    public string InspectorStatus { get => _inspectorStatus; private set => SetProperty(ref _inspectorStatus, value); }
    public string InspectorUpdated { get => _inspectorUpdated; private set => SetProperty(ref _inspectorUpdated, value); }
    public bool CanInspect => !_isBusy && _service.SupportsLiveProtocol && _state is CompanionState.Installed or CompanionState.Active;
    public string InspectorDisabledReason => !_service.SupportsLiveProtocol
        ? L.T("Для живой проверки нужен установленный Companion-протокол.")
        : _state is not (CompanionState.Installed or CompanionState.Active)
            ? L.T("Установите Companion для выбранной игры; живые данные доступны при запущенной игре.")
            : string.Empty;

    public bool CanSpawnForGame(string releaseId) =>
        _service.SupportsLiveProtocol && SelectedGame == releaseId && State == CompanionState.Active;

    public async Task<(bool Success, string Message)> GiveItemAsync(string releaseId, string section)
    {
        var result = await _service.GiveItemAsync(releaseId, section, 1).ConfigureAwait(false);
        return (result.Success, result.Message);
    }

    public bool CanInstall => !_isBusy && _state != CompanionState.Active;

    /// <summary>All three X-Ray games: where each was found and whether the mod is there.</summary>
    public ObservableCollection<CompanionGameRow> Games { get; } = [];

    public RelayCommand InstallCheckedCommand { get; private set; } = null!;

    private const string Stalker2ReleaseId = "stalker2";

    public async Task RefreshGamesAsync()
    {
        var rows = new List<CompanionGameRow>();
        foreach (var (releaseId, title) in AvailableGames)
        {
            var status = await _service.GetStatusAsync(releaseId);
            var found = status.GamePath is { Length: > 0 } path && path != "—";
            rows.Add(new CompanionGameRow(releaseId, title, found, found ? status.GamePath : L.T("не найдена"), status.State switch
            {
                CompanionState.Active => L.T("работает"),
                CompanionState.Installed => L.T("мод установлен ") + status.Version,
                CompanionState.Error => L.T("ошибка: ") + (status.ErrorMessage ?? L.T("проверьте файлы")),
                _ => found ? L.T("мод не установлен") : L.T("игра не найдена"),
            }));
        }

        if (_service is CompanionServiceAdapter adapter)
        {
            var s2 = await Task.Run(adapter.Stalker2Status);
            rows.Add(new CompanionGameRow(
                Stalker2ReleaseId,
                L.T("S.T.A.L.K.E.R. 2 (экспериментально, нужен UE4SS)"),
                s2.GameFound && s2.LoaderFound,
                s2.GameDirectory ?? L.T("не найдена"),
                !s2.GameFound ? L.T("игра не найдена")
                    : !s2.LoaderFound ? L.T("нет UE4SS: установите его, затем мод")
                    : s2.Issue is not null ? L.T("ошибка: ") + s2.Issue
                    : s2.ModInstalled ? L.T("мод установлен ") + s2.ModBuild : L.T("мод не установлен"))
            {
                IsChecked = false, // experimental: only when the player asks for it
            });
        }

        Games.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, _) => InstallCheckedCommand.NotifyCanExecuteChanged();
            Games.Add(row);
        }

        InstallCheckedCommand.NotifyCanExecuteChanged();
    }

    public async Task InstallCheckedAsync()
    {
        IsBusy = true;
        var results = new List<string>();
        try
        {
            foreach (var row in Games.Where(row => row.IsChecked && row.GameFound).ToArray())
            {
                StatusMessage = L.T("Установка в «{0}»…", row.Title);
                try
                {
                    var ok = row.ReleaseId == Stalker2ReleaseId && _service is CompanionServiceAdapter s2Adapter
                        ? (await Task.Run(s2Adapter.InstallStalker2)).ModInstalled
                        : await _service.InstallAsync(row.ReleaseId);
                    results.Add($"{row.Title}: {(ok ? L.T("Готово") : L.T("не установлен, причина — в строке игры"))}");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    results.Add($"{row.Title}: {exception.Message}");
                }
            }

            StatusMessage = string.Join("; ", results) + L.T(". Перезапустите игры, которые были открыты.");
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshGamesAsync();
        await RefreshStatusAsync();
    }
    public bool CanUninstall => !_isBusy && _state != CompanionState.NotInstalled;
    public bool CanPing => !_isBusy && (_state == CompanionState.Active || _state == CompanionState.Installed);

    public ObservableCollection<CompanionHotkeyItemViewModel> Hotkeys { get; } = [];

    public RelayCommand InstallCommand { get; private set; } = null!;
    public RelayCommand UninstallCommand { get; private set; } = null!;
    public RelayCommand PingCommand { get; private set; } = null!;
    public RelayCommand RefreshCommand { get; private set; } = null!;
    public RelayCommand SetManualDirCommand { get; private set; } = null!;
    public RelayCommand ToggleHotkeysCommand { get; private set; } = null!;
    public RelayCommand SaveHotkeysCommand { get; private set; } = null!;
    public RelayCommand ResetHotkeysCommand { get; private set; } = null!;

    /// <summary>"Ctrl+H · Ctrl+R · …" for the current rows.</summary>
    public string HotkeySummary => string.Join("  ·  ", Hotkeys.Select(hotkey => hotkey.Key));
    public RelayCommand InspectCommand { get; private set; } = null!;

    private string _stalker2CommandStatus = string.Empty;

    /// <summary>EXPERIMENTAL S2: the game's own debug commands through the UE4SS mod (desktop only).</summary>
    public bool SupportsStalker2Commands => _service is CompanionServiceAdapter;

    public string Stalker2CommandStatus
    {
        get => _stalker2CommandStatus;
        private set => SetProperty(ref _stalker2CommandStatus, value);
    }

    public RelayCommand<string> Stalker2CommandCommand => _stalker2Command ??= new RelayCommand<string>(
        async spec => await SendStalker2Async(spec));

    private RelayCommand<string>? _stalker2Command;

    /// <summary><paramref name="spec"/> is "command argument", e.g. "god on" or "timespeed 5".</summary>
    public async Task SendStalker2Async(string? spec)
    {
        if (_service is not CompanionServiceAdapter adapter || string.IsNullOrWhiteSpace(spec)) return;
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Stalker2CommandStatus = L.T("Отправка в игру…");
        var (ok, text) = await adapter.SendStalker2Async(parts[0], parts[1..]);
        Stalker2CommandStatus = ok ? L.T("Игра выполнила: {0}", text) : L.T("Не выполнено: {0}", text);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private void InitCommands()
    {
        // Install is idempotent: on an installed game it updates the mod files to the bundled build.
        InstallCommand = new RelayCommand(
            async () => await InstallAsync(),
            () => !_isBusy && _state != CompanionState.Active);

        InstallCheckedCommand = new RelayCommand(
            async () => await InstallCheckedAsync(),
            () => !_isBusy && Games.Any(row => row.IsChecked && row.GameFound));

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
        SaveHotkeysCommand = new RelayCommand(async () => await SaveHotkeysAsync(), () => !_isBusy && Hotkeys.Count > 0);
        ResetHotkeysCommand = new RelayCommand(() =>
        {
            foreach (var binding in HotkeyLayout.Default.Bindings)
            {
                if (Hotkeys.FirstOrDefault(row => row.Action == HotkeyLayout.ActionName(binding.Action)) is { } row) row.Key = binding.Gesture.ToString();
            }
            BackgroundTask.Run(SaveHotkeysAsync(), "hotkey save");
        }, () => !_isBusy && Hotkeys.Count > 0);

        InspectCommand = new RelayCommand(
            async () => await RefreshInspectorAsync(),
            () => CanInspect);
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
            var status = await _service.GetStatusAsync(_selectedGame);
            State = status.State;
            VersionText = status.Version;
            GamePath = status.GamePath;
            PingText = status.LastPing.HasValue
                ? L.T("{0:F0} сек назад", DateTime.UtcNow.Subtract(status.LastPing.Value).TotalSeconds)
                : "—";

            // Show any installer issues (anchor not found, file changed, etc.)
            if (_service is CompanionServiceAdapter adapter)
            {
                var issues = await adapter.GetInstallIssuesAsync(_selectedGame);
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

            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith(L.T("Ошибка"), StringComparison.Ordinal))
            {
                StatusMessage = string.Empty;
            }

            // Populate hotkeys list.
            var hotkeys = await _service.GetHotkeysAsync(_selectedGame);
            Hotkeys.Clear();
            foreach (var hk in hotkeys)
            {
                Hotkeys.Add(new CompanionHotkeyItemViewModel(hk));
            }
            OnPropertyChanged(nameof(HotkeySummary));
            SaveHotkeysCommand.NotifyCanExecuteChanged();
            ResetHotkeysCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanInspect));
            OnPropertyChanged(nameof(InspectorDisabledReason));
            InspectCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("Ошибка обновления статуса: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshInspectorAsync()
    {
        if (!CanInspect)
        {
            InspectorStatus = InspectorDisabledReason;
            return;
        }

        IsBusy = true;
        InspectorStatus = L.T("Чтение ответов Companion…");
        try
        {
            var result = await _service.InspectAsync(_selectedGame);
            InspectorStatus = result.Message;
            if (result.Success)
            {
                InspectorInfo = result.Info;
                InspectorInventory = result.Inventory;
                InspectorUpdated = DateTimeOffset.Now.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            InspectorStatus = L.T("Не удалось получить данные Companion: {0}", exception.Message);
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
        StatusMessage = L.T("Установка компаньона в gamedata…");
        try
        {
            var ok = await _service.InstallAsync(_selectedGame);
            StatusMessage = ok
                ? L.T("Компаньон успешно установлен!")
                : L.T("Не удалось установить компаньон — убедитесь, что папка игры найдена.");
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("Ошибка установки: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UninstallAsync()
    {
        IsBusy = true;
        StatusMessage = L.T("Удаление компаньона…");
        try
        {
            var ok = await _service.UninstallAsync(_selectedGame);
            StatusMessage = ok ? L.T("Компаньон удалён.") : L.T("Не удалось удалить компаньон.");
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("Ошибка удаления: {0}", ex.Message);
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
        StatusMessage = L.T("Проверка связи с модом…");
        try
        {
            var latency = await _service.PingAsync(_selectedGame);
            if (latency.HasValue)
            {
                PingText = L.T("{0:F0} мс", latency.Value.TotalMilliseconds);
                StatusMessage = (_service as CompanionServiceAdapter)?.ModBuildWarning(_selectedGame)
                    ?? L.T("Мод отвечает. Задержка: {0:F0} мс", latency.Value.TotalMilliseconds);
            }
            else
            {
                PingText = L.T("Нет ответа");
                StatusMessage = L.T("Компаньон не отвечает. Убедитесь, что игра запущена.");
            }
        }
        catch (Exception ex)
        {
            PingText = L.T("Ошибка");
            StatusMessage = L.T("Ошибка пинга: {0}", ex.Message);
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
            StatusMessage = L.T("Папка не найдена: {0}", dir);
            return;
        }

        adapter.SetUserGameDirectory(_selectedGame, string.IsNullOrEmpty(dir) ? null : dir);
        StatusMessage = string.IsNullOrEmpty(dir)
            ? L.T("Папка очищена, используется автообнаружение.")
            : L.T("Папка задана: {0}", dir);
        BackgroundTask.Run(RefreshStatusAsync(), "companion status");
    }

    // ── Hotkeys toggle ────────────────────────────────────────────────────────

    private async Task ToggleHotkeysAsync()
    {
        if (_service is not CompanionServiceAdapter adapter) return;
        IsBusy = true;
        var enable = !_hotkeysEnabled;
        StatusMessage = enable ? L.T("Включение горячих клавиш…") : L.T("Отключение горячих клавиш…");
        try
        {
            var (success, error) = await adapter.ToggleHotkeysAsync(
                _selectedGame, enable);
            if (success)
            {
                HotkeysEnabled = enable;
                StatusMessage = enable
                    ? L.T("Горячие клавиши активированы: {0}.", HotkeySummary)
                    : L.T("Горячие клавиши отключены.");
            }
            else
            {
                StatusMessage = (enable ? L.T("Не удалось включить горячие клавиши: {0}", error) : L.T("Не удалось отключить горячие клавиши: {0}", error));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("Ошибка горячих клавиш: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Validates the edited keys (one modifier + letter each, no duplicates) and saves them.</summary>
    public async Task SaveHotkeysAsync()
    {
        HotkeyLayout layout;
        try
        {
            layout = HotkeyLayout.Parse(string.Join('\n', Hotkeys.Select(row => row.Action + "=" + row.Key)));
        }
        catch (HotkeyLayoutException exception)
        {
            StatusMessage = L.T("Клавиши не сохранены: {0}", exception.Message);
            return;
        }

        if (_service is not CompanionServiceAdapter adapter)
        {
            StatusMessage = L.T("Клавиши сохранены: {0}.", HotkeySummary);
            return;
        }

        IsBusy = true;
        try
        {
            var (success, error) = await adapter.SaveHotkeyLayoutAsync(_selectedGame, layout);
            foreach (var binding in layout.Bindings)
            {
                if (Hotkeys.FirstOrDefault(row => row.Action == HotkeyLayout.ActionName(binding.Action)) is { } row) row.Key = binding.Gesture.ToString();
            }
            OnPropertyChanged(nameof(HotkeySummary));
            StatusMessage = success ? L.T("Клавиши сохранены: {0}.", HotkeySummary) : L.T("Клавиши сохранены, но перезапуск не удался: {0}", error ?? string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.T("Клавиши не сохранены: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

}

/// <summary>A row of the companion's game list; checked rows get «Установить / обновить во все отмеченные».</summary>
public sealed class CompanionGameRow(string releaseId, string title, bool gameFound, string path, string status) : ObservableViewModel
{
    private bool _isChecked = gameFound;

    public string ReleaseId { get; } = releaseId;
    public string Title { get; } = title;
    public bool GameFound { get; } = gameFound;
    public string Path { get; } = path;
    public string Status { get; } = status;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value && GameFound);
    }
}
