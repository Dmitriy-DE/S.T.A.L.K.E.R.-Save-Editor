using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace StalkerSaveEditor.Core.Catalogs;

public static class CatalogBundleReader
{
    private const string EmbeddedResourceName = "StalkerSaveEditor.Core.Catalogs.Data.catalogs.json";
    private static readonly Lazy<IReadOnlyDictionary<string, CatalogBundle>> EmbeddedBundles =
        new(LoadEmbeddedCore, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly HashSet<string> SupportedReleases = new(StringComparer.Ordinal)
    {
        "stalker-soc",
        "stalker-cs",
        "stalker-cop",
        "stalker2",
    };

    public static IReadOnlyDictionary<string, CatalogBundle> Load(ReadOnlySpan<byte> payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload.ToArray());
        }
        catch (JsonException exception)
        {
            throw new CatalogBundleException("Invalid JSON catalog bundle.", exception);
        }

        return LoadDocument(document);
    }

    private static ReadOnlyDictionary<string, CatalogBundle> LoadDocument(JsonDocument document)
    {
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema_version", out var schema) ||
                schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var schemaVersion) ||
                schemaVersion != 1)
            {
                throw new CatalogBundleException("Unsupported catalog bundle schema version.");
            }

            if (!root.TryGetProperty("releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogBundleException("Catalog bundle does not contain releases.");
            }

            var loaded = new Dictionary<string, CatalogBundle>(StringComparer.Ordinal);
            foreach (var releaseProperty in releases.EnumerateObject())
            {
                var releaseId = releaseProperty.Name;
                if (!SupportedReleases.Contains(releaseId))
                {
                    throw new CatalogBundleException($"Unsupported catalog release '{releaseId}'.");
                }

                if (releaseProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new CatalogBundleException($"Invalid catalog release '{releaseId}'.");
                }

                var items = ReadItems(releaseProperty.Value, releaseId);
                var factions = ReadFactions(releaseProperty.Value, releaseId);
                var upgrades = ReadUpgrades(releaseProperty.Value, releaseId);
                loaded.Add(releaseId, new CatalogBundle(releaseId, items, factions, upgrades));
            }

            return new ReadOnlyDictionary<string, CatalogBundle>(BorrowSeriesNames(loaded));
        }
    }

    public static IReadOnlyDictionary<string, CatalogBundle> LoadEmbedded() => EmbeddedBundles.Value;

    private static ReadOnlyDictionary<string, CatalogBundle> LoadEmbeddedCore()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new CatalogBundleException("Embedded official catalog bundle is missing.");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException exception)
        {
            throw new CatalogBundleException("Invalid JSON catalog bundle.", exception);
        }

        return LoadDocument(document);
    }

    private static ItemCatalog ReadItems(JsonElement release, string releaseId)
    {
        if (!release.TryGetProperty("items", out var rawItems) || rawItems.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogBundleException($"Invalid item list for release '{releaseId}'.");
        }

        var items = new List<ItemDefinition>();
        foreach (var rawItem in rawItems.EnumerateArray())
        {
            if (rawItem.ValueKind != JsonValueKind.Object ||
                !rawItem.TryGetProperty("key", out var rawKey) ||
                rawKey.ValueKind != JsonValueKind.String)
            {
                throw new CatalogBundleException($"Invalid item record for release '{releaseId}'.");
            }

            var key = rawKey.GetString()!;
            items.Add(new ItemDefinition(
                key,
                OptionalString(rawItem, "display_name", releaseId),
                OptionalString(rawItem, "category", releaseId),
                OptionalDouble(rawItem, "unit_weight", releaseId),
                OptionalInt(rawItem, "width", releaseId),
                OptionalInt(rawItem, "height", releaseId),
                OptionalInt(rawItem, "max_stack", releaseId),
                OptionalStringList(rawItem, "slots", releaseId),
                OptionalString(rawItem, "source", releaseId) ?? "generated-official-metadata",
                OptionalString(rawItem, "serialization_family", releaseId),
                OptionalInt(rawItem, "icon_x", releaseId),
                OptionalInt(rawItem, "icon_y", releaseId),
                OptionalString(rawItem, "icon_texture", releaseId),
                OptionalString(rawItem, "class_name", releaseId),
                OptionalString(rawItem, "display_name_key", releaseId),
                cost: OptionalInt(rawItem, "cost", releaseId)));
        }

        return new ItemCatalog(releaseId, items);
    }

    private static FactionCatalog? ReadFactions(JsonElement release, string releaseId)
    {
        if (!release.TryGetProperty("factions", out var rawFactions) ||
            rawFactions.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (rawFactions.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogBundleException($"Invalid faction list for release '{releaseId}'.");
        }

        var factions = new List<FactionDefinition>();
        foreach (var rawFaction in rawFactions.EnumerateArray())
        {
            if (rawFaction.ValueKind != JsonValueKind.Object ||
                !rawFaction.TryGetProperty("key", out var rawKey) ||
                rawKey.ValueKind != JsonValueKind.String)
            {
                throw new CatalogBundleException($"Invalid faction record for release '{releaseId}'.");
            }

            var factionRelease = rawFaction.TryGetProperty("release_id", out var rawRelease)
                ? rawRelease.ValueKind == JsonValueKind.String ? rawRelease.GetString() : null
                : releaseId;
            if (!string.Equals(factionRelease, releaseId, StringComparison.Ordinal))
            {
                throw new CatalogBundleException($"Foreign faction release id in '{releaseId}'.");
            }

            var source = RequiredString(rawFaction, "source", releaseId);
            var numericId = OptionalInt(rawFaction, "numeric_id", releaseId);
            if (numericId is < 0)
            {
                throw new CatalogBundleException($"Negative faction id in '{releaseId}'.");
            }

            factions.Add(new FactionDefinition(
                rawKey.GetString()!,
                OptionalString(rawFaction, "display_name", releaseId),
                source,
                releaseId,
                numericId));
        }

        var relations = new List<FactionRelation>();
        if (release.TryGetProperty("relation_addresses", out var rawRelations) &&
            rawRelations.ValueKind != JsonValueKind.Null)
        {
            if (rawRelations.ValueKind != JsonValueKind.Array)
            {
                throw new CatalogBundleException($"Invalid relation list for release '{releaseId}'.");
            }

            foreach (var rawRelation in rawRelations.EnumerateArray())
            {
                if (rawRelation.ValueKind != JsonValueKind.Object)
                {
                    throw new CatalogBundleException($"Invalid relation record for release '{releaseId}'.");
                }

                var source = RequiredString(rawRelation, "source", releaseId);
                var target = RequiredString(rawRelation, "target", releaseId);
                var value = RequiredInt(rawRelation, "value", releaseId);
                _ = OptionalInt(rawRelation, "row", releaseId);
                _ = OptionalInt(rawRelation, "column", releaseId);
                relations.Add(new FactionRelation(source, target, value));
            }
        }

        return new FactionCatalog(
            releaseId,
            factions,
            relations,
            OptionalInt(release, "goodwill_min", releaseId),
            OptionalInt(release, "goodwill_max", releaseId),
            OptionalInt(release, "attitude_neutral_threshold", releaseId),
            OptionalInt(release, "attitude_friend_threshold", releaseId));
    }

    private static UpgradeCatalog? ReadUpgrades(JsonElement release, string releaseId)
    {
        if (!release.TryGetProperty("upgrades", out var rawUpgrades) ||
            rawUpgrades.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (rawUpgrades.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogBundleException($"Invalid upgrade list for release '{releaseId}'.");
        }

        var upgrades = new List<UpgradeDefinition>();
        foreach (var rawUpgrade in rawUpgrades.EnumerateArray())
        {
            if (rawUpgrade.ValueKind != JsonValueKind.Object ||
                !rawUpgrade.TryGetProperty("key", out var rawKey) ||
                rawKey.ValueKind != JsonValueKind.String)
            {
                throw new CatalogBundleException($"Invalid upgrade record for release '{releaseId}'.");
            }

            var upgradeRelease = rawUpgrade.TryGetProperty("release_id", out var rawRelease)
                ? rawRelease.ValueKind == JsonValueKind.String ? rawRelease.GetString() : null
                : releaseId;
            if (!string.Equals(upgradeRelease, releaseId, StringComparison.Ordinal))
            {
                throw new CatalogBundleException($"Foreign upgrade release id in '{releaseId}'.");
            }

            var source = RequiredString(rawUpgrade, "source", releaseId);
            var applicable = OptionalStringList(rawUpgrade, "applicable_item_keys", releaseId);
            upgrades.Add(new UpgradeDefinition(
                rawKey.GetString()!,
                OptionalString(rawUpgrade, "display_name", releaseId),
                OptionalString(rawUpgrade, "category", releaseId),
                OptionalString(rawUpgrade, "item_key", releaseId),
                source,
                releaseId,
                OptionalString(rawUpgrade, "section", releaseId),
                OptionalString(rawUpgrade, "property", releaseId),
                OptionalString(rawUpgrade, "icon", releaseId),
                applicable));
        }

        return new UpgradeCatalog(releaseId, upgrades);
    }

    private static string RequiredString(JsonElement element, string property, string releaseId)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new CatalogBundleException($"Invalid {property} for release '{releaseId}'.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string property, string releaseId)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new CatalogBundleException($"Invalid {property} for release '{releaseId}'.");
        }

        return value.GetString();
    }

    private static int? OptionalInt(JsonElement element, string property, string releaseId)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new CatalogBundleException($"Invalid {property} for release '{releaseId}'.");
        }

        return result;
    }

    private static int RequiredInt(JsonElement element, string property, string releaseId) =>
        OptionalInt(element, property, releaseId)
        ?? throw new CatalogBundleException($"Missing {property} for release '{releaseId}'.");

    private static double? OptionalDouble(JsonElement element, string property, string releaseId)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result) || !double.IsFinite(result))
        {
            throw new CatalogBundleException($"Invalid {property} for release '{releaseId}'.");
        }

        return result;
    }

    private static IReadOnlyList<string> OptionalStringList(
        JsonElement element,
        string property,
        string releaseId)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogBundleException($"Invalid {property} for release '{releaseId}'.");
        }

        var items = new List<string>();
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                throw new CatalogBundleException($"Invalid {property} for release '{releaseId}'.");
            }

            items.Add(entry.GetString()!);
        }

        return Array.AsReadOnly(items.ToArray());
    }

    private static Dictionary<string, CatalogBundle> BorrowSeriesNames(Dictionary<string, CatalogBundle> loaded)
    {
        var trilogyIds = loaded.Keys.Where(IsOriginalTrilogy).ToArray();
        foreach (var releaseId in trilogyIds)
        {
            var bundle = loaded[releaseId];
            var donors = trilogyIds.Where(other => other != releaseId).Select(other => loaded[other]).ToArray();
            var items = bundle.Items.Items.Select(item =>
            {
                if (IsRussian(item.DisplayName) || item.Key.StartsWith("wpn_", StringComparison.OrdinalIgnoreCase) ||
                    item.Key.StartsWith("helm_", StringComparison.OrdinalIgnoreCase) ||
                    item.Key.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) ||
                    item.Key.EndsWith("_outfit", StringComparison.OrdinalIgnoreCase) ||
                    item.SerializationFamily?.StartsWith("weapon", StringComparison.Ordinal) == true ||
                    item.SerializationFamily?.StartsWith("outfit", StringComparison.Ordinal) == true)
                {
                    return item;
                }

                var donorName = donors.Select(donor => donor.Items.Resolve(item.Key)?.DisplayName)
                    .FirstOrDefault(IsRussian);
                return donorName is null ? item : item.WithDisplayName(donorName);
            }).ToArray();

            var factions = bundle.Factions;
            if (factions is not null)
            {
                var factionValues = factions.Factions.Select(faction =>
                {
                    if (faction.Key == "actor" || IsRussian(faction.DisplayName)) return faction;
                    var donorName = donors
                        .SelectMany(donor => donor.Factions?.Factions ?? Array.Empty<FactionDefinition>())
                        .FirstOrDefault(candidate => candidate.Key == faction.Key && IsRussian(candidate.DisplayName))
                        ?.DisplayName;
                    return donorName is null ? faction : faction with { DisplayName = donorName };
                }).ToArray();
                factions = factions.WithFactions(factionValues);
            }

            loaded[releaseId] = bundle.With(new ItemCatalog(releaseId, items), factions);
        }

        return loaded;
    }

    private static bool IsOriginalTrilogy(string releaseId) => releaseId is "stalker-soc" or "stalker-cs" or "stalker-cop";

    private static bool IsRussian(string? value) => value is not null && value.Any(character =>
        character is >= 'А' and <= 'Я' or >= 'а' and <= 'я' or 'Ё' or 'ё');
}
