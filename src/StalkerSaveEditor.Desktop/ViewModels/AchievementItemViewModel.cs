using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class AchievementItemViewModel : ObservableViewModel
{
    private bool _isAchieved;
    private uint _unlockTime;

    public AchievementItemViewModel(SteamAchievement model)
    {
        Model = model;
        _isAchieved = model.Achieved;
        _unlockTime = model.UnlockTime;
    }

    public SteamAchievement Model { get; }

    public string ApiName => Model.ApiName;
    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? Model.ApiName : Model.Name;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? "Скрытое достижение" : Model.Description;
    public bool Hidden => Model.Hidden;

    public bool IsAchieved
    {
        get => _isAchieved;
        set
        {
            if (SetProperty(ref _isAchieved, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(IconText));
            }
        }
    }

    public uint UnlockTime
    {
        get => _unlockTime;
        set
        {
            if (SetProperty(ref _unlockTime, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public DateTime? UnlockDateTime => _unlockTime > 0
        ? DateTimeOffset.FromUnixTimeSeconds(_unlockTime).ToLocalTime().DateTime
        : null;

    public string StatusText => IsAchieved
        ? (UnlockDateTime.HasValue ? $"Получено: {UnlockDateTime:yyyy-MM-dd HH:mm}" : "Получено")
        : "Не получено";

    public string StatusBadgeColor => IsAchieved ? "#4E7A4A" : "#333333";

    public string IconText => IsAchieved ? "🏆" : "🔒";
}
