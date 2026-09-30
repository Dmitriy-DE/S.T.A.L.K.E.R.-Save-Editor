using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class CatalogItemEntry(ItemDefinition definition, string localizedName) : ObservableViewModel
{
    public ItemDefinition Definition { get; } = definition;
    public string Key => Definition.Key;
    public string DisplayName { get; } = localizedName;
    public string Category => Definition.Category ?? "other";
}

public sealed class AddItemViewModel : ObservableViewModel
{
    private readonly List<CatalogItemEntry> _allItems;
    private string _searchText = string.Empty;
    private CatalogItemEntry? _selectedItem;
    private uint _quantity = 1;

    public AddItemViewModel(ItemCatalog? catalog, OfficialNamesCatalog officialNames, string releaseId, string locale = "ru")
    {
        _allItems = [];
        if (catalog is not null)
        {
            foreach (var item in catalog.Items)
            {
                var localized = officialNames.Resolve(releaseId, "items", item.Key, locale) ?? item.DisplayName ?? item.Key;
                _allItems.Add(new CatalogItemEntry(item, localized));
            }
        }
#pragma warning disable CA1309 // user-facing names sort in the user's language order
        _allItems.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
#pragma warning restore CA1309
        FilteredItems = new ObservableCollection<CatalogItemEntry>(_allItems);
        SelectedItem = FilteredItems.FirstOrDefault();
    }

    public ObservableCollection<CatalogItemEntry> FilteredItems { get; }

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

    public CatalogItemEntry? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public uint Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, Math.Max(1, value));
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var query = _searchText.Trim();
        foreach (var item in _allItems)
        {
            if (string.IsNullOrEmpty(query) ||
                item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
        SelectedItem = FilteredItems.FirstOrDefault();
    }
}
