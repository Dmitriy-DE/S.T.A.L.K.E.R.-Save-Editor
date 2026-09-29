using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using StalkerSaveEditor.Desktop;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

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

    private static SaveFileSummary Summary(string directory, string name) => new(
        Path.Combine(directory, name),
        "Call of Pripyat",
        "stalker-cop",
        new string('0', 64),
        0,
        canEditMoney: false,
        inventory: []);

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
