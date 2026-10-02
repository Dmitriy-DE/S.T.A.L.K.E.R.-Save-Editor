using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class CrashSignatureCatalogTests
{
    [Fact]
    public void Wild_napr_crash_names_the_fix_and_the_quest_rule()
    {
        const string log = "! [LUA][ERROR] ERROR: wrong target for storyline quest: logic@work5,gar_smart_terrain_6_3\n";

        var result = CrashLogAnalyzer.Analyze(log, game: "cs");

        Assert.Equal("cs.wrong-target-wild-napr", result.KnownIssueId);
        Assert.Equal(CrashAdvice.RepairSave, result.KnownIssue!.Advice);
        Assert.Equal("cs.quest.dead-wild-napr", result.KnownIssue.FixId);
        Assert.Equal("cs.wild-napr-dead", result.KnownIssue.QuestRuleId);
    }

    [Theory]
    [InlineData("[error]Arguments     : LUA error: ...\\sim_combat.script:419: attempt to index field 'actor' (a nil value)", "cs", "cs.sim-combat-actor-nil")]
    [InlineData("smart_terrain.script:483: Insufficient smart_terrain jobs val_smart_terrain_9_6", "cs", "cs.insufficient-smart-jobs")]
    [InlineData("ERROR: cant find animation for slot 8", "cs", "cs.hospital-jump-down-animation")]
    [InlineData("LUA error: ... clear sky\\gamedata\\scripts\\sim_squad_generic.script:1184: attempt to index field '?' (a nil value)", "cs", "cs.squad-hint-unknown-target")]
    [InlineData("LUA error: xr_logic: pstor_load_all: not registered type N 147 encountered", "cs", "cs.pstor-unknown-type")]
    [InlineData("[error]Description   : entity not found. id_parent=1350 id_entity=1312 frame=11471", "soc", "soc.entity-not-found")]
    [InlineData("- Critical: SMapLocation binded to non-existent object id=4242", "soc", "soc.map-location-dead-object")]
    public void Quoted_messages_match_their_signature(string log, string game, string id)
    {
        Assert.Equal(id, CrashSignatureCatalog.Match(log, game)?.Id);
        Assert.Equal(id, CrashSignatureCatalog.Match(log)?.Id);
    }

    [Fact]
    public void A_signature_of_another_game_or_a_generic_error_does_not_match()
    {
        Assert.Null(CrashSignatureCatalog.Match("entity not found. id_parent=1 id_entity=2", "cop"));
        Assert.Null(CrashSignatureCatalog.Match("Insufficient smart_terrain jobs x", "soc"));
        Assert.Null(CrashLogAnalyzer.Analyze("FATAL ERROR\n[error]Expression    : 0\n").KnownIssueId);
    }

    [Fact]
    public void Every_linked_fix_and_rule_exists()
    {
        var fixes = GameFixCatalog.All.Select(fix => fix.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(CrashSignatureCatalog.All.Where(s => s.FixId is not null), s => Assert.Contains(s.FixId!, fixes));
        Assert.Equal(CrashSignatureCatalog.All.Count, CrashSignatureCatalog.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Latest_game_log_is_analyzed_from_the_game_folder()
    {
        var directory = Directory.CreateTempSubdirectory("crash-log-");
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(directory.FullName, "_appdata_", "logs"));
            File.WriteAllText(Path.Combine(logs.FullName, "xray_old.log"), "normal");
            File.SetLastWriteTimeUtc(Path.Combine(logs.FullName, "xray_old.log"), DateTime.UtcNow.AddDays(-1));
            File.WriteAllText(Path.Combine(logs.FullName, "xray_user.log"), "! [LUA][ERROR] ERROR: You are saving too much\n");

            var result = CrashLogDiscovery.AnalyzeLatestInGameDirectory(directory.FullName, "cs");

            Assert.Equal("cs.saving-too-much", result?.KnownIssueId);
            Assert.Equal(CrashLogKind.LuaError, result!.Kind);
            Assert.Equal("[LUA][ERROR] ERROR: You are saving too much", result.Summary);
            Assert.Null(CrashLogDiscovery.AnalyzeLatestInGameDirectory(Directory.CreateDirectory(Path.Combine(directory.FullName, "empty")).FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("Can't find model file 'monsters\\up_monsters\\pseudodog_noah.ogf'.", "cop", "any.missing-model")]
    [InlineData("Can't open section 'wpn_pm_actor'", "cop", "any.missing-section")]
    [InlineData("Can't find variable night_vision in [device_torch]", "soc", "any.missing-config-value")]
    [InlineData("[error]Arguments : string table xml file not found string_table_includes.xml", "soc", "any.missing-string-table")]
    [InlineData("[error]Expression    : hFile>0\n[error]Function      : FileDownload", "cs", "any.config-not-opened")]
    public void A_damaged_installation_is_recognised_in_every_game(string log, string game, string id)
    {
        var match = CrashSignatureCatalog.Match(log, game);
        Assert.Equal(id, match?.Id);
        Assert.Equal(CrashAdvice.RepairInstallation, match!.Advice);
    }

    [Fact]
    public void A_crash_a_fix_explains_is_named_before_the_general_missing_file() =>
        Assert.Equal("cs.missing-backpack-model", CrashSignatureCatalog.Match(@"Can't find model file 'dynamics\equipments\item_rukzak.ogf'", "cs")?.Id);
}
