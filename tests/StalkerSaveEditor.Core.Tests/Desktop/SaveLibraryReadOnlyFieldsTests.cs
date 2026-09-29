using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class SaveLibraryReadOnlyFieldsTests
{
    [Fact]
    public void Missing_progress_weather_and_game_build_are_reported_as_unknown()
    {
        using var directory = new TemporaryDirectory();
        var summary = Summary(Path.Combine(directory.Path, "unknown-fields.sav"));
        var viewModel = CreateViewModel(directory.Path);
        viewModel.SelectedSave = summary;

        Assert.Equal("—", summary.TasksDisplay);
        Assert.Equal("—", summary.KillsDisplay);
        Assert.Equal("—", summary.WeatherDisplay);
        Assert.Equal("—", viewModel.GameBuildDisplay);
    }

    [Fact]
    public void Transitions_route_keeps_save_derived_relocation_destinations_available()
    {
        using var directory = new TemporaryDirectory();
        var destination = new RelocationAnchorViewModel(new XRayRelocationAnchor(
            "exit_to_garbage",
            "garbage",
            "garbage_to_agroprom",
            1,
            2,
            new XRayVector3(1, 2, 3),
            new XRayVector3(0, 0, 1)));
        var summary = Summary(Path.Combine(directory.Path, "with-transition.sav"), [destination]);
        var viewModel = CreateViewModel(directory.Path);
        viewModel.Saves.Add(summary);
        viewModel.SelectedSave = summary;
        viewModel.SelectedTab = "transitions";

        Assert.True(summary.CanRelocate);
        Assert.Single(summary.RelocationAnchors);
        Assert.True(viewModel.ShowTransitionsScreen);
    }

    private static SaveFileSummary Summary(
        string path,
        IReadOnlyList<RelocationAnchorViewModel>? relocationAnchors = null) => new(
        path,
        "Call of Pripyat",
        "stalker-cop",
        new string('0', 64),
        0,
        canEditMoney: false,
        inventory: [])
    {
        RelocationAnchors = relocationAnchors ?? [],
    };

    private static SaveLibraryViewModel CreateViewModel(string directory) => new(
        discoverLocalSaves: false,
        saveDirectoriesProvider: () => [],
        backupDirectoryProvider: () => Path.Combine(directory, "backups"),
        draftsDirectory: Path.Combine(directory, "drafts"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stalker-library-readonly-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
