using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class StashItemViewModel : ObservableViewModel
{
    private bool _isTaken;

    public StashItemViewModel(ushort handle, string typeKey, string displayName, uint count)
    {
        Handle = handle;
        TypeKey = typeKey;
        DisplayName = displayName;
        Count = count;
        CountDisplay = count > 1 ? $"× {count}" : string.Empty;
    }

    public ushort Handle { get; }
    public string TypeKey { get; }
    public string DisplayName { get; }
    public uint Count { get; }
    public string CountDisplay { get; }

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
        Name = string.IsNullOrWhiteSpace(name) ? $"Тайник 0x{handle:X4}" : name;
        Level = string.IsNullOrWhiteSpace(level) ? "Неизвестно" : level;
        Items = new ObservableCollection<StashItemViewModel>(items);
    }

    public ushort Handle { get; }
    public string Name { get; }
    public string Level { get; }
    public ObservableCollection<StashItemViewModel> Items { get; }

    public int ItemCount => Items.Count;
    public string HeaderDisplay => $"{Name} ({Level}) — {Items.Count} предм.";
}
