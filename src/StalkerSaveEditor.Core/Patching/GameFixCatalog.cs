using StalkerSaveEditor.Core.Diagnostics;
using System.Text.Json;
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
    private const string ResourceName = "StalkerSaveEditor.Core.Patching.Data.game-fixes.json";
    private static CatalogData? _data;

    /// <summary>
    /// Retail definitions and the per-operation Enhanced Edition file hashes, shipped as embedded JSON
    /// (<c>Patching/Data/game-fixes.json</c>): data, not code. EE variants are derived below.
    /// </summary>
    private static CatalogData Data => _data ??= Load();

    private static IReadOnlyList<GameFixDefinition> Definitions => Data.Definitions;

    private static CatalogData Load()
    {
        using var stream = typeof(GameFixCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The Game Fix catalogue resource is missing.");
        var data = JsonSerializer.Deserialize(stream, GameFixCatalogJsonContext.Default.CatalogData)
            ?? throw new InvalidOperationException("The Game Fix catalogue is empty.");
        if (data.Definitions.Count == 0 || data.Definitions.Select(fix => fix.Id).Distinct(StringComparer.Ordinal).Count() != data.Definitions.Count)
            throw new InvalidOperationException("The Game Fix catalogue has no definitions or duplicate ids.");
        return data;
    }

    internal sealed record CatalogData(
        IReadOnlyList<GameFixDefinition> Definitions,
        IReadOnlyDictionary<string, string[]> EnhancedEditionSha256);

    public const string DatasetVersion = "2026.09.4";
    public const string PreviousDatasetVersion = "2026.09.3";
    public const GameFixPreset DefaultPreset = GameFixPreset.Recommended;

    private static IReadOnlyList<GameFixDefinition>? _all;

    /// <summary>Retail definitions followed by their Enhanced Edition variants.</summary>
    public static IReadOnlyList<GameFixDefinition> All => _all ??= [.. Definitions, .. EnhancedEditionVariants()];

    public static IReadOnlyList<GameFixDefinition> ForGame(GameTarget game) =>
        All.Where(definition => definition.Game == game).ToArray();

    public static IReadOnlyDictionary<GameFixCategory, int> CategoryCounts(GameTarget game) =>
        Enum.GetValues<GameFixCategory>().ToDictionary(
            category => category,
            category => Definitions.Count(definition => definition.Game == game && definition.Category == category));

    /// <summary>Preset counts at the previous shipped catalogue version; update alongside DatasetVersion.</summary>
    public static int PreviousPresetCount(GameTarget game, GameFixPreset preset) =>
        preset == GameFixPreset.Recommended
            ? game switch
            {
                GameTarget.ClearSky => 23,
                GameTarget.ShadowOfChernobyl => 15,
                GameTarget.CallOfPripyat => 11,
                _ => 0,
            }
            : 0;

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
            GameFixPreset.AllSafeFixes => definition.Category is GameFixCategory.Essential or GameFixCategory.Recommended,
            _ => false,
        };

    public static bool TryGet(string id, out GameFixDefinition? definition)
    {
        definition = Definitions.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return definition is not null;
    }

    private static GameTarget EnhancedEditionOf(GameTarget game) => game switch
    {
        GameTarget.ShadowOfChernobyl => GameTarget.ShadowOfChernobylEnhancedEdition,
        GameTarget.ClearSky => GameTarget.ClearSkyEnhancedEdition,
        GameTarget.CallOfPripyat => GameTarget.CallOfPripyatEnhancedEdition,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    private static string EnhancedEditionBuild(GameTarget game) => game switch
    {
        GameTarget.ShadowOfChernobyl => "24067120",
        GameTarget.ClearSky => "24067129",
        GameTarget.CallOfPripyat => "24067133",
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    private static IEnumerable<GameFixDefinition> EnhancedEditionVariants()
    {
        foreach (var fix in Definitions)
        {
            if (!Data.EnhancedEditionSha256.TryGetValue(fix.Id, out var hashes)) continue;
            if (hashes.Length != fix.TextPatches.Count + fix.SpawnEdits.Count)
                throw new InvalidOperationException("EE hash count does not match the operations of " + fix.Id);
            var build = EnhancedEditionBuild(fix.Game);
            yield return fix with
            {
                Id = fix.Id + ".ee",
                Game = EnhancedEditionOf(fix.Game),
                SupportedSteamBuildIds = [build],
                TextPatches = fix.TextPatches.Select((operation, index) => operation with { ExpectedFileSha256 = hashes[index] }).ToArray(),
                SpawnEdits = fix.SpawnEdits.Select((edit, index) => edit with { ExpectedFileSha256 = hashes[fix.TextPatches.Count + index] }).ToArray(),
                Source = fix.Source + " Enhanced Edition variant: same anchors, EE file hashes.",
                DetectionMethod = $"Steam build {build}, exact archived EE source-file SHA-256 per operation, and unique exact text anchors.",
                DependsOn = fix.DependsOn.Select(id => id + ".ee").ToArray(),
                ConflictsWith = fix.ConflictsWith.Select(id => id + ".ee").ToArray(),
            };
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(GameFixCatalog.CatalogData))]
internal sealed partial class GameFixCatalogJsonContext : JsonSerializerContext
{
}
