using System.Globalization;
using System.Text.RegularExpressions;
using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Core.Content;

/// <summary>
/// Builds an item, upgrade and community catalog from the LTX and string tables of an
/// installed X-Ray game (port of the Python oracle's <c>XRayCatalogProvider</c> and
/// <c>factions_from_sections</c>). Everything comes from the game's own files; nothing is guessed.
/// </summary>
internal static partial class InstalledGameCatalogBuilder
{
    public static CatalogBundle? Build(
        string releaseId,
        IReadOnlyDictionary<string, LtxSection> sections,
        IReadOnlyDictionary<string, string> strings)
    {
        var resolved = LtxDocument.Resolve(sections).ToArray();
        var items = BuildItems(resolved, strings);
        if (items.Count == 0) return null;
        var itemCatalog = new ItemCatalog(releaseId, items);
        var upgrades = BuildUpgrades(releaseId, resolved, strings, items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal));
        var factions = BuildFactions(releaseId, resolved, strings);
        return new CatalogBundle(releaseId, itemCatalog, factions, upgrades);
    }

    private static List<ItemDefinition> BuildItems(
        IEnumerable<(LtxSection Section, IReadOnlyDictionary<string, string> Values)> resolved,
        IReadOnlyDictionary<string, string> strings)
    {
        var items = new List<ItemDefinition>();
        foreach (var (section, values) in resolved)
        {
            var name = section.Name;
            var category = Category(name, values);
            // *_hud sections describe first-person models, not inventory items.
            if (category is null || name.StartsWith('$') || name.EndsWith("_hud", StringComparison.OrdinalIgnoreCase)) continue;
            // A section's own name wins over an inherited one (CoP tech materials inherit
            // inv_name_short from device_pda but define their own inv_name).
            var nameKey = Own(section, "inv_name_short") ?? Own(section, "inv_name") ??
                          Get(values, "inv_name_short") ?? Get(values, "inv_name");
            var maxStack = ParseInt(Get(values, "box_size")) ?? ParseInt(Get(values, "inv_max_count"));
            items.Add(new ItemDefinition(
                name,
                nameKey is not null && strings.TryGetValue(nameKey, out var display) ? display : null,
                category,
                NonNegative(ParseDouble(Get(values, "inv_weight"))),
                NonNegative(ParseInt(Get(values, "inv_grid_width"))),
                NonNegative(ParseInt(Get(values, "inv_grid_height"))),
                NonNegative(maxStack),
                ParseSlots(Get(values, "inv_grid_slot")),
                $"{section.Source}#{name}",
                SerializationFamily(name, values, category),
                NonNegative(ParseInt(Get(values, "inv_grid_x"))),
                NonNegative(ParseInt(Get(values, "inv_grid_y"))),
                Get(values, "icons_texture") ?? "ui_icon_equipment",
                Get(values, "class"),
                nameKey));
        }

        return items;
    }

    private static UpgradeCatalog? BuildUpgrades(
        string releaseId,
        IReadOnlyList<(LtxSection Section, IReadOnlyDictionary<string, string> Values)> resolved,
        IReadOnlyDictionary<string, string> strings,
        IReadOnlySet<string> itemKeys)
    {
        var aliases = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (section, _) in resolved)
        {
            if (string.Equals(section.Name, "upgraded_inventory", StringComparison.OrdinalIgnoreCase))
            {
                aliases.UnionWith(section.Entries);
            }
        }

        var upgrades = new List<UpgradeDefinition>();
        foreach (var (section, values) in resolved)
        {
            if (!section.Name.StartsWith("up_", StringComparison.OrdinalIgnoreCase) || Get(values, "section") is null) continue;
            var source = section.Source.Replace('\\', '/').ToLowerInvariant();
            var candidate = UpgradeItemKey(source);
            var itemKey = candidate is not null && itemKeys.Contains(candidate) ? candidate : null;
            var applicable = candidate is null
                ? []
                : aliases.Where(alias => alias == candidate || alias.StartsWith(candidate + "_", StringComparison.Ordinal)).ToArray();
            var nameKey = Get(values, "name");
            upgrades.Add(new UpgradeDefinition(
                section.Name,
                nameKey is not null && strings.TryGetValue(nameKey, out var display) ? display : nameKey,
                source.Contains("/weapons/upgrades/", StringComparison.Ordinal) ? "weapon"
                    : source.Contains("/outfit_upgrades/", StringComparison.Ordinal) ? "outfit" : null,
                itemKey,
                $"{section.Source}#{section.Name}",
                releaseId,
                Get(values, "section"),
                Get(values, "property"),
                Get(values, "icon"),
                applicable));
        }

        return upgrades.Count == 0 ? null : new UpgradeCatalog(releaseId, upgrades);
    }

    private static FactionCatalog? BuildFactions(
        string releaseId,
        IReadOnlyList<(LtxSection Section, IReadOnlyDictionary<string, string> Values)> resolved,
        IReadOnlyDictionary<string, string> strings)
    {
        (string Key, int Id)[] communities = [];
        var communitySource = string.Empty;
        IReadOnlyDictionary<string, string>? gameRelations = null;
        IReadOnlyDictionary<string, string>? relationRows = null;
        IReadOnlyDictionary<string, string>? actionPoints = null;
        foreach (var (section, values) in resolved)
        {
            switch (section.Name.ToLowerInvariant())
            {
                case "game_relations":
                    if (communities.Length == 0)
                    {
                        communities = CommunityPairs(Get(values, "communities"));
                        if (communities.Length > 0) communitySource = $"{section.Source}#communities";
                    }

                    gameRelations ??= values;
                    break;
                case "communities_relations":
                    relationRows ??= values;
                    break;
                case "action_points":
                    actionPoints ??= values;
                    break;
            }
        }

        if (communities.Length == 0) return null;
        var factions = communities
            .Select(pair => new FactionDefinition(pair.Key, strings.GetValueOrDefault(pair.Key), communitySource, releaseId, pair.Id))
            .ToArray();
        var relations = new List<FactionRelation>();
        foreach (var (key, _) in communities)
        {
            var row = IntList(relationRows is null ? null : Get(relationRows, key));
            for (var column = 0; column < row.Length && column < communities.Length; column++)
            {
                relations.Add(new FactionRelation(key, communities[column].Key, row[column]));
            }
        }

        var limits = IntList(actionPoints is null ? null : Get(actionPoints, "community_goodwill_limits"));
        return new FactionCatalog(
            releaseId,
            factions,
            relations,
            limits.Length >= 2 ? limits[0] : null,
            limits.Length >= 2 ? limits[1] : null,
            ParseInt(gameRelations is null ? null : Get(gameRelations, "attitude_neutal_threshold")),
            ParseInt(gameRelations is null ? null : Get(gameRelations, "attitude_friend_threshold")));
    }

    internal static string? Category(string name, IReadOnlyDictionary<string, string> values)
    {
        var className = (Get(values, "class") ?? string.Empty).ToUpperInvariant();
        var lowered = name.ToLowerInvariant();
        if (className == "AMMO" || lowered.StartsWith("ammo_", StringComparison.Ordinal)) return "ammo";
        if (Get(values, "weapon_class") is not null || Get(values, "ammo_class") is not null ||
            lowered.StartsWith("wpn_", StringComparison.Ordinal) || lowered.StartsWith("weapon_", StringComparison.Ordinal)) return "weapon";
        if (className.StartsWith("G_", StringComparison.Ordinal) || StartsWithAny(lowered, "grenade", "rgd", "f1_")) return "grenade";
        if (className is "DETECTOR" or "DEVICE" || StartsWithAny(lowered, "device_", "detector_")) return "device";
        if (StartsWithAny(lowered, "outfit_", "scientific_", "helm_", "armor_")) return "outfit";
        if (StartsWithAny(lowered, "af_", "artifact_")) return "artifact";
        if (StartsWithAny(lowered, "medkit", "bandage", "antirad", "drug_", "food_", "bread", "kolbasa", "vodka", "energy")) return "consumable";
        if (values.ContainsKey("inv_name") || values.ContainsKey("inv_name_short")) return "item";
        return null;
    }

    /// <summary>Maps a config definition to its STATE/UPDATE serializer family (only families visible in the server sources).</summary>
    internal static string SerializationFamily(string name, IReadOnlyDictionary<string, string> values, string? category)
    {
        var lowered = name.ToLowerInvariant();
        var className = (Get(values, "class") ?? string.Empty).Trim().ToUpperInvariant();
        if (category == "ammo" || className == "AMMO") return "ammo";
        if (lowered == "device_torch") return "torch";
        if (lowered == "device_pda") return "pda";
        if (StartsWithAny(lowered, "detector_", "device_detector")) return "detector";
        if (category == "weapon" || StartsWithAny(lowered, "wpn_", "weapon_"))
        {
            if (className == "WP_KNIFE" || lowered.EndsWith("_knife", StringComparison.Ordinal)) return "weapon";
            if (className is "WP_BM16" or "WP_RG6" or "WP_SHOTG" or "WP_SPAS12" or "WP_TOZ34") return "weapon_shotgun";
            if (className is "WP_AK74" or "WP_FN2000" or "WP_GROZA") return "weapon_wgl";
            if (className.StartsWith("WP_", StringComparison.Ordinal)) return "weapon_magazined";
        }

        if (category == "outfit" || className is "E_STLK" or "E_SCI" or "E_MILIT" or "E_EXO") return "outfit";
        if (className is "II_PDA" or "IITEM_PDA") return "pda";
        if (className is "II_DOCUMENT" or "IITEM_DOCUMENT") return "document";
        return "base";
    }

    /// <summary>w_&lt;item&gt;_up.ltx → wpn_&lt;item&gt;, o_&lt;item&gt;_up.ltx → &lt;item&gt;; CoP names helmet files directly.</summary>
    private static string? UpgradeItemKey(string source)
    {
        var fileName = source[(source.LastIndexOf('/') + 1)..];
        if (!fileName.EndsWith("_up.ltx", StringComparison.Ordinal)) return null;
        var stem = fileName[..^"_up.ltx".Length];
        if (source.Contains("/weapons/upgrades/", StringComparison.Ordinal) && stem.StartsWith("w_", StringComparison.Ordinal))
        {
            return stem.Length > 2 ? "wpn_" + stem[2..] : null;
        }

        if (source.Contains("/outfit_upgrades/", StringComparison.Ordinal))
        {
            if (stem.StartsWith("o_", StringComparison.Ordinal)) return stem.Length > 2 ? stem[2..] : null;
            return stem.Length > 0 ? stem : null;
        }

        return null;
    }

    private static (string Key, int Id)[] CommunityPairs(string? value)
    {
        if (value is null) return [];
        var tokens = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || tokens.Length % 2 != 0) return [];
        var pairs = new List<(string, int)>();
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (ParseInt(tokens[index + 1]) is not { } id || id < 0) return [];
            pairs.Add((tokens[index], id));
        }

        return pairs.ToArray();
    }

    private static int[] IntList(string? value)
    {
        if (value is null) return [];
        var result = new List<int>();
        foreach (var token in value.Split(','))
        {
            if (ParseInt(token) is not { } number) return [];
            result.Add(number);
        }

        return result.ToArray();
    }

    private static string? Own(LtxSection section, string key) => section.Values.GetValueOrDefault(key) is { Length: > 0 } value ? value : null;

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.GetValueOrDefault(key) is { Length: > 0 } value ? value : null;

    private static bool StartsWithAny(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));

    private static int? ParseInt(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) return hex;
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number;
        return double.TryParse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var real) &&
               real is >= int.MinValue and <= int.MaxValue
            ? (int)real
            : null;
    }

    private static double? ParseDouble(string? value) =>
        value is not null && double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
        double.IsFinite(number)
            ? number
            : null;

    private static int? NonNegative(int? value) => value is < 0 ? null : value;

    private static double? NonNegative(double? value) => value is < 0 ? null : value;

    private static string[] ParseSlots(string? value) =>
        value is null ? [] : SlotSeparator().Split(value).Select(token => token.Trim()).Where(token => token.Length > 0).ToArray();

    [GeneratedRegex("[,; ]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlotSeparator();
}
