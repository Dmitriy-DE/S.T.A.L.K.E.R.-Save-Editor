using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection(AvaloniaViewTestGroup.Name)]
public sealed class InventoryViewBindingTests
{
    [Fact]
    public void Inventory_rows_follow_their_data_context()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        var first = Item("first item", "wpn_first");
        var second = Item("selected item", "wpn_selected");

        var view = InventoryView.Build(viewModel);
        var list = view.GetVisualDescendants()
            .OfType<ListBox>()
            .Single(candidate => candidate.ItemTemplate is FuncDataTemplate<InventoryLineViewModel>);
        var template = Assert.IsType<FuncDataTemplate<InventoryLineViewModel>>(list.ItemTemplate);
        var row = template.Build(first) ?? throw new InvalidOperationException("Inventory row template returned no control.");
        row.DataContext = second;
        viewModel.SelectedItem = second;

        var labels = row.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text).ToArray();
        Assert.Contains(second.Name, labels);
        Assert.DoesNotContain(first.Name, labels);

        var key = row.GetVisualDescendants().OfType<TextBlock>().Single(label => label.Text == second.TypeKey);
        Assert.True(second.IsSelected);
        Assert.Equal(StalkerTheme.BrushAccentForeground, key.Foreground);
    }

    [Fact]
    public void Inventory_rows_keep_the_icon_and_fit_in_a_compact_height()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        var list = InventoryView.Build(viewModel).GetVisualDescendants()
            .OfType<ListBox>()
            .Single(candidate => candidate.ItemTemplate is FuncDataTemplate<InventoryLineViewModel>);
        var template = Assert.IsType<FuncDataTemplate<InventoryLineViewModel>>(list.ItemTemplate);
        var row = Assert.IsType<Grid>(template.Build(Item("compact item", "wpn_compact")));
        var icon = Assert.IsType<Border>(row.Children[0]);

        Assert.Equal(40, row.MinHeight);
        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
    }

    [Fact]
    public void Unknown_stack_and_placement_values_stay_unknown()
    {
        var item = new InventoryLineViewModel(
            "unknown item",
            "item_unknown",
            handle: 1,
            category: "other",
            count: null,
            canEditCount: false,
            condition: null,
            canEditCondition: false,
            placement: null,
            canEditPlacement: false,
            upgrades: [],
            canEditUpgrades: false);

        Assert.Equal("—", item.OriginalCountDisplay);
        Assert.Equal("—", item.ConditionDisplay);
        Assert.Equal("—", item.PlacementDisplay);
        Assert.Equal("#716F67", item.ConditionColor);
    }

    [Fact]
    public void Add_item_opens_inline_catalog_and_cancel_returns_to_inventory()
    {
        using var directory = new TemporaryDirectory();
        var savePath = Path.Combine(directory.Path, "slot.sav");
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-add", "xray-add-cop-ammo-source.sav");
        File.Copy(fixturePath, savePath);
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(savePath));
        Assert.True(viewModel.SelectedSave!.CanAddItems);
        var view = Assert.IsType<Grid>(InventoryView.Build(viewModel));
        var screenLayer = Assert.IsType<Grid>(view.Children[2]);
        var workspace = screenLayer.Children[0];
        var catalogPanel = Assert.IsType<Border>(screenLayer.Children[1]);
        var addButton = view.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Content?.ToString() == "+ Добавить предмет");

        addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(catalogPanel.IsVisible);
        Assert.False(workspace.IsVisible);
        Assert.False(viewModel.HasDraftChanges);

        var cancelButton = catalogPanel.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Content?.ToString() == "Отмена");
        cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(catalogPanel.IsVisible);
        Assert.True(workspace.IsVisible);
        Assert.False(viewModel.HasDraftChanges);
    }

    private static InventoryLineViewModel Item(string name, string key) => new(
        name,
        key,
        handle: 1,
        category: "weapon",
        count: 1,
        canEditCount: true,
        condition: 0.75f,
        canEditCondition: true,
        placement: "ruck",
        canEditPlacement: true,
        upgrades: [],
        canEditUpgrades: true);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stalker-inventory-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
