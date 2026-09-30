using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
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
        var second = Summary(directory.Path, "selected.sav");
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
        XRayWeather? weather = null) => new(
        Path.Combine(directory, name),
        "Call of Pripyat",
        "stalker-cop",
        new string('0', 64),
        0,
        canEditMoney: false,
        inventory: [])
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
