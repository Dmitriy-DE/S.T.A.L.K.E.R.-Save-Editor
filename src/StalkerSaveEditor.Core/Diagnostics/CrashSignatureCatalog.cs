using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Diagnostics;

/// <summary>What the user can do about a known crash.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CrashAdvice>))]
public enum CrashAdvice
{
    /// <summary>Install the named Game Fix; the crash cannot recur after it.</summary>
    InstallFix,
    /// <summary>Run Quest Doctor on the last save; the named rule repairs the cause.</summary>
    RepairSave,
    /// <summary>A transient engine or spawn failure: load an earlier save.</summary>
    ReloadEarlierSave,
    /// <summary>Fixed only by a community patch (SRP for Clear Sky, ZRP for Shadow of Chernobyl); nothing we ship.</summary>
    CommunityPatch,
    /// <summary>The save itself is damaged; only an older save helps.</summary>
    CorruptSave,
}

/// <summary>A crash message documented by a community patch history, matched against a game log.</summary>
public sealed record CrashSignature(
    string Id,
    string Game,
    string Title,
    CrashAdvice Advice,
    string Explanation,
    string Source)
{
    public string? FixId { get; init; }

    public string? QuestRuleId { get; init; }

    [JsonIgnore]
    internal Regex Pattern { get; init; } = null!;
}

/// <summary>
/// Crash messages quoted verbatim in the SRP v1.1.5 history (Clear Sky) and ZRP 1.09 CrashesStillInTheGame
/// (Shadow of Chernobyl). A match names the documented cause; it never changes a file.
/// </summary>
public static class CrashSignatureCatalog
{
    private const string Srp = "https://github.com/Decane/SRP/blob/master/SRP%20v1.1.5%20-%20Version%20History.txt";
    private const string Zrp = "ZRP 1.09 XR3a, gamedata/docs/CrashesStillInTheGame.txt (metacognix.com)";
    private const string ClearSky = "cs";
    private const string Shadow = "soc";

    private static Regex P(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<CrashSignature> All { get; } =
    [
        new("cs.wrong-target-wild-napr", ClearSky, "Task targets Wild Napr after his death", CrashAdvice.RepairSave,
            "A Flea Market task was given with Wild Napr as its target after he died offline.", Srp)
        {
            Pattern = P(@"wrong target for storyline quest:\s*logic@work5,\s*gar_smart_terrain_6_3"),
            FixId = "cs.quest.dead-wild-napr",
            QuestRuleId = "cs.wild-napr-dead",
        },
        new("cs.insufficient-smart-jobs", ClearSky, "Too many stalkers for one camp", CrashAdvice.CommunityPatch,
            "More squads were sent to a smart terrain than it has jobs (Dark Valley wagon, Army Warehouses rocks and others).", Srp)
        {
            Pattern = P(@"Insufficient smart_terrain jobs"),
        },
        new("cs.sim-combat-actor-nil", ClearSky, "Loading a save during a squad fight", CrashAdvice.InstallFix,
            "sim_combat.script reads the actor before it exists right after a save is loaded; loading again usually works.", Srp)
        {
            Pattern = P(@"sim_combat\.script:\d+:\s*attempt to index field 'actor' \(a nil value\)"),
            FixId = "cs.crash.sim-combat",
        },
        new("cs.sim-combat-attack-squad-nil", ClearSky, "Help task for a squad that no longer exists", CrashAdvice.InstallFix,
            "The game evaluated a 'help' task for an attacking squad that was already gone.", Srp)
        {
            Pattern = P(@"sim_combat\.script:\d+:\s*attempt to index local 'attack_squad_obj'"),
            FixId = "cs.crash.sim-combat",
        },
        new("cs.squad-current-action-nil", ClearSky, "Smart terrain captured by a squad without an action", CrashAdvice.InstallFix,
            "A squad captured a smart terrain while it had no current action.", Srp)
        {
            Pattern = P(@"sim_squad_generic\.script:\d+:\s*attempt to index field 'current_action'"),
            FixId = "cs.crash.squad-action-finished-twice",
        },
        new("cs.squad-help-task-nil", ClearSky, "'Help' task with nothing to offer", CrashAdvice.InstallFix,
            "The game tried to offer a delayed defence ('help') task, but no task fitted.", Srp)
        {
            Pattern = P(@"sim_squad_generic\.script:\d+:\s*attempt to index local 'task' \(a nil value\)"),
            FixId = "cs.crash.squad-action-finished-twice",
        },
        new("cs.marsh-creature-no-squad", ClearSky, "Marsh creature attacked a stalker without a squad", CrashAdvice.InstallFix,
            "The marsh creature ambush tried to make the victim's squad react, but the victim had no squad.", Srp)
        {
            Pattern = P(@"sr_bloodsucker\.script:\d+:\s*attempt to index field 'npc_squad'"),
            FixId = "cs.crash.marsh-creature-no-squad",
        },
        new("cs.agroprom-orest-path", ClearSky, "Orest left his spot at the Agroprom loner base", CrashAdvice.InstallFix,
            "Orest's movement restrictor does not contain his own patrol path.", Srp)
        {
            Pattern = P(@"patrol path \[agr_stalker_leader_walk\] is inaccessible"),
            FixId = "cs.crash.agroprom-orest-path",
        },
        new("cs.all-spawn-cordon-waypoint", ClearSky, "Waypoint off the AI map at the Cordon bonfire", CrashAdvice.InstallFix,
            "A waypoint of the 'Bonfire in forest' camp lies outside the AI map.", Srp)
        {
            Pattern = P(@"esc_smart_terrain_3_7_walker_1_walk"),
            FixId = "cs.crash.all-spawn-errors",
        },
        new("cs.storyline-task-missing-npc", ClearSky, "Story task for an NPC who is not there (Wild Napr)", CrashAdvice.InstallFix,
            "The game tried to give a story task whose target NPC is fighting or has died offline.", Srp)
        {
            Pattern = P(@"wrong target for storyline quest"),
            FixId = "cs.crash.capture-task-missing-squad",
        },
        new("cs.all-spawn-jobs-mil-2-1", ClearSky, "Too many squads for the Army Warehouses 'Camp amidst rocks'", CrashAdvice.InstallFix,
            "The camp accepts more squads than it has jobs. The fix applies in a new game.", Srp)
        {
            Pattern = P(@"Insufficient smart_terrain jobs mil_smart_terrain_2_1"),
            FixId = "cs.crash.all-spawn-errors",
        },
        new("cs.all-spawn-mil-path", ClearSky, "Missing path between Army Warehouses camps", CrashAdvice.InstallFix,
            "A camp's list of neighbours misses a link that mutant attacks use. The fix applies in a new game.", Srp)
        {
            Pattern = P(@"Path between \[mil_smart_terrain_7_11\] and \[mil_smart_terrain_7_10\] doesnt exist"),
            FixId = "cs.crash.all-spawn-errors",
        },
        new("cs.missing-backpack-model", ClearSky, "Missing backpack model", CrashAdvice.InstallFix,
            "The stalker corpse model points at a file Clear Sky does not ship.", Srp)
        {
            Pattern = P(@"Can't find model file 'dynamics\\equipments\\item_rukzak\.ogf'"),
            FixId = "cs.crash.missing-backpack-model",
        },
        new("cs.kamp-empty-interval", ClearSky, "Campfire with nobody to talk", CrashAdvice.InstallFix,
            "The campfire story scheme picked a random speaker from an empty list.", Srp)
        {
            Pattern = P(@"xr_kamp\.script:\d+:\s*bad argument #1 to 'random' \(interval is empty\)"),
            FixId = "cs.crash.kamp-no-animation",
        },
        new("cs.robbery-squad-left", ClearSky, "Robbers left during a hold-up", CrashAdvice.InstallFix,
            "A robber squad walked off to another camp in the middle of a hold-up.", Srp)
        {
            Pattern = P(@"sr_robbery\.script:\d+:\s*attempt to index field '\?' \(a nil value\)"),
            FixId = "cs.crash.robbery-squad-left",
        },
        new("cs.robbery-manager-nil", ClearSky, "Robbery leader chosen from a squad that already left", CrashAdvice.InstallFix,
            "The robbery scheme still counted a squad that had left the camp when it picked the leader.", Srp)
        {
            Pattern = P(@"actor_reaction\.script:\d+:\s*attempt to index local 'manager'"),
            FixId = "cs.crash.robbery-leader-offline",
        },
        new("cs.capture-task-missing-squad", ClearSky, "Capture task for a squad that does not exist", CrashAdvice.InstallFix,
            "The game tried to give a 'capture' task to a squad that no longer exists.", Srp)
        {
            Pattern = P(@"task_objects\.script:\d+:\s*attempt to index field '\?' \(a nil value\)"),
            FixId = "cs.crash.capture-task-missing-squad",
        },
        new("cs.anomaly-art-nil", ClearSky, "Artefact spawn in an anomaly field", CrashAdvice.InstallFix,
            "An anomaly field referenced an artefact that was already gone.", Srp)
        {
            Pattern = P(@"bind_anomaly_zone\.script:\d+:\s*attempt to index local 'art'"),
            FixId = "cs.crash.anomaly-zone-missing-artefact",
        },
        new("cs.saving-too-much", ClearSky, "Save data too large", CrashAdvice.CommunityPatch,
            "The scripts wrote more data into a save packet than the engine allows.", Srp)
        {
            Pattern = P(@"You are saving too much"),
        },
        new("cs.patrol-point-cordon-bonfire", ClearSky, "Patrol point at the Cordon forest bonfire", CrashAdvice.CommunityPatch,
            "A stalker patrolling the 'Bonfire in forest' reached a waypoint that is not on the level graph.", Srp)
        {
            Pattern = P(@"patrol path\s*\[esc_smart_terrain_3_7_walker_1_walk\]"),
        },
        new("cs.patrol-red-forest-trader", ClearSky, "Red Forest mine trader left his desk", CrashAdvice.CommunityPatch,
            "The trader in the mine strayed from his spot and his patrol path became unreachable.", Srp)
        {
            Pattern = P(@"patrol path\s*\[red_smart_terrain_3_2_patrol_1_walk\] is inaccessible"),
        },
        new("cs.patrol-agroprom-orest", ClearSky, "Orest displaced in Agroprom", CrashAdvice.CommunityPatch,
            "Orest was pushed out of his space restrictor and his walk path became unreachable.", Srp)
        {
            Pattern = P(@"patrol path\s*\[agr_stalker_leader_walk\] is inaccessible"),
        },
        new("cs.missing-rukzak-model", ClearSky, "Missing backpack model", CrashAdvice.CommunityPatch,
            "The game referenced a backpack mesh that is not shipped.", Srp)
        {
            Pattern = P(@"Can't find model file 'dynamics\\equipments\\item_rukzak\.ogf'"),
        },
        new("cs.treasure-box-in-use", ClearSky, "Stash refilled while Stringov is alive", CrashAdvice.InstallFix,
            "Re-entering the Garbage tried to fill a stash that was already filled.", Srp)
        {
            Pattern = P(@"Unable to give treasure \[gar_treasure_quest_smuggler_weapons\]"),
            FixId = "cs.crash.treasure-given-twice",
        },
        new("cs.red-forest-missing-squad", ClearSky, "Witch Circle ambush squad already dead", CrashAdvice.InstallFix,
            "Following Strelok's helper into the ambush after the ambush squad was killed.", Srp)
        {
            Pattern = P(@"There is no squad \[red_pursuit_bounty_hunters_squad_\d+\] in sim_board"),
            FixId = "cs.crash.relation-to-missing-squad",
        },
        new("cs.military-dogs-path", ClearSky, "Army Warehouses mutant attack path", CrashAdvice.CommunityPatch,
            "A mutant squad attacking the military base had no path between two smart terrains (new game needed after the patch).", Srp)
        {
            Pattern = P(@"Path between \[mil_smart_terrain_7_11\] and \[mil_smart_terrain_7_10\] doesnt exist"),
        },
        new("soc.controller-body-state", Shadow, "Controller animation crash", CrashAdvice.ReloadEarlierSave,
            "A bad controller animation, usually while it is under attack. Kill controllers before they reach this state.", Zrp)
        {
            Pattern = P(@"dBodyStateValide\(b\)"),
        },
        new("soc.entity-not-found", Shadow, "Dropped weapon vanished while an NPC evaluated it", CrashAdvice.ReloadEarlierSave,
            "A killed NPC's weapon was destroyed or fell through the ground while another NPC considered picking it up.", Zrp)
        {
            Pattern = P(@"entity not found\.\s*id_parent=\d+\s*id_entity=\d+"),
        },
        new("soc.map-location-dead-object", Shadow, "Map spot bound to a destroyed body", CrashAdvice.CorruptSave,
            "The game destroyed a body but kept its map spot; every later save carries the damage.", Zrp)
        {
            Pattern = P(@"(?:SMapLocation|CMapLocation::UpdateSpot) binded to non-existent object"),
        },
        new("soc.no-level-in-graph", Shadow, "Creature spawned outside the level", CrashAdvice.ReloadEarlierSave,
            "A mutant or NPC was spawned outside the level or below it.", Zrp)
        {
            Pattern = P(@"there is no specified level in the game graph|There is no proper graph point neighbour"),
        },
        new("soc.unknown-weapon-rank", Shadow, "Weapon missing from the rank table", CrashAdvice.CommunityPatch,
            "A weapon (usually from a mod) has no entry in the weapon rank table.", Zrp)
        {
            Pattern = P(@"cannot find rank for"),
        },
        new("soc.format-no-value", Shadow, "Script string formatting error", CrashAdvice.CommunityPatch,
            "A script passed nothing to string.format; usually an incompatible mod.", Zrp)
        {
            Pattern = P(@"bad argument #2 to 'format' \(string expected, got no value\)"),
        },
    ];

    /// <summary>The first signature whose pattern occurs in the log, limited to one game when it is known.</summary>
    public static CrashSignature? Match(string text, string? game = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var key = NormalizeGame(game);
        return All.FirstOrDefault(signature => (key is null || signature.Game == key) && signature.Pattern.IsMatch(text));
    }

    private static string? NormalizeGame(string? game) => game?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "cs" or "cs-ee" or "clear sky" or "stalker-cs" or "stalker-cs-ee" => ClearSky,
        "soc" or "soc-ee" or "shadow of chernobyl" or "stalker-soc" or "stalker-soc-ee" => Shadow,
        _ => "none",
    };
}
