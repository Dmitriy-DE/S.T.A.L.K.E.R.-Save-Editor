using System.Collections.ObjectModel;
using System.Windows.Input;
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

    public CompanionViewModel(ICompanionService? service = null)
    {
        _service = service ?? MockCompanionService.Instance;

        InstallCommand = new RelayCommand(async () => await InstallAsync(), () => !_isBusy && _state == CompanionState.NotInstalled);
        UninstallCommand = new RelayCommand(async () => await UninstallAsync(), () => !_isBusy && _state != CompanionState.NotInstalled);
        PingCommand = new RelayCommand(async () => await PingAsync(), () => !_isBusy && _state == CompanionState.Active);
        RefreshCommand = new RelayCommand(async () => await RefreshStatusAsync(), () => !_isBusy);

        Hotkeys = [];

        _ = RefreshStatusAsync();
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
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanInstall => !_isBusy && _state == CompanionState.NotInstalled;
    public bool CanUninstall => !_isBusy && _state != CompanionState.NotInstalled;
    public bool CanPing => !_isBusy && _state == CompanionState.Active;

    public ObservableCollection<CompanionHotkeyItemViewModel> Hotkeys { get; }

    public RelayCommand InstallCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand PingCommand { get; }
    public RelayCommand RefreshCommand { get; }

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
                ? $"{DateTime.UtcNow.Subtract(status.LastPing.Value).TotalSeconds:F0} сек назад"
                : "—";

            var hotkeys = await _service.GetHotkeysAsync(_selectedGame);
            Hotkeys.Clear();
            foreach (var hk in hotkeys)
            {
                Hotkeys.Add(new CompanionHotkeyItemViewModel(hk, (action, newKey) =>
                {
                    _ = _service.UpdateHotkeyAsync(_selectedGame, action, newKey);
                }));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallAsync()
    {
        IsBusy = true;
        StatusMessage = "Установка компаньона в gamedata...";
        try
        {
            var ok = await _service.InstallAsync(_selectedGame);
            StatusMessage = ok ? "Компаньон успешно установлен!" : "Не удалось установить компаньон.";
            await RefreshStatusAsync();
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
        StatusMessage = "Удаление компаньона...";
        try
        {
            var ok = await _service.UninstallAsync(_selectedGame);
            StatusMessage = ok ? "Компаньон удалён." : "Не удалось удалить компаньон.";
            await RefreshStatusAsync();
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

    public async Task PingAsync()
    {
        IsBusy = true;
        StatusMessage = "Проверка связи через mtime...";
        try
        {
            var latency = await _service.PingAsync(_selectedGame);
            if (latency.HasValue)
            {
                PingText = $"{latency.Value.TotalMilliseconds:F0} мс";
                StatusMessage = $"Связь стабильна. Задержка mtime: {latency.Value.TotalMilliseconds:F0} мс";
            }
            else
            {
                PingText = "Нет ответа";
                StatusMessage = "Компаньон не отвечает. Убедитесь, что игра запущена.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка пинга: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
