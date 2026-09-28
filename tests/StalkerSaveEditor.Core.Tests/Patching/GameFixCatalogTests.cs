using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class GameFixCatalogTests
{
    [Fact]
    public void Shipped_catalogue_contains_the_retail_verified_clear_sky_candidate_without_auto_selecting_it()
    {
        var fix = Assert.Single(GameFixCatalog.ForGame(GameTarget.ClearSky));
        Assert.Equal("cs.quest.dead-wild-napr", fix.Id);
        Assert.Equal(GameFixCategory.Essential, fix.Category);
        Assert.Equal(GameFixMaturity.Experimental, fix.Maturity);
        Assert.Equal(GameFixVerificationState.RetailFilesVerified, fix.VerificationState);
        Assert.Equal(["11450472"], fix.SupportedSteamBuildIds);
        Assert.Equal(GameFixImplementationType.ExactTextReplacement, fix.Implementation);
        Assert.Single(fix.TextPatches);
        Assert.Matches("^[0-9a-f]{64}$", fix.TextPatches[0].ExpectedFileSha256);

        foreach (var game in Enum.GetValues<GameTarget>().Where(game => game != GameTarget.ClearSky))
            Assert.Empty(GameFixCatalog.ForGame(game));
        Assert.Empty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.EssentialOnly));
        Assert.Empty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended));
        Assert.Empty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.AllSafeFixes));
        Assert.Empty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Custom));
    }

    [Fact]
    public void Game_fixes_screen_shows_actual_researched_category_counts()
    {
        var viewModel = new GameFixesViewModel();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");

        Assert.Equal(5, viewModel.Categories.Count);
        Assert.Equal(1, Assert.Single(viewModel.Categories, category => category.Category == GameFixCategory.Essential).Count);
        Assert.Equal(0, Assert.Single(viewModel.Categories, category => category.Category == GameFixCategory.Experimental).Count);
        Assert.Contains("1", viewModel.CatalogueStatus, StringComparison.Ordinal);
    }
}
