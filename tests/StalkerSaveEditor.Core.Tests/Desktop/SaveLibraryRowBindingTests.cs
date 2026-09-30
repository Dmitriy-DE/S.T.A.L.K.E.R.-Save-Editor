using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using Avalonia.VisualTree;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection(AvaloniaViewTestGroup.Name)]
public sealed class SaveLibraryRowBindingTests
{
    [Fact]
    public void Save_rows_follow_their_data_context()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        var first = Summary(directory.Path, "first.sav");
        var second = Summary(directory.Path, "selected.sav", lastModified: new DateTime(2026, 9, 30, 15, 20, 0));
        viewModel.Saves.Add(first);
        viewModel.Saves.Add(second);

        var root = MainWindow.BuildRoot(viewModel);
        var saveList = root.GetVisualDescendants()
            .OfType<ListBox>()
            .Single(list => list.ItemTemplate is FuncDataTemplate<SaveFileSummary>);
        var template = Assert.IsType<FuncDataTemplate<SaveFileSummary>>(saveList.ItemTemplate);
        var row = template.Build(first) ?? throw new InvalidOperationException("Save row template returned no control.");
        row.DataContext = second;

        var rowText = row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains(second.DisplayName, rowText);
        Assert.DoesNotContain(first.DisplayName, rowText);
        var date = row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "save-library-date");
        Assert.Equal("30.09.26 15:20", second.LibraryMetadataDisplay);
        Assert.Equal(second.LibraryMetadataDisplay, date.Text);
        Assert.DoesNotContain("Call of Pripyat ·", date.Text);
    }

    [Fact]
    public void Library_metadata_keeps_the_existing_stalker_two_slot_summary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-s2.sav");
        var summary = new SaveFileSummary(path, "S.T.A.L.K.E.R. 2", "stalker2", new string('0', 64), 0, false, []);

        Assert.Equal(summary.SlotTitle, summary.LibraryMetadataDisplay);
    }

    [Fact]
    public void Collapsed_navigation_icons_have_screen_name_tooltips_and_accessible_names()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        var root = MainWindow.BuildRoot(viewModel);
        var navigationButtons = root.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.Name?.StartsWith("nav-", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.True(navigationButtons.Length >= 14);
        foreach (var button in navigationButtons)
        {
            var tab = button.Name!["nav-".Length..];
            var icon = root.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Name == "nav-icon-" + tab);
            var label = root.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Name == "nav-label-" + tab);
            var screenName = Assert.IsType<string>(label.Text);

            Assert.Equal(screenName, ToolTip.GetTip(button)?.ToString());
            Assert.Equal(screenName, ToolTip.GetTip(icon)?.ToString());
            Assert.Equal(screenName, AutomationProperties.GetName(button));
        }
    }

    [Fact]
    public void Transitions_view_keeps_its_relocation_picker_and_action()
    {
        var view = TransitionsView.Build();

        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString() == L.T("ПЕРЕНЕСТИ"));
        Assert.Contains(view.GetVisualDescendants().OfType<ComboBox>(), picker => picker.PlaceholderText == L.T("Куда перенести"));
    }

    [Fact]
    public void Overview_binds_read_only_fields_and_shows_unknown_values_as_dashes()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [],
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        viewModel.SelectedSave = Summary(directory.Path, "unknown.sav");
        var overview = OverviewView.Build();
        overview.DataContext = viewModel;

        Assert.Equal("—", MetricValue(overview, L.T("ЗАДАНИЯ")));
        Assert.Equal("—", MetricValue(overview, L.T("УБИТО")));
        Assert.Equal("—", MetricValue(overview, L.T("ПОГОДА")));
        Assert.Equal("—", RowValue(overview, L.T("Сборка игры:")));

        var populated = Summary(directory.Path, "populated.sav",
            new XRayProgress(
                [new XRayTask("quest", "Quest", XRayTaskState.Completed, 0, 0, [])],
                [new XRayStatisticLine("stalkerkills", "bandit", 3, 0)]),
            new XRayWeather("weather", "clear", "rain"));
        viewModel.SelectedSave = populated;

        Assert.Equal(populated.TasksDisplay, MetricValue(overview, L.T("ЗАДАНИЯ")));
        Assert.Equal(populated.KillsDisplay, MetricValue(overview, L.T("УБИТО")));
        Assert.Equal(populated.WeatherDisplay, MetricValue(overview, L.T("ПОГОДА")));
        Assert.Equal("—", RowValue(overview, L.T("Сборка игры:")));
    }

    private static string? MetricValue(Control root, string label)
    {
        var cell = root.GetLogicalDescendants().OfType<StackPanel>()
            .Single(panel => panel.Children.OfType<TextBlock>().Any(block => block.Text == label));
        return Assert.IsType<TextBlock>(cell.Children[1]).Text;
    }

    private static string? RowValue(Control root, string label)
    {
        var row = root.GetLogicalDescendants().OfType<Grid>()
            .Single(grid => grid.Children.OfType<TextBlock>().Any(block => block.Text == label));
        return Assert.IsType<TextBlock>(row.Children[1]).Text;
    }

    private static SaveFileSummary Summary(
        string directory,
        string name,
        XRayProgress? progress = null,
        XRayWeather? weather = null,
        DateTime? lastModified = null) => new(
        Path.Combine(directory, name),
        "Call of Pripyat",
        "stalker-cop",
        new string('0', 64),
        0,
        canEditMoney: false,
        inventory: [],
        lastModified: lastModified)
    {
        Progress = progress,
        Weather = weather,
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stalker-library-row-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
