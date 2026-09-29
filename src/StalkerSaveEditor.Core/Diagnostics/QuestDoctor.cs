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
    IReadOnlyList<string> References);

/// <summary>Reports quest breaks that are proven by the save's own data and prepares the one-flag repair.</summary>
public static class QuestDoctor
{
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
            "Wolf is dead, but esc_wolf_dead is missing. The Cordon task list (tm_escape.ltx) cancels his rescue and escort tasks on that flag; without it the reward task stays active forever.",
            [SrpHistory]),
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

        if (overview.FormatId != "stalker-cs")
        {
            return new QuestDoctorReport(
                SaveDoctorStatus.Unknown,
                overview.FormatId,
                QuestStatesAvailable: false,
                "Quest Doctor has evidence-backed rules only for Clear Sky saves; no states or preventing fixes can be inferred for this format.",
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
            if (known is null)
            {
                state = QuestTaskStatus.Unknown;
                detail = "The save has no readable actor info list.";
            }
            else if (known.Contains(rule.InfoPortion))
            {
                state = QuestTaskStatus.Ok;
                detail = "The info portion is already set.";
            }
            else if (vitals.Count == 0)
            {
                state = QuestTaskStatus.Unknown;
                detail = "The NPC is not in the save (or could not be read); a missing object does not prove his death.";
            }
            else if (vitals.Any(v => !v.IsDead))
            {
                state = QuestTaskStatus.Ok;
                detail = "The NPC is alive.";
            }
            else
            {
                state = QuestTaskStatus.Broken;
                detail = rule.Explanation;
            }

            states.Add(new QuestTaskState(rule.Id, state, rule.PreventingFixId)
            {
                Title = rule.Title,
                Detail = detail,
                NpcSection = rule.NpcSection,
                MissingInfoPortion = state == QuestTaskStatus.Broken ? rule.InfoPortion : null,
                References = rule.References,
            });
        }

        return states;
    }
}
