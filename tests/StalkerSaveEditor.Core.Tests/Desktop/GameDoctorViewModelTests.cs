using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class GameDoctorViewModelTests
{
    [Fact]
    public async Task Analyze_command_populates_checks_for_the_selected_install()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-doctor-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        Directory.CreateDirectory(Path.Combine(root, "gamedata", "scripts"));
        File.WriteAllText(Path.Combine(root, "gamedata", "scripts", "unclassified.script"), "local = true\n");
        var viewModel = new GameDoctorViewModel();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");
        viewModel.GameDirectory = root;

        try
        {
            await viewModel.AnalyzeAsync();

            Assert.True(viewModel.HasReport);
            Assert.Contains(viewModel.Checks, check => check.Name.Length > 0);
            var file = Assert.Single(viewModel.FileAudit, row => row.RelativePath == "gamedata/scripts/unclassified.script");
            Assert.Equal("Unclassified", file.Owner);
            Assert.False(viewModel.IsAnalyzing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Discover_command_lists_candidates_and_selecting_one_sets_its_target_and_path()
    {
        var candidate = new GameDoctorInstallation(
            GameTarget.CallOfPripyat,
            Path.Combine(Path.GetTempPath(), "detected-cop"),
            GameInstallSource.Steam,
            "12345678");
        var viewModel = new GameDoctorViewModel(() => [candidate]);

        await viewModel.DiscoverInstallationsAsync();

        var selected = Assert.Single(viewModel.Installations);
        Assert.Equal(candidate.Directory, selected.Directory);
        Assert.EndsWith("1", viewModel.DiscoveryStatus, StringComparison.Ordinal);
        viewModel.SelectedInstallation = selected;
        Assert.Equal(GameTarget.CallOfPripyat, viewModel.SelectedTarget.Target);
        Assert.Equal(candidate.Directory, viewModel.GameDirectory);
    }
}
