using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class EncyclopediaItemViewModel : ObservableViewModel
{
    private readonly Func<bool> _canAdd;
    private readonly Func<bool> _canSpawn;
    private readonly Action _add;
    private readonly Func<Task<(bool Success, string Message)>> _spawn;
    private bool _canAddToSave;
    private bool _canSpawnViaCompanion;

    internal EncyclopediaItemViewModel(
        string releaseId,
        ItemDefinition definition,
        string displayName,
        Func<bool> canAdd,
        Func<bool> canSpawn,
        Action add,
        Func<Task<(bool Success, string Message)>> spawn)
    {
        ReleaseId = releaseId;
        Definition = definition;
        DisplayName = displayName;
        _canAdd = canAdd;
        _canSpawn = canSpawn;
        _add = add;
        _spawn = spawn;
        RefreshAvailability();
    }

    public string ReleaseId { get; }
    public ItemDefinition Definition { get; }
    public string Key => Definition.Key;
    public string Section => Definition.Key;
    public string DisplayName { get; }
    public string? Category => Definition.Category;
    public double? Weight => Definition.UnitWeight;
    public int? Cost => Definition.Cost;
    public bool CanAddToSave => _canAddToSave;
    public bool CanSpawnViaCompanion => _canSpawnViaCompanion;
    public string AddDisabledReason => CanAddToSave ? string.Empty : L.T("Выберите совместимое сохранение с поддержкой добавления предметов.");
    public string SpawnDisabledReason => CanSpawnViaCompanion ? string.Empty : L.T("Выберите эту игру в Компаньоне и подключитесь к запущенной игре.");
    public string WeightDisplay => Weight is { } weight ? weight.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) : "—";
    public string CostDisplay => Cost?.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) ?? "—";

    internal void RefreshAvailability()
    {
        var canAdd = _canAdd();
        if (SetProperty(ref _canAddToSave, canAdd, nameof(CanAddToSave)))
            OnPropertyChanged(nameof(AddDisabledReason));
        var canSpawn = _canSpawn();
        if (SetProperty(ref _canSpawnViaCompanion, canSpawn, nameof(CanSpawnViaCompanion)))
            OnPropertyChanged(nameof(SpawnDisabledReason));
    }

    public void AddToSave()
    {
        RefreshAvailability();
        if (CanAddToSave) _add();
    }

    public async Task SpawnViaCompanionAsync()
    {
        RefreshAvailability();
        if (!CanSpawnViaCompanion) return;
        var result = await _spawn();
        StatusMessage = result.Message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }
}

/// <summary>Installed-game item catalogue and the two existing save/Companion actions.</summary>
public sealed class EncyclopediaViewModel : ObservableViewModel
{
    private readonly Func<IReadOnlyList<GameContent>> _contents;
    private readonly Func<string, bool> _canAdd;
    private readonly Action<string, string> _add;
    private readonly Func<string, bool> _canSpawn;
    private readonly Func<string, string, Task<(bool Success, string Message)>> _spawn;
    private string _selectedGameReleaseId = string.Empty;
    private string _statusMessage = string.Empty;

    public EncyclopediaViewModel(
        Func<IReadOnlyList<GameContent>> contents,
        Func<string, bool> canAdd,
        Action<string, string> add,
        Func<string, bool> canSpawn,
        Func<string, string, Task<(bool Success, string Message)>> spawn)
    {
        _contents = contents ?? throw new ArgumentNullException(nameof(contents));
        _canAdd = canAdd ?? throw new ArgumentNullException(nameof(canAdd));
        _add = add ?? throw new ArgumentNullException(nameof(add));
        _canSpawn = canSpawn ?? throw new ArgumentNullException(nameof(canSpawn));
        _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
        Refresh();
    }

    public ObservableCollection<KeyValuePair<string, string>> AvailableGames { get; } = [];
    public ObservableCollection<EncyclopediaItemViewModel> Items { get; } = [];

    public string SelectedGameReleaseId
    {
        get => _selectedGameReleaseId;
        set
        {
            if (SetProperty(ref _selectedGameReleaseId, value ?? string.Empty)) LoadItems();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void Refresh()
    {
        var contents = _contents();
        var previous = SelectedGameReleaseId;
        AvailableGames.Clear();
        foreach (var content in contents)
        {
            AvailableGames.Add(new KeyValuePair<string, string>(content.Status.ReleaseId, GameTitle(content.Status.ReleaseId)));
        }

        var next = AvailableGames.Any(game => game.Key == previous) ? previous : AvailableGames.FirstOrDefault().Key ?? string.Empty;
        if (_selectedGameReleaseId == next) LoadItems();
        else SelectedGameReleaseId = next;
        OnPropertyChanged(nameof(HasInstalledGames));
    }

    public bool HasInstalledGames => AvailableGames.Count > 0;

    public void RefreshAvailability()
    {
        foreach (var item in Items) item.RefreshAvailability();
    }

    public void AddToSave(EncyclopediaItemViewModel? item)
    {
        if (item is null) return;
        item.AddToSave();
        RefreshAvailability();
    }

    public async Task SpawnViaCompanionAsync(EncyclopediaItemViewModel? item)
    {
        if (item is null) return;
        await item.SpawnViaCompanionAsync();
        StatusMessage = item.StatusMessage;
    }

    private void LoadItems()
    {
        Items.Clear();
        var content = _contents().FirstOrDefault(value => value.Status.ReleaseId == SelectedGameReleaseId);
        if (content is null)
        {
            StatusMessage = L.T("Установленный каталог предметов не загружен.");
            OnPropertyChanged(nameof(HasItems));
            return;
        }

        StatusMessage = content.Status.ModName is { Length: > 0 } modName
            ? L.T("Источник: установленные файлы игры и мод «{0}».", modName)
            : L.T("Источник: файлы выбранной установленной игры.");
        // The installed files name items in the language of that installation; the interface language comes first.
        var items = content.Bundle.Items.Items
            .Select(item => (Item: item, Name: SaveNaming.ItemName(content.Status.ReleaseId, item.Key, item.DisplayName)))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => (entry.Name, item: entry.Item))
            .Select(entry => new EncyclopediaItemViewModel(
                content.Status.ReleaseId,
                entry.item,
                entry.Name,
                () => _canAdd(content.Status.ReleaseId),
                () => _canSpawn(content.Status.ReleaseId),
                () => _add(content.Status.ReleaseId, entry.item.Key),
                () => _spawn(content.Status.ReleaseId, entry.item.Key)))
            .ToArray();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(nameof(HasItems));
    }

    public bool HasItems => Items.Count > 0;

    private static string GameTitle(string releaseId) => releaseId switch
    {
        "stalker-soc" => L.T("Тень Чернобыля"),
        "stalker-cs" => L.T("Чистое Небо"),
        "stalker-cop" => L.T("Зов Припяти"),
        _ => releaseId,
    };
}
