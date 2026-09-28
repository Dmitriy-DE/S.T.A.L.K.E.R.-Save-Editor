using StalkerSaveEditor.Core.Diagnostics;
using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Core.Patching;

[JsonConverter(typeof(JsonStringEnumConverter<GameFixPreset>))]
public enum GameFixPreset
{
    EssentialOnly,
    Recommended,
    AllSafeFixes,
    Custom,
}

/// <summary>Evidence-backed fix definitions shipped by this application.</summary>
public static class GameFixCatalog
{
    private static readonly IReadOnlyList<GameFixDefinition> Definitions =
    [
        new GameFixDefinition(
            "cs.quest.dead-wild-napr",
            GameTarget.ClearSky,
            "1.0.0",
            "Stop Flea Market quests after Wild Napr dies",
            ["11450472"],
            GameFixCategory.Essential,
            GameFixMaturity.Experimental,
            [],
            [],
            [
                new TextPatchOperation(
                    "gamedata/configs/creatures/spawn_sections_garbage.ltx",
                    "character_profile     = gar_digger_quester\r\n",
                    "character_profile     = gar_digger_quester\r\non_death             = %+gar_flea_market_stop_quest_line%\r\n")
                {
                    ExpectedFileSha256 = "029ddb9331d44c8c0d6aaf3a5791bbb53f7e738c8efba7fc121a30a90a0a5078",
                },
            ],
            "Adapted from SRP v1.1.5; the target is limited to the verified Clear Sky Steam build.")
        {
            Problem = "Wild Napr can die while offline without stopping his storyline tasks; later tasks can target a missing logic@work5 and crash.",
            Description = "Adds the NPC death info-portion used by Clear Sky's task definitions to stop the Flea Market quest line. Only deaths processed after this change are covered; existing affected saves were not verified.",
            Implementation = GameFixImplementationType.ExactTextReplacement,
            RequiresNewGame = false,
            SaveCompatibility = GameFixSaveCompatibility.Unknown,
            VerificationState = GameFixVerificationState.RetailFilesVerified,
            DetectionMethod = "Exact SHA-256 of the effective spawn_sections_garbage.ltx from Steam build 11450472 and one unique CRLF text anchor.",
            References = ["https://github.com/Decane/SRP/blob/master/SRP%20v1.1.5%20-%20Version%20History.txt"],
        },
    ];

    public const string DatasetVersion = "2026.09";
    public const GameFixPreset DefaultPreset = GameFixPreset.Recommended;

    public static IReadOnlyList<GameFixDefinition> All => Definitions;

    public static IReadOnlyList<GameFixDefinition> ForGame(GameTarget game) =>
        Definitions.Where(definition => definition.Game == game).ToArray();

    public static IReadOnlyDictionary<GameFixCategory, int> CategoryCounts(GameTarget game) =>
        Enum.GetValues<GameFixCategory>().ToDictionary(
            category => category,
            category => Definitions.Count(definition => definition.Game == game && definition.Category == category));

    /// <summary>Presets never select Experimental or ResearchOnly definitions implicitly.</summary>
    public static IReadOnlyList<GameFixDefinition> ForPreset(GameTarget game, GameFixPreset preset)
    {
        if (preset == GameFixPreset.Custom) return [];
        return Definitions.Where(definition => definition.Game == game && IsIncludedInPreset(definition, preset)).ToArray();
    }

    internal static bool IsIncludedInPreset(GameFixDefinition definition, GameFixPreset preset) =>
        preset != GameFixPreset.Custom &&
        definition.Maturity == GameFixMaturity.Validated &&
        definition.Category != GameFixCategory.Experimental &&
        preset switch
        {
            GameFixPreset.EssentialOnly => definition.Category == GameFixCategory.Essential,
            GameFixPreset.Recommended => definition.Category is GameFixCategory.Essential or GameFixCategory.Recommended,
            GameFixPreset.AllSafeFixes => definition.Category is GameFixCategory.Essential or GameFixCategory.Recommended or GameFixCategory.Optional or GameFixCategory.Community,
            _ => false,
        };

    public static bool TryGet(string id, out GameFixDefinition? definition)
    {
        definition = Definitions.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return definition is not null;
    }
}
