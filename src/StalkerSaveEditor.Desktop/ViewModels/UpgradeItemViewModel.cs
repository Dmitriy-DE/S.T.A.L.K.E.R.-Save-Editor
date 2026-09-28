using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class UpgradeItemViewModel : ObservableViewModel
{
    private bool _isInstalled;

    public UpgradeItemViewModel(UpgradeDefinition definition, bool isInstalled, bool canEdit, string? disabledReason = null)
    {
        Definition = definition;
        Key = definition.Key;
        DisplayName = definition.DisplayName ?? definition.Key;
        Category = definition.Category ?? "Апгрейд";
        _isInstalled = isInstalled;
        OriginalInstalled = isInstalled;
        CanEdit = canEdit;
        DisabledReason = disabledReason ?? (canEdit ? "Установить или снять апгрейд" : "Модификации оружия и брони не поддерживаются форматом");
    }

    public UpgradeItemViewModel(string key, string displayName, string category, bool isInstalled, bool canEdit, string? disabledReason = null)
    {
        Definition = null;
        Key = key;
        DisplayName = displayName;
        Category = category;
        _isInstalled = isInstalled;
        OriginalInstalled = isInstalled;
        CanEdit = canEdit;
        DisabledReason = disabledReason ?? (canEdit ? "Установить или снять апгрейд" : "Модификации оружия и брони не поддерживаются форматом");
    }

    public UpgradeDefinition? Definition { get; }
    public string Key { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool OriginalInstalled { get; }
    public bool CanEdit { get; }
    public string DisabledReason { get; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }
}
