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
        var viewModel = new GameDoctorViewModel();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");
        viewModel.GameDirectory = root;

        try
        {
            await viewModel.AnalyzeAsync();

            Assert.True(viewModel.HasReport);
            Assert.Contains(viewModel.Checks, check => check.Name.Length > 0);
            Assert.False(viewModel.IsAnalyzing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
