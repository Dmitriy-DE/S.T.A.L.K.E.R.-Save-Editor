using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class GameFixEnhancedEditionTests
{
    [Theory]
    [InlineData(GameTarget.ShadowOfChernobylEnhancedEdition, "24067120", 14)]
    [InlineData(GameTarget.ClearSkyEnhancedEdition, "24067129", 38)]
    [InlineData(GameTarget.CallOfPripyatEnhancedEdition, "24067133", 20)]
    public void Each_edition_gets_the_variants_verified_against_its_files(GameTarget game, string build, int count)
    {
        var fixes = GameFixCatalog.ForGame(game);

        Assert.Equal(count, fixes.Count);
        Assert.All(fixes, fix =>
        {
            Assert.EndsWith(".ee", fix.Id, StringComparison.Ordinal);
            Assert.Equal([build], fix.SupportedSteamBuildIds);
            Assert.All(fix.TextPatches, patch => Assert.Matches("^[0-9a-f]{64}$", patch.ExpectedFileSha256!));
        });
    }

    [Fact]
    public void A_variant_keeps_the_retail_anchors_and_changes_only_the_hashes()
    {
        var retail = Assert.Single(GameFixCatalog.All, fix => fix.Id == "soc.quest.petruha-report-once");
        var ee = Assert.Single(GameFixCatalog.All, fix => fix.Id == "soc.quest.petruha-report-once.ee");

        Assert.Equal(GameTarget.ShadowOfChernobylEnhancedEdition, ee.Game);
        Assert.Equal(retail.TextPatches.Select(p => (p.RelativePath, p.ExpectedText, p.ReplacementText)),
            ee.TextPatches.Select(p => (p.RelativePath, p.ExpectedText, p.ReplacementText)));
        Assert.NotEqual(retail.TextPatches[0].ExpectedFileSha256, ee.TextPatches[0].ExpectedFileSha256);
    }

    [Fact]
    public void Fixes_already_fixed_in_enhanced_edition_get_no_variant()
    {
        Assert.DoesNotContain(GameFixCatalog.All, fix => fix.Id == "cs.ai.snork-aggression-key.ee");
        Assert.DoesNotContain(GameFixCatalog.All, fix => fix.Id == "cop.prp.knife-hit-reach.ee");
        Assert.Equal(GameFixCatalog.All.Count, GameFixCatalog.All.Select(fix => fix.Id).Distinct().Count());
    }
}
