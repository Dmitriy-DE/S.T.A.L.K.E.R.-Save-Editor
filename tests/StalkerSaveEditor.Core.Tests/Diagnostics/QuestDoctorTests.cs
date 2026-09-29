using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class QuestDoctorTests
{
    // First 200 bytes of the STATE window of the Cordon stalker "esc_wolf" (Clear Sky, object version 124),
    // copied from a real save. It holds NPC data only.
    private const string WolfStateHex =
        "92450F006573635F776F6C6600010000006573635F776F6C66000F0000006B03000000000000C2EEEBEA0039020000000001000000A9CF0000BFFFFFFF" +
        "3B20737461" + "6C6B65725F637573746F6D5F646174612E6C74780D0A5B67616D655F696E666F5D0D0A00FFFFFFFFFFFFFFFF" +
        "6163746F72735C7374616C6B65725F6E65757472616C5C7374616C6B65725F6E65757472616C5F776F6C6600000F03000000803F0000000000000000FFFF" +
        "000000000000000000007F1300050000000200000200040000000201";

    private static byte[] Fixture(string relative) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static XRayCreatureVitals Vitals(string section, float health) => new(1, section, health, null, null);

    private static Func<string, IReadOnlyList<XRayCreatureVitals>> Npcs(params XRayCreatureVitals[] all) =>
        section => all.Where(v => v.Section == section).ToArray();

    private static QuestTaskState State(IEnumerable<QuestTaskState> states, string id) => Assert.Single(states, s => s.TaskId == id);

    private static HashSet<string> Known(params string[] flags) => new(flags, StringComparer.Ordinal);

    [Fact]
    public void Reads_health_killer_and_death_time_of_a_real_stalker_record()
    {
        var state = Convert.FromHexString(WolfStateHex);
        var item = new XRayRegistryObject("esc_wolf", "esc_wolf", 7, 0xFFFF, 124, 0, state.Length, 0, state.Length, 0, 0, null, 0);

        var alive = XRayTrilogyReader.ReadCreatureVitals(state, item);

        Assert.NotNull(alive);
        Assert.Equal(1f, alive.Health);
        Assert.False(alive.IsDead);
        Assert.Equal((ushort)0xFFFF, alive.KillerId);
        Assert.Equal(0UL, alive.DeathTime);

        BitConverter.TryWriteBytes(state.AsSpan(alive.HealthOffset, sizeof(float)), 0f);
        Assert.True(XRayTrilogyReader.ReadCreatureVitals(state, item)!.IsDead);
    }

    [Fact]
    public void Refuses_a_state_that_does_not_parse_as_a_stalker()
    {
        var state = Convert.FromHexString(WolfStateHex)[..40];
        var item = new XRayRegistryObject("esc_wolf", "esc_wolf", 7, 0xFFFF, 124, 0, state.Length, 0, state.Length, 0, 0, null, 0);

        Assert.Null(XRayTrilogyReader.ReadCreatureVitals(state, item));
    }

    [Fact]
    public void Dead_npc_without_the_cancellation_flag_is_broken_and_links_the_preventing_fix()
    {
        var states = QuestDoctor.Evaluate(
            "stalker-cs",
            Known("unrelated"),
            Npcs(Vitals("esc_wolf", 0f), Vitals("gar_digger_quester", -0.1f)));

        var wolf = State(states, "cs.wolf-dead");
        Assert.Equal(QuestTaskStatus.Broken, wolf.State);
        Assert.Equal("esc_wolf_dead", wolf.MissingInfoPortion);
        Assert.Equal("cs.quest.wolf-offline-cancellation", wolf.PreventingFixId);
        var napr = State(states, "cs.wild-napr-dead");
        Assert.Equal(QuestTaskStatus.Broken, napr.State);
        Assert.Equal("gar_flea_market_stop_quest_line", napr.MissingInfoPortion);
        Assert.Equal("cs.quest.dead-wild-napr", napr.PreventingFixId);
        Assert.All(states, s => Assert.NotEmpty(s.References));
    }

    [Fact]
    public void Living_dead_with_flag_missing_or_unreadable_npcs_are_not_broken()
    {
        var alive = QuestDoctor.Evaluate("stalker-cs", Known(), Npcs(Vitals("esc_wolf", 1f), Vitals("gar_digger_quester", 0.4f), Vitals("mil_hog", 0.7f)));
        Assert.All(alive, s => Assert.Equal(QuestTaskStatus.Ok, s.State));

        var flagged = QuestDoctor.Evaluate("stalker-cs", Known("esc_wolf_dead"), Npcs(Vitals("esc_wolf", 0f)));
        Assert.Equal(QuestTaskStatus.Ok, State(flagged, "cs.wolf-dead").State);

        var absent = QuestDoctor.Evaluate("stalker-cs", Known(), Npcs());
        Assert.All(absent, s => Assert.Equal(QuestTaskStatus.Unknown, s.State));

        var noActorInfo = QuestDoctor.Evaluate("stalker-cs", null, Npcs(Vitals("esc_wolf", 0f)));
        Assert.Equal(QuestTaskStatus.Unknown, State(noActorInfo, "cs.wolf-dead").State);
        Assert.All(absent.Concat(noActorInfo), s => Assert.Null(s.MissingInfoPortion));
    }

    [Fact]
    public void Dead_hog_is_repairable_only_before_the_forester_talk()
    {
        var early = State(QuestDoctor.Evaluate("stalker-cs", Known(), Npcs(Vitals("mil_hog", 0f))), "cs.hog-dead");
        Assert.Equal(QuestTaskStatus.Broken, early.State);
        Assert.Equal("mil_hog_death", early.MissingInfoPortion);
        Assert.False(early.NeedsPreventingFix);

        var late = State(QuestDoctor.Evaluate("stalker-cs", Known("forester_talked_2"), Npcs(Vitals("mil_hog", 0f))), "cs.hog-dead");
        Assert.Equal(QuestTaskStatus.Unknown, late.State);
        Assert.Equal("too-late", late.Reason);
        Assert.Null(late.MissingInfoPortion);
    }

    [Fact]
    public void Wolf_repair_says_it_needs_the_game_fix()
    {
        var wolf = State(QuestDoctor.Evaluate("stalker-cs", Known(), Npcs(Vitals("esc_wolf", 0f))), "cs.wolf-dead");
        Assert.True(wolf.NeedsPreventingFix);
    }

    [Theory]
    [InlineData("soc.mole-dead", "agr_krot", "agr_krot_dead")]
    [InlineData("soc.prisoner-dead", "val_prisoner_captive", "val_prisoner_dead")]
    [InlineData("soc.courier-dead", "mil_freedom_member0001", "mil_courier_dead")]
    [InlineData("soc.informer-dead", "mil_ara", "mil_ara_dead")]
    public void Shadow_of_chernobyl_dead_npc_without_his_death_flag_is_broken(string rule, string npc, string flag)
    {
        var broken = State(QuestDoctor.Evaluate("stalker-soc", Known(), Npcs(Vitals(npc, 0f))), rule);
        Assert.Equal(QuestTaskStatus.Broken, broken.State);
        Assert.Equal(flag, broken.MissingInfoPortion);

        Assert.Equal(QuestTaskStatus.Ok, State(QuestDoctor.Evaluate("stalker-soc", Known(), Npcs(Vitals(npc, 1f))), rule).State);
        Assert.Equal(QuestTaskStatus.Ok, State(QuestDoctor.Evaluate("stalker-soc", Known(flag), Npcs(Vitals(npc, 0f))), rule).State);
    }

    [Fact]
    public void Other_formats_have_no_rules()
    {
        Assert.Empty(QuestDoctor.Evaluate("stalker-cop", Known(), Npcs(Vitals("esc_wolf", 0f))));
        Assert.All(QuestDoctor.Evaluate("stalker-soc", Known(), Npcs(Vitals("esc_wolf", 0f))), s => Assert.Equal(QuestTaskStatus.Unknown, s.State));
    }

    [Fact]
    public void A_save_without_the_npcs_reports_unknown_and_prepares_no_repair()
    {
        var source = Fixture("writer-factions/cs-source.sav");

        var report = QuestDoctor.Analyze(source);

        Assert.True(report.QuestStatesAvailable);
        Assert.Equal(SaveDoctorStatus.Ok, report.Status);
        Assert.All(report.States, s => Assert.NotEqual(QuestTaskStatus.Broken, s.State));
        Assert.Null(QuestDoctor.PrepareRepair(source));
    }

    [Theory]
    [InlineData("writer-factions/cop-source.sav", "stalker-cop")]
    [InlineData("writer-s2-money/s2-money-source.sav", "stalker2")]
    public void Formats_without_rules_report_no_states(string fixture, string format)
    {
        var report = QuestDoctor.Analyze(Fixture(fixture));

        Assert.Equal(SaveDoctorStatus.Unknown, report.Status);
        Assert.Equal(format, report.FormatId);
        Assert.False(report.QuestStatesAvailable);
        Assert.Empty(report.States);
        Assert.Null(QuestDoctor.PrepareRepair(Fixture(fixture)));
    }

    [Fact]
    public void Repair_write_keeps_a_backup_and_the_flag_reads_back()
    {
        var directory = Directory.CreateTempSubdirectory("quest-repair-");
        try
        {
            var savePath = Path.Combine(directory.FullName, "saves", "quest.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            var source = Fixture("writer-factions/cs-source.sav");
            File.WriteAllBytes(savePath, source);
            var prepared = XRayInfoPortionWriter.AddActorInfo(source, ["esc_wolf_dead"]);

            var receipt = LocalSaveReplacement.ReplaceLocal(savePath, prepared, Path.Combine(directory.FullName, "backups"),
                readBack => QuestDoctor.VerifyRepair(readBack.Span));

            Assert.Equal(source, File.ReadAllBytes(receipt.BackupPath));
            var written = XRayTrilogyReader.FromBytes(File.ReadAllBytes(savePath));
            Assert.Contains("esc_wolf_dead", written.ActorKnownInfo);
            Assert.Equal(QuestTaskStatus.Ok, State(QuestDoctor.Analyze(File.ReadAllBytes(savePath)).States, "cs.wolf-dead").State);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Verify_repair_rejects_bytes_that_do_not_parse()
    {
        Assert.Throws<InvalidDataException>(() => QuestDoctor.VerifyRepair(new byte[64]));
    }
}
