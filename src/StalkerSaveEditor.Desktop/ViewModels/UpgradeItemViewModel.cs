using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class UpgradeItemViewModel : ObservableViewModel
{
    private bool _isInstalled;

    public UpgradeItemViewModel(UpgradeDefinition definition, bool isInstalled, bool canEdit)
    {
        Definition = definition;
        Key = definition.Key;
        DisplayName = definition.DisplayName ?? definition.Key;
        Category = definition.Category ?? "Апгрейд";
        _isInstalled = isInstalled;
        OriginalInstalled = isInstalled;
        CanEdit = canEdit;
    }

    public UpgradeDefinition Definition { get; }
    public string Key { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool OriginalInstalled { get; }
    public bool CanEdit { get; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }
}
