using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class SaveDoctorViewModelTests
{
    [Fact]
    public async Task Analyzes_a_supported_save_read_only_and_shows_unknown_semantic_checks()
    {
        var viewModel = new SaveDoctorViewModel
        {
            SavePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-s2-money", "s2-money-source.sav"),
        };

        await viewModel.AnalyzeAsync();

        Assert.True(viewModel.HasReport);
        Assert.Equal(4, viewModel.Checks.Count);
        Assert.Contains(viewModel.Checks, check => check.Status.ToString() == "Ok");
        Assert.Contains(viewModel.Checks, check => check.Status.ToString() == "Unknown");
        Assert.Contains(viewModel.Checks, check => check.Status.ToString() == "Unknown" && check.Name.Contains("КВЕСТОВ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("stalker2", viewModel.Checks[0].Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_sky_save_lists_quest_rules_and_offers_no_repair_without_proof()
    {
        var viewModel = new SaveDoctorViewModel
        {
            SavePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-factions", "cs-source.sav"),
        };

        await viewModel.AnalyzeAsync();

        Assert.Contains(viewModel.Checks, check => check.Name.Contains("ВОЛКА", StringComparison.Ordinal) && check.Mark == "?");
        Assert.Contains(viewModel.Checks, check => check.Name.Contains("НАПРА", StringComparison.Ordinal) && check.Mark == "?");
        Assert.DoesNotContain(viewModel.Checks, check => check.Name == "РЕМОНТ СОХРАНЕНИЯ");
        Assert.False(viewModel.CanRepairQuests);
        Assert.False(viewModel.RepairQuestsCommand.CanExecute(null));
    }
}
