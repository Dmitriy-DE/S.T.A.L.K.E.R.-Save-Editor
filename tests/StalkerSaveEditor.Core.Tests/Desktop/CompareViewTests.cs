using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using StalkerSaveEditor.Desktop;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection(AvaloniaViewTestGroup.Name)]
public sealed class CompareViewTests
{
    [Fact]
    public void Compare_visibility_tracks_the_library_tab_selection()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        viewModel.SelectedTab = "overview";

        var root = MainWindow.BuildRoot(viewModel);
        var compare = root.GetLogicalDescendants().OfType<Control>()
            .Single(control => control.Name == "compare-screen");

        Assert.False(compare.IsVisible);
        viewModel.SelectedTab = "compare";
        Assert.True(compare.IsVisible);
        viewModel.SelectedTab = "inventory";
        Assert.False(compare.IsVisible);
    }

    [Fact]
    public void Compare_is_a_reachable_selected_save_workspace_screen()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = CopyFixture("xray-money-cop-source.sav", directory.Path, "before.sav");
        var currentPath = CopyFixture("xray-money-cop-expected.sav", directory.Path, "current.sav");
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(sourcePath));
        Assert.True(viewModel.AddPreviewSave(currentPath));
        viewModel.Compare.Selected = Assert.Single(viewModel.Compare.Candidates);
        viewModel.SelectedTab = "compare";

        var root = MainWindow.BuildRoot(viewModel);
        var compare = root.GetLogicalDescendants().OfType<Control>()
            .Single(control => control.Name == "compare-screen");

        Assert.True(compare.IsVisible);
        Assert.NotEmpty(viewModel.Compare.Rows);
        Assert.True(viewModel.IsSaveWorkspace);
    }

    [Fact]
    public void Timeline_adjacent_comparison_opens_compare_with_the_previous_save_selected()
    {
        using var directory = new TemporaryDirectory();
        var previousPath = CopyFixture("xray-money-cop-source.sav", directory.Path, "previous.sav");
        var currentPath = CopyFixture("xray-money-cop-expected.sav", directory.Path, "current.sav");
        File.SetLastWriteTimeUtc(previousPath, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(currentPath, DateTime.UtcNow.AddMinutes(-1));
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(previousPath));
        Assert.True(viewModel.AddPreviewSave(currentPath));
        var latest = viewModel.Timeline.Entries.Single(entry => entry.Save.FilePath == currentPath);

        Assert.True(viewModel.Timeline.CanCompare(latest));
        viewModel.Timeline.CompareAdjacent(latest);

        Assert.Equal("compare", viewModel.SelectedTab);
        Assert.Equal(currentPath, viewModel.SelectedSave?.FilePath);
        Assert.Equal(previousPath, viewModel.Compare.Selected?.Path);
        Assert.True(viewModel.ShowCompareScreen);
        Assert.NotEmpty(viewModel.Compare.Rows);
    }

    [Fact]
    public void Unsupported_compare_actions_and_categories_are_disabled_with_reasons()
    {
        var viewModel = new CompareViewModel(_ => null);
        var view = CompareView.Build(viewModel);
        var noCandidates = view.GetLogicalDescendants().OfType<TextBlock>()
            .Single(block => block.Name == "compare-no-candidates");
        Assert.Equal(L.T("Нет других сейвов этой игры или бэкапов для сравнения."), noCandidates.Text);
        var actionButtons = view.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.Name is "compare-export" or "compare-copy-list" or "compare-apply")
            .ToArray();

        Assert.Equal(3, actionButtons.Length);
        Assert.All(actionButtons, button =>
        {
            Assert.False(button.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button)?.ToString()));
        });

        var unchanged = view.GetLogicalDescendants().OfType<CheckBox>()
            .Single(checkBox => checkBox.Name == "compare-show-unchanged");
        Assert.False(unchanged.IsEnabled);
        Assert.Equal(StalkerTheme.BrushTextSecondary, unchanged.Foreground);
        Assert.Equal(StalkerTheme.BrushTextMuted, Assert.IsType<TextBlock>(unchanged.Content).Foreground);
        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(unchanged)?.ToString()));

        var picker = view.GetLogicalDescendants().OfType<ComboBox>()
            .Single(comboBox => comboBox.Name == "compare-candidate-picker");
        Assert.Equal(StalkerTheme.BrushTextPrimary, picker.Foreground);

        var categories = view.GetLogicalDescendants().OfType<ItemsControl>()
            .Single(control => control.Name == "compare-category-filters");
        var template = Assert.IsType<FuncDataTemplate<CompareFilterOption>>(categories.ItemTemplate);
        var unsupported = new CompareFilterOption("world", "Мир", 0, IsSupported: false, DisabledReason: "No world fields.");
        var worldFilter = template.Build(unsupported) ?? throw new InvalidOperationException("Compare category template returned no control.");
        worldFilter.DataContext = unsupported;

        var worldButton = Assert.IsType<Button>(worldFilter);
        Assert.False(worldButton.IsEnabled);
        Assert.Equal("No world fields.", ToolTip.GetTip(worldButton)?.ToString());
        var worldLabel = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(worldButton.Content).Children[0]);
        Assert.Equal(StalkerTheme.BrushTextMuted, worldLabel.Foreground);
    }

    [Fact]
    public void Difference_rows_follow_their_current_data_context()
    {
        var view = CompareView.Build(new CompareViewModel(_ => null));
        var list = view.GetLogicalDescendants().OfType<ItemsControl>()
            .Single(control => control.Name == "compare-rows-list");
        var template = Assert.IsType<FuncDataTemplate<CompareDisplayRow>>(list.ItemTemplate);
        var first = new CompareDisplayRow("Money", "10", "20", "character", "Character", "changed", "Changed");
        var second = new CompareDisplayRow("Rifle", "—", "1", "items", "Items", "added", "Added");
        var row = template.Build(first) ?? throw new InvalidOperationException("Compare row template returned no control.");
        row.DataContext = second;

        var text = row.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
        Assert.Contains("Rifle", text);
        Assert.Contains("—", text);
        Assert.Contains("1", text);
        Assert.DoesNotContain("Money", text);
    }

    private static string CopyFixture(string name, string directory, string targetName)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-money", name);
        var target = Path.Combine(directory, targetName);
        File.Copy(fixture, target);
        return target;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stalker-compare-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
