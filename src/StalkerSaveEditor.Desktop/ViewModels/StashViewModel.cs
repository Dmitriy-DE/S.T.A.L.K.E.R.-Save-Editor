using StalkerSaveEditor.Desktop.Services;
using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class StashItemViewModel : ObservableViewModel
{
    private bool _isTaken;

    public StashItemViewModel(ushort handle, string typeKey, string displayName, uint count, bool canEdit = true, string? disabledReason = null)
    {
        Handle = handle;
        TypeKey = typeKey;
        DisplayName = displayName;
        Count = count;
        CountDisplay = count > 1 ? $"× {count}" : string.Empty;
        CanEdit = canEdit;
        DisabledReason = disabledReason ?? (canEdit ? L.T("Переместить предмет в инвентарь персонажа") : L.T("Перемещение из тайников не поддерживается"));
    }

    public ushort Handle { get; }
    public string TypeKey { get; }
    public string DisplayName { get; }
    public uint Count { get; }
    public string CountDisplay { get; }
    public bool CanEdit { get; }
    public string DisabledReason { get; }

    public bool IsTaken
    {
        get => _isTaken;
        set => SetProperty(ref _isTaken, value);
    }
}

public sealed class StashViewModel : ObservableViewModel
{
    public StashViewModel(ushort handle, string name, string? level, IEnumerable<StashItemViewModel> items)
    {
        Handle = handle;
        Name = string.IsNullOrWhiteSpace(name) ? L.T("Тайник 0x{0:X4}", handle) : name;
        Level = string.IsNullOrWhiteSpace(level) ? L.T("Неизвестно") : level;
        Items = new ObservableCollection<StashItemViewModel>(items);
    }

    public ushort Handle { get; }
    public string Name { get; }
    public string Level { get; }
    public ObservableCollection<StashItemViewModel> Items { get; }

    /// <summary>Items queued to be put or created in this box on save.</summary>
    public ObservableCollection<string> Pending { get; } = [];

    public int ItemCount => Items.Count;
    public string HeaderDisplay => L.T("{0} ({1}) — {2} предм.", Name, Level, Items.Count);
}
