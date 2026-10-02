using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class GameFixCatalogTests
{
    [Fact]
    public void Lookup_presets_and_counts_cover_exactly_what_a_game_lists_including_enhanced_editions()
    {
        foreach (var game in Enum.GetValues<GameTarget>())
        {
            var listed = GameFixCatalog.ForGame(game);
            foreach (var fix in listed)
            {
                Assert.True(GameFixCatalog.TryGet(fix.Id, out var found), fix.Id);
                Assert.Equal(game, found!.Game);
            }
            Assert.Equal(listed.Count, GameFixCatalog.CategoryCounts(game).Values.Sum());
            var recommended = GameFixCatalog.ForPreset(game, GameFixPreset.Recommended);
            Assert.All(recommended, fix => Assert.Contains(fix, listed));
            Assert.Equal(
                listed.Where(fix => fix.Maturity == GameFixMaturity.Validated && fix.Category is GameFixCategory.Essential or GameFixCategory.Recommended).Select(fix => fix.Id).Order(),
                recommended.Select(fix => fix.Id).Order());
        }
        Assert.NotEmpty(GameFixCatalog.ForPreset(GameTarget.ClearSkyEnhancedEdition, GameFixPreset.Recommended));
    }

    [Fact]
    public void All_spawn_fix_is_structured_and_pinned_to_the_retail_file()
    {
        var fix = GameFixCatalog.All.Single(candidate => candidate.Id == "cs.crash.all-spawn-errors");

        Assert.Equal(GameFixImplementationType.Structured, fix.Implementation);
        Assert.Empty(fix.TextPatches);
        Assert.Equal(43, fix.SpawnEdits.Count);
        Assert.Single(fix.SpawnEdits.Select(edit => edit.ExpectedFileSha256).Distinct());
        Assert.All(fix.SpawnEdits, edit =>
        {
            Assert.Equal("gamedata/spawns/all.spawn", edit.RelativePath);
            Assert.Matches("^[0-9a-f]{64}$", edit.ExpectedFileSha256);
            Assert.False(string.IsNullOrEmpty(edit.Expected));
        });
        var cordon = Assert.Single(fix.SpawnEdits, edit => edit.Target == "esc_smart_terrain_3_7_walker_1_walk");
        Assert.Equal(135001u, cordon.LevelVertexId);
        Assert.Contains(fix.SpawnEdits, edit => edit.Target == "mil_smart_terrain_2_1" && edit.Replacement!.Contains("squad_capacity = 1", StringComparison.Ordinal));
        Assert.DoesNotContain(GameFixCatalog.All, candidate => candidate.Id == "cs.crash.all-spawn-errors.ee");
    }

    [Fact]
    public void Shipped_catalogue_contains_archive_verified_clear_sky_fixes_and_populates_safe_presets()
    {
        var fixes = GameFixCatalog.ForGame(GameTarget.ClearSky);
        Assert.Equal(63, fixes.Count);
        var fix = Assert.Single(fixes, candidate => candidate.Id == "cs.quest.dead-wild-napr");
        Assert.Equal("cs.quest.dead-wild-napr", fix.Id);
        Assert.Equal(GameFixCategory.Essential, fix.Category);
        Assert.Equal(GameFixMaturity.Validated, fix.Maturity);
        Assert.Equal(GameFixVerificationState.RetailFilesVerified, fix.VerificationState);
        Assert.Equal(["11450472"], fix.SupportedSteamBuildIds);
        Assert.Equal(GameFixImplementationType.ExactTextReplacement, fix.Implementation);
        Assert.Single(fix.TextPatches);
        Assert.Matches("^[0-9a-f]{64}$", fix.TextPatches[0].ExpectedFileSha256);

        Assert.All(fixes, candidate =>
        {
            Assert.Equal(GameFixVerificationState.RetailFilesVerified, candidate.VerificationState);
            Assert.Equal(GameFixMaturity.Validated, candidate.Maturity);
            Assert.Equal(["11450472"], candidate.SupportedSteamBuildIds);
            Assert.All(candidate.TextPatches, operation => Assert.Matches("^[0-9a-f]{64}$", operation.ExpectedFileSha256));
        });
        var pathOwners = fixes
            .SelectMany(candidate => candidate.TextPatches.Select(operation => (candidate.Id, operation.RelativePath)))
            .GroupBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.All(pathOwners, group => Assert.Single(group.Select(entry => entry.Id).Distinct(StringComparer.Ordinal)));
        Assert.NotEmpty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.EssentialOnly));
        Assert.NotEmpty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended));
        Assert.Equal(61, GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended).Count);
        Assert.Contains(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended), candidate => candidate.Id == fix.Id);
        Assert.DoesNotContain(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended), candidate => candidate.Category == GameFixCategory.Community);

        Assert.Empty(GameFixCatalog.ForGame(GameTarget.Stalker2));
        Assert.NotEmpty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.AllSafeFixes));
        Assert.Empty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Custom));
    }

    [Fact]
    public void Community_dialogue_and_sniper_fixes_target_their_exact_terminal_sections()
    {
        var dialog2 = Assert.Single(GameFixCatalog.All.Single(fix => fix.Id == "cs.dialog.escape-2-level-changers").TextPatches,
            patch => patch.RelativePath.EndsWith("esc_pda_dialog_2.ltx", StringComparison.Ordinal) && patch.ExpectedText.StartsWith("[sr_idle", StringComparison.Ordinal));
        Assert.Equal("[sr_idle@7]\r\n", dialog2.ExpectedText);
        Assert.Equal("[sr_idle@7]\r\non_signal = sound_end | nil %=enable_level_changer(443) =enable_level_changer(445)%\r\n", dialog2.ReplacementText);

        var dialog4 = Assert.Single(GameFixCatalog.All.Single(fix => fix.Id == "cs.dialog.escape-4-level-changer").TextPatches,
            patch => patch.RelativePath.EndsWith("esc_pda_dialog_4.ltx", StringComparison.Ordinal) && patch.ExpectedText.StartsWith("[sr_idle", StringComparison.Ordinal));
        Assert.Equal("[sr_idle@14]\r\n", dialog4.ExpectedText);
        Assert.Equal("[sr_idle@14]\r\non_signal = sound_end | nil %=enable_level_changer(444)%\r\n", dialog4.ReplacementText);

        var sniperPatches = GameFixCatalog.All.Single(fix => fix.Id == "cs.quest.verified-hospital-sniper-danger-keys").TextPatches;
        Assert.Equal(2, sniperPatches.Count);
        Assert.Contains(sniperPatches, patch => patch.ExpectedText.StartsWith("[danger_condition]\r\n", StringComparison.Ordinal));
        Assert.Contains(sniperPatches, patch => patch.ExpectedText.StartsWith("[danger_condition@2]\r\n", StringComparison.Ordinal));
    }

    [Fact]
    public void No_game_file_is_claimed_by_two_fixes()
    {
        // The engine installs one fix per file; two entries on one file could never both be installed.
        var owners = GameFixCatalog.All
            .SelectMany(fix => GameFixEngine.ManagedPaths(fix).Select(path => (fix.Game, Path: path.ToLowerInvariant(), fix.Id)))
            .GroupBy(entry => (entry.Game, entry.Path))
            .Where(group => group.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => $"{group.Key.Game} {group.Key.Path}: {string.Join(", ", group.Select(entry => entry.Id).Distinct())}")
            .ToArray();

        Assert.Empty(owners);
    }

    [Fact]
    public void A_retail_only_patch_is_left_out_of_the_enhanced_edition_variant()
    {
        var retail = GameFixCatalog.All.Single(fix => fix.Id == "cs.crash.treasure-given-twice");
        var enhanced = GameFixCatalog.All.Single(fix => fix.Id == "cs.crash.treasure-given-twice.ee");

        Assert.Contains(retail.TextPatches, patch => patch.RetailOnly);
        Assert.DoesNotContain(enhanced.TextPatches, patch => patch.RetailOnly);
        Assert.Equal(retail.TextPatches.Count(patch => !patch.RetailOnly), enhanced.TextPatches.Count);
    }

    [Fact]
    public void Shipped_soc_catalogue_contains_only_retail_verified_zrp_bug_fixes()
    {
        var fixes = GameFixCatalog.ForGame(GameTarget.ShadowOfChernobyl);

        Assert.True(fixes.Count >= 15);
        Assert.All(fixes, fix =>
        {
            Assert.Equal(["11567845"], fix.SupportedSteamBuildIds);
            Assert.Equal(GameFixMaturity.Validated, fix.Maturity);
            Assert.Equal(GameFixVerificationState.RetailFilesVerified, fix.VerificationState);
            Assert.True(
                fix.Source.Contains("ZRP", StringComparison.OrdinalIgnoreCase) ||
                fix.Source.Contains("static check", StringComparison.Ordinal));
            Assert.Contains(fix.References, reference =>
                reference.Contains("metacognix.com/stlkrsoc", StringComparison.Ordinal) ||
                reference.Contains("tools/lua_globals.py", StringComparison.Ordinal));
            Assert.All(fix.TextPatches, patch => Assert.Matches("^[0-9a-f]{64}$", patch.ExpectedFileSha256));
        });

        Assert.True(GameFixCatalog.ForPreset(GameTarget.ShadowOfChernobyl, GameFixPreset.Recommended).Count >= 15);
        var pathOwners = fixes
            .SelectMany(fix => fix.TextPatches.Select(patch => (fix.Id, patch.RelativePath)))
            .GroupBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.All(pathOwners, group => Assert.Single(group.Select(entry => entry.Id).Distinct(StringComparer.Ordinal)));
        Assert.DoesNotContain(GameFixCatalog.ForGame(GameTarget.ClearSky), fix => fix.SupportedSteamBuildIds.Contains("11567845", StringComparer.Ordinal));
    }

    [Fact]
    public void Shipped_cop_catalogue_contains_retail_verified_fixes_and_keeps_community_edits_out_of_presets()
    {
        var fixes = GameFixCatalog.ForGame(GameTarget.CallOfPripyat);

        Assert.True(fixes.Count >= 15);
        Assert.All(fixes, fix =>
        {
            Assert.Equal(["11450453"], fix.SupportedSteamBuildIds);
            Assert.Equal(GameFixMaturity.Validated, fix.Maturity);
            Assert.Equal(GameFixVerificationState.RetailFilesVerified, fix.VerificationState);
            Assert.True(
                fix.Source.Contains("stalker-cop-patch", StringComparison.OrdinalIgnoreCase) ||
                fix.Source.Contains("Pripyat Reclamation Patch", StringComparison.OrdinalIgnoreCase) ||
                fix.Source.Contains("UCoPEEP", StringComparison.Ordinal) ||
                fix.Source.Contains("Enhanced Edition", StringComparison.Ordinal));
            Assert.Contains(fix.References, reference =>
                reference.Contains("github.com/victor-homyakov/stalker-cop-patch", StringComparison.Ordinal) ||
                reference.Contains("moddb.com/mods/pripyat-reclamation-patch", StringComparison.Ordinal) ||
                reference.Contains("steamcommunity.com/sharedfiles/filedetails/?id=3487808500", StringComparison.Ordinal) ||
                reference.Contains("Enhanced Edition, gamedata/scripts", StringComparison.Ordinal) ||
                reference.Contains("tools/check_condlists.py", StringComparison.Ordinal));
            Assert.All(fix.TextPatches, patch => Assert.Matches("^[0-9a-f]{64}$", patch.ExpectedFileSha256));
        });

        var recommended = GameFixCatalog.ForPreset(GameTarget.CallOfPripyat, GameFixPreset.Recommended);
        Assert.Equal(21, recommended.Count);
        Assert.Contains(recommended, fix => fix.Id == "cop.weapon.spas12-sight-alignment");
        Assert.Contains(recommended, fix => fix.Id == "cop.weapon.val-sight-alignment");
        Assert.Contains(recommended, fix => fix.Id == "cop.dialog.correct-anomaly-name");
        Assert.Contains(recommended, fix => fix.Id == "cop.dialog.gonta-after-soroka-recovered");
        Assert.Contains(recommended, fix => fix.Id == "cop.quest.memory-module-unlock-attribution");
        Assert.Contains(recommended, fix => fix.Id == "cop.prp.crow-counter-guard");
        Assert.Contains(recommended, fix => fix.Id == "cop.prp.x8-burer-health-guard");
        Assert.Contains(recommended, fix => fix.Id == "cop.prp.jupiter-scanner-task-guard");
        Assert.Contains(recommended, fix => fix.Id == "cop.prp.altered-insulator-door-gate");
        Assert.Contains(recommended, fix => fix.Id == "cop.prp.sky-stretching-fix");
        Assert.DoesNotContain(recommended, fix => fix.Id == "cop.prp.knife-hit-reach");
        Assert.DoesNotContain(GameFixCatalog.ForPreset(GameTarget.CallOfPripyat, GameFixPreset.AllSafeFixes), fix => fix.Category == GameFixCategory.Community);
        Assert.Equal(11, GameFixCatalog.PreviousPresetCount(GameTarget.CallOfPripyat, GameFixPreset.Recommended));

        var russianText = fixes.SelectMany(fix => fix.TextPatches).Where(patch => patch.RelativePath.Contains("/text/rus/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(russianText);
        Assert.All(russianText, patch => Assert.Equal(1251, patch.CodePage));

        var pathOwners = fixes
            .SelectMany(fix => fix.TextPatches.Select(patch => (fix.Id, patch.RelativePath)))
            .GroupBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.All(pathOwners, group => Assert.Single(group.Select(entry => entry.Id).Distinct(StringComparer.Ordinal)));
        Assert.DoesNotContain(GameFixCatalog.ForGame(GameTarget.ClearSky), fix => fix.SupportedSteamBuildIds.Contains("11450453", StringComparer.Ordinal));
    }

    [Fact]
    public void Prp_preset_payloads_are_exact_and_do_not_claim_the_same_retail_file()
    {
        var fixes = GameFixCatalog.ForGame(GameTarget.CallOfPripyat)
            .Where(fix => fix.Id.StartsWith("cop.prp.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(6, fixes.Length);
        Assert.All(fixes, fix =>
        {
            Assert.Equal(GameFixVerificationState.RetailFilesVerified, fix.VerificationState);
            Assert.Contains("PRP v1.2", fix.Source, StringComparison.Ordinal);
            Assert.Contains(fix.References, reference => reference.Contains("mediafire.com/folder/", StringComparison.Ordinal));
        });
        Assert.Equal(5, fixes.Count(fix => fix.Category == GameFixCategory.Recommended));
        Assert.Equal(GameFixCategory.Community, Assert.Single(fixes, fix => fix.Id == "cop.prp.knife-hit-reach").Category);

        var pathOwners = fixes
            .SelectMany(fix => fix.TextPatches.Select(patch => (fix.Id, patch.RelativePath)))
            .GroupBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.All(pathOwners, group => Assert.Single(group.Select(entry => entry.Id).Distinct(StringComparer.Ordinal)));
    }

    [Fact]
    public void Game_fixes_screen_shows_actual_researched_category_counts()
    {
        var viewModel = new GameFixesViewModel();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");

        Assert.Equal(5, viewModel.Categories.Count);
        Assert.Equal(27, Assert.Single(viewModel.Categories, category => category.Category == GameFixCategory.Essential).Count);
        Assert.Equal(0, Assert.Single(viewModel.Categories, category => category.Category == GameFixCategory.Experimental).Count);
        Assert.Contains("63", viewModel.CatalogueStatus, StringComparison.Ordinal);
    }
}
