using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Core.Diagnostics;

public static class QuestTaskStatus
{
    public const string Ok = "ok";
    public const string Broken = "broken";
    public const string Unknown = "unknown";
}

public sealed record QuestTaskState(string TaskId, string State, string? PreventingFixId = null)
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>Why the state was chosen: no-info-list, flag-set, npc-missing, alive, too-late or dead-without-flag.</summary>
    public string? Reason { get; init; }

    public bool NeedsPreventingFix { get; init; }

    public string? NpcSection { get; init; }

    public string? MissingInfoPortion { get; init; }

    public IReadOnlyList<string> References { get; init; } = [];
}

public sealed record QuestDoctorReport(
    SaveDoctorStatus Status,
    string? FormatId,
    bool QuestStatesAvailable,
    string Summary,
    IReadOnlyList<QuestTaskState> States);

/// <summary>
/// A known quest break: the NPC is dead in the save, but the info portion that the game's own task and dialog
/// configs use to cancel his tasks was never set (the NPC died offline before the fix existed).
/// </summary>
internal sealed record QuestRule(
    string Id,
    string FormatId,
    string Title,
    string NpcSection,
    string InfoPortion,
    string? PreventingFixId,
    string Explanation,
    IReadOnlyList<string> References)
{
    /// <summary>An info portion after which the quest line has already branched; the flag no longer helps.</summary>
    public string? TooLateInfo { get; init; }

    /// <summary>Only the preventing Game Fix makes the game react to the flag; unpatched Clear Sky never reads it.</summary>
    public bool NeedsPreventingFix { get; init; }
}

/// <summary>Reports quest breaks that are proven by the save's own data and prepares the one-flag repair.</summary>
public static class QuestDoctor
{
    private const string SocAllSpawn = "retail Shadow of Chernobyl gamedata: spawns/all.spawn ([death] on_info of the NPC)";
    private const string SrpHistory = "https://github.com/Decane/SRP/blob/master/SRP%20v1.1.5%20-%20Version%20History.txt";

    // Every rule needs: the NPC section as the game spawns it, the info portion that game configs (tm_*.ltx
    // "{+flag} reversed/fail", dialogs) react to, and a source that documents the break.
    private static readonly IReadOnlyList<QuestRule> Rules =
    [
        new QuestRule(
            "cs.wild-napr-dead",
            "stalker-cs",
            "Wild Napr's tasks after his death",
            "gar_digger_quester",
            "gar_flea_market_stop_quest_line",
            "cs.quest.dead-wild-napr",
            "Wild Napr is dead, but gar_flea_market_stop_quest_line is missing. The Flea Market task list (tm_garbadge.ltx) cancels his tasks on that flag; without it a later task can point at his removed logic and crash with \"wrong target for storyline quest: logic@work5,gar_smart_terrain_6_3\".",
            [SrpHistory]),
        new QuestRule(
            "cs.wolf-dead",
            "stalker-cs",
            "Wolf's tasks after his death",
            "esc_wolf",
            "esc_wolf_dead",
            "cs.quest.wolf-offline-cancellation",
            "Wolf is dead, but esc_wolf_dead is missing. With the Wolf Game Fix installed, his rescue quest lines (esc_quest_additional_line*.ltx) close on that flag; without it the reward task stays active forever.",
            [SrpHistory])
        {
            NeedsPreventingFix = true,
        },
        new QuestRule(
            "cs.hog-dead",
            "stalker-cs",
            "Hog's storyline task after his death",
            "mil_hog",
            "mil_hog_death",
            null,
            "Hog is dead, but mil_hog_death is missing. After the talk with Forester the Army Warehouses quest line (mil_quest_line.ltx) checks that flag: without it the story sends you to talk to the dead Hog and stops.",
            [SrpHistory])
        {
            TooLateInfo = "forester_talked_2",
        },

        // Shadow of Chernobyl: the NPC's own [death] logic in the retail all.spawn gives the flag, and the
        // retail task XML fails or completes the task on it. A death the game processed offline never runs that logic.
        new QuestRule(
            "soc.mole-dead",
            "stalker-soc",
            "Mole's Agroprom task after his death",
            "agr_krot",
            "agr_krot_dead",
            null,
            "Mole is dead, but agr_krot_dead is missing. tasks_agroprom.xml fails the \"meet Mole's group\" objective on that flag; without it the objective points at a dead NPC forever.",
            [SocAllSpawn, "config/gameplay/tasks_agroprom.xml"]),
        new QuestRule(
            "soc.prisoner-dead",
            "stalker-soc",
            "Dark Valley prisoner task after his death",
            "val_prisoner_captive",
            "val_prisoner_dead",
            null,
            "The captive Duty soldier is dead, but val_prisoner_dead is missing. tasks_darkvalley.xml fails \"help the prisoner\" on that flag; without it the task stays open.",
            [SocAllSpawn, "config/gameplay/tasks_darkvalley.xml"]),
        new QuestRule(
            "soc.courier-dead",
            "stalker-soc",
            "Freedom courier task after his death",
            "mil_freedom_member0001",
            "mil_courier_dead",
            null,
            "The Freedom courier is dead, but mil_courier_dead is missing. tasks_military.xml completes the \"kill the courier\" step on that flag; without it the step can never complete.",
            [SocAllSpawn, "config/gameplay/tasks_military.xml"]),
        new QuestRule(
            "soc.informer-dead",
            "stalker-soc",
            "Freedom informer task after his death",
            "mil_ara",
            "mil_ara_dead",
            null,
            "The informer is dead, but mil_ara_dead is missing. tasks_military.xml completes the \"deal with the informer\" step on that flag; without it the step can never complete.",
            [SocAllSpawn, "config/gameplay/tasks_military.xml"]),
    ];

    public static QuestDoctorReport Analyze(ReadOnlySpan<byte> data)
    {
        var save = SaveDoctor.Analyze(data);
        if (save.Overview is not { } overview)
        {
            return new QuestDoctorReport(
                save.Status,
                FormatId: null,
                QuestStatesAvailable: false,
                "The save must parse before quest or task state fields can be inspected.",
                []);
        }

        if (!Rules.Any(rule => rule.FormatId == overview.FormatId))
        {
            return new QuestDoctorReport(
                SaveDoctorStatus.Unknown,
                overview.FormatId,
                QuestStatesAvailable: false,
                "Quest Doctor has evidence-backed rules only for Shadow of Chernobyl and Clear Sky saves; no states or preventing fixes can be inferred for this format.",
                []);
        }

        var parsed = XRayTrilogyReader.FromBytes(data.ToArray());
        var known = parsed.RelationRegistry is null ? null : new HashSet<string>(parsed.ActorKnownInfo, StringComparer.Ordinal);
        var states = Evaluate(parsed.FormatId, known, parsed.FindCreatureVitals);
        var broken = states.Count(state => state.State == QuestTaskStatus.Broken);
        var summary = broken == 0
            ? "No known broken quest was found. Only the listed rules are checked."
            : $"{broken} known broken quest(s) found; each can be repaired by adding one info portion.";
        return new QuestDoctorReport(
            broken == 0 ? SaveDoctorStatus.Ok : SaveDoctorStatus.Warning,
            overview.FormatId,
            QuestStatesAvailable: true,
            summary,
            states);
    }

    /// <summary>Adds the missing info portions for every broken quest. Returns null when nothing is proven broken.</summary>
    public static PreparedEdit? PrepareRepair(ReadOnlySpan<byte> data)
    {
        var report = Analyze(data);
        var flags = report.States
            .Where(state => state is { State: QuestTaskStatus.Broken, MissingInfoPortion: not null })
            .Select(state => state.MissingInfoPortion!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return flags.Length == 0 ? null : XRayInfoPortionWriter.AddActorInfo(data, flags);
    }

    /// <summary>Throws unless the written save still parses and no rule reports a broken quest any more.</summary>
    public static void VerifyRepair(ReadOnlySpan<byte> written)
    {
        var report = Analyze(written);
        if (!report.QuestStatesAvailable || report.States.Any(state => state.State == QuestTaskStatus.Broken))
        {
            throw new InvalidDataException("The repaired save still reports a broken quest.");
        }
    }

    internal static List<QuestTaskState> Evaluate(
        string formatId,
        IReadOnlySet<string>? known,
        Func<string, IReadOnlyList<XRayCreatureVitals>> findVitals)
    {
        var states = new List<QuestTaskState>();
        foreach (var rule in Rules.Where(rule => rule.FormatId == formatId))
        {
            var vitals = findVitals(rule.NpcSection);
            string state;
            string detail;
            string reason;
            if (known is null)
            {
                state = QuestTaskStatus.Unknown;
                detail = "The save has no readable actor info list.";
                reason = "no-info-list";
            }
            else if (known.Contains(rule.InfoPortion))
            {
                state = QuestTaskStatus.Ok;
                detail = "The info portion is already set.";
                reason = "flag-set";
            }
            else if (vitals.Count == 0)
            {
                state = QuestTaskStatus.Unknown;
                detail = "The NPC is not in the save (or could not be read); a missing object does not prove his death.";
                reason = "npc-missing";
            }
            else if (vitals.Any(v => !v.IsDead))
            {
                state = QuestTaskStatus.Ok;
                detail = "The NPC is alive.";
                reason = "alive";
            }
            else if (rule.TooLateInfo is { } tooLate && known.Contains(tooLate))
            {
                state = QuestTaskStatus.Unknown;
                detail = $"The NPC is dead without {rule.InfoPortion}, but {tooLate} is already set: the quest line has branched and adding the flag no longer repairs it.";
                reason = "too-late";
            }
            else
            {
                state = QuestTaskStatus.Broken;
                detail = rule.Explanation;
                reason = "dead-without-flag";
            }

            states.Add(new QuestTaskState(rule.Id, state, rule.PreventingFixId)
            {
                Title = rule.Title,
                Detail = detail,
                Reason = reason,
                NeedsPreventingFix = rule.NeedsPreventingFix,
                NpcSection = rule.NpcSection,
                MissingInfoPortion = state == QuestTaskStatus.Broken ? rule.InfoPortion : null,
                References = rule.References,
            });
        }

        return states;
    }
}
