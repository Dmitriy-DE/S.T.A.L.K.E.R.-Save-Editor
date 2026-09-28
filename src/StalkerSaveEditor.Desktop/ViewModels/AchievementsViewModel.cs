using System.Collections.ObjectModel;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class AchievementsViewModel : ObservableViewModel
{
    private readonly ISteamAchievementsAdapter _adapter;

    private int _selectedAppId = 41700; // Default to Call of Pripyat
    private string _searchText = string.Empty;
    private bool _isLoading;
    private bool _isMutating;
    private string _statusMessage = string.Empty;

    // Confirmation dialog state
    private bool _showConfirmDialog;
    private AchievementItemViewModel? _pendingAchievement;
    private bool _pendingNewState;

    public AchievementsViewModel(ISteamAchievementsAdapter? adapter = null)
    {
        _adapter = adapter ?? new SteamAchievementsAdapter();

        AllAchievements = new ObservableCollection<AchievementItemViewModel>();
        FilteredAchievements = new ObservableCollection<AchievementItemViewModel>();

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        RequestToggleCommand = new RelayCommand<AchievementItemViewModel>(RequestToggle);
        ConfirmToggleCommand = new RelayCommand(async () => await ConfirmToggleAsync(), () => CanConfirmToggle);
        CancelToggleCommand = new RelayCommand(CancelToggle);
    }

    public ObservableCollection<AchievementItemViewModel> AllAchievements { get; }
    public ObservableCollection<AchievementItemViewModel> FilteredAchievements { get; }

    public int SelectedAppId
    {
        get => _selectedAppId;
        set
        {
            if (SetProperty(ref _selectedAppId, value))
            {
                OnPropertyChanged(nameof(GameName));
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(AvailabilityMessage));
                _ = RefreshAsync();
            }
        }
    }

    public string GameName => SelectedAppId switch
    {
        4500 => "S.T.A.L.K.E.R.: Тень Чернобыля",
        20510 => "S.T.A.L.K.E.R.: Чистое Небо",
        41700 => "S.T.A.L.K.E.R.: Зов Припяти",
        1643320 => "S.T.A.L.K.E.R. 2: Heart of Chornobyl",
        _ => "S.T.A.L.K.E.R."
    };

    public bool IsAvailable => _adapter.IsAvailable(SelectedAppId);

    public string AvailabilityMessage => _adapter.GetAvailabilityMessage(SelectedAppId)
        ?? (IsAvailable ? "Steam доступен" : "Steam не запущен или недоступен.");

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsMutating
    {
        get => _isMutating;
        set
        {
            if (SetProperty(ref _isMutating, value))
            {
                OnPropertyChanged(nameof(CanConfirmToggle));
                ConfirmToggleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // Progress stats
    public int TotalCount => AllAchievements.Count;
    public int UnlockedCount => AllAchievements.Count(a => a.IsAchieved);
    public double ProgressFraction => TotalCount == 0 ? 0.0 : (double)UnlockedCount / TotalCount;
    public double ProgressPercentage => Math.Round(ProgressFraction * 100);
    public string ProgressText => $"{UnlockedCount} из {TotalCount} получено ({ProgressPercentage:F0}%)";

    // Dialog state
    public bool ShowConfirmDialog
    {
        get => _showConfirmDialog;
        set
        {
            if (SetProperty(ref _showConfirmDialog, value))
            {
                OnPropertyChanged(nameof(CanConfirmToggle));
                ConfirmToggleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AchievementItemViewModel? PendingAchievement
    {
        get => _pendingAchievement;
        private set
        {
            if (SetProperty(ref _pendingAchievement, value))
            {
                OnPropertyChanged(nameof(ConfirmDialogTitle));
                OnPropertyChanged(nameof(ConfirmDialogMessage));
            }
        }
    }

    public bool PendingNewState
    {
        get => _pendingNewState;
        private set
        {
            if (SetProperty(ref _pendingNewState, value))
            {
                OnPropertyChanged(nameof(ConfirmDialogTitle));
                OnPropertyChanged(nameof(ConfirmDialogMessage));
            }
        }
    }

    public string ConfirmDialogTitle => PendingNewState ? "РАЗБЛОКИРОВАТЬ ДОСТИЖЕНИЕ" : "СНЯТЬ ДОСТИЖЕНИЕ";

    public string ConfirmDialogMessage => PendingAchievement is null
        ? string.Empty
        : $"Вы действительно хотите {(PendingNewState ? "разблокировать" : "снять")} достижение «{PendingAchievement.Name}» в Steam?";

    public bool CanConfirmToggle => ShowConfirmDialog && PendingAchievement is not null && !IsMutating;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand<AchievementItemViewModel> RequestToggleCommand { get; }
    public RelayCommand ConfirmToggleCommand { get; }
    public RelayCommand CancelToggleCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = $"Запрос достижений для {GameName}...";

        try
        {
            AllAchievements.Clear();
            var items = await _adapter.ListAsync(SelectedAppId);
            foreach (var item in items)
            {
                AllAchievements.Add(new AchievementItemViewModel(item));
            }

            ApplyFilter();
            UpdateProgress();

            StatusMessage = AllAchievements.Count == 0
                ? "Достижения не найдены (проверьте подключение к Steam)."
                : $"Загружено {AllAchievements.Count} достижений.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки достижений: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(AvailabilityMessage));
        }
    }

    public void RequestToggle(AchievementItemViewModel? item)
    {
        if (item is null || IsMutating) return;
        PendingAchievement = item;
        PendingNewState = !item.IsAchieved;
        ShowConfirmDialog = true;
    }

    public void CancelToggle()
    {
        ShowConfirmDialog = false;
        PendingAchievement = null;
    }

    public async Task ConfirmToggleAsync()
    {
        var item = PendingAchievement;
        var newState = PendingNewState;
        if (item is null || IsMutating)
        {
            ShowConfirmDialog = false;
            return;
        }

        ShowConfirmDialog = false;
        IsMutating = true;
        StatusMessage = $"Обновление «{item.Name}» в Steam...";

        try
        {
            var result = await _adapter.SetAsync(SelectedAppId, item.ApiName, newState);
            item.IsAchieved = result.Achieved;
            item.UnlockTime = result.UnlockTime;
            UpdateProgress();
            StatusMessage = $"Достижение «{item.Name}» успешно {(newState ? "получено" : "снято")} в Steam.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка изменения достижения: {ex.Message}";
        }
        finally
        {
            IsMutating = false;
            PendingAchievement = null;
        }
    }

    private void ApplyFilter()
    {
        FilteredAchievements.Clear();
        var query = SearchText?.Trim() ?? string.Empty;

        foreach (var item in AllAchievements)
        {
            if (string.IsNullOrEmpty(query) ||
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.ApiName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredAchievements.Add(item);
            }
        }
    }

    private void UpdateProgress()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressText));
    }
}
