using StalkerSaveEditor.Desktop.Services;
namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class FactionRelationViewModel : ObservableViewModel
{
    private int _goodwill;

    public FactionRelationViewModel(string community, string displayName, int goodwill, bool canEdit, string? disabledReason = null)
    {
        Community = community;
        DisplayName = displayName;
        _goodwill = goodwill;
        OriginalGoodwill = goodwill;
        CanEdit = canEdit;
        DisabledReason = disabledReason ?? (canEdit ? L.T("Изменить отношение группировки") : L.T("Редактирование отношений фракций не поддерживается данным форматом"));
    }

    public string Community { get; }
    public string DisplayName { get; }
    public int OriginalGoodwill { get; }
    public bool CanEdit { get; }
    public string DisabledReason { get; }

    public int Goodwill
    {
        get => _goodwill;
        set
        {
            if (SetProperty(ref _goodwill, value))
            {
                OnPropertyChanged(nameof(Attitude));
                OnPropertyChanged(nameof(AttitudeBadgeColor));
                OnPropertyChanged(nameof(GoodwillDisplay));
            }
        }
    }

    public string GoodwillDisplay => $"{_goodwill:+0;-0;0}";

    public string Attitude => _goodwill switch
    {
        >= 1000 => L.T("Друг"),
        <= -1000 => L.T("Враг"),
        _ => L.T("Нейтрал"),
    };

    public string AttitudeBadgeColor => _goodwill switch
    {
        >= 1000 => "#7BCB62",
        <= -1000 => "#D85A45",
        _ => "#D6A62D",
    };
}
