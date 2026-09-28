using System.Text.Json;

namespace StalkerSaveEditor.Core.Catalogs;

/// <summary>Writes catalog bundles in the same schema <see cref="CatalogBundleReader"/> reads.</summary>
internal static class CatalogBundleWriter
{
    public static byte[] Write(IEnumerable<CatalogBundle> bundles)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("schema_version", 1);
            json.WriteStartObject("releases");
            foreach (var bundle in bundles)
            {
                json.WriteStartObject(bundle.ReleaseId);
                WriteItems(json, bundle.Items);
                WriteFactions(json, bundle.ReleaseId, bundle.Factions);
                WriteUpgrades(json, bundle.Upgrades);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteItems(Utf8JsonWriter json, ItemCatalog items)
    {
        json.WriteStartArray("items");
        foreach (var item in items.Items)
        {
            json.WriteStartObject();
            json.WriteString("key", item.Key);
            OptionalString(json, "display_name", item.DisplayName);
            OptionalString(json, "category", item.Category);
            if (item.UnitWeight is { } weight) json.WriteNumber("unit_weight", weight);
            OptionalInt(json, "width", item.Width);
            OptionalInt(json, "height", item.Height);
            OptionalInt(json, "max_stack", item.MaxStack);
            if (item.Slots.Count > 0)
            {
                json.WriteStartArray("slots");
                foreach (var slot in item.Slots) json.WriteStringValue(slot);
                json.WriteEndArray();
            }

            json.WriteString("source", item.Source);
            OptionalString(json, "serialization_family", item.SerializationFamily);
            OptionalInt(json, "icon_x", item.IconX);
            OptionalInt(json, "icon_y", item.IconY);
            OptionalString(json, "icon_texture", item.IconTexture);
            OptionalString(json, "class_name", item.ClassName);
            OptionalString(json, "display_name_key", item.DisplayNameKey);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteFactions(Utf8JsonWriter json, string releaseId, FactionCatalog? factions)
    {
        if (factions is null)
        {
            json.WriteNull("factions");
            return;
        }

        json.WriteStartArray("factions");
        {
            foreach (var faction in factions.Factions)
            {
                json.WriteStartObject();
                json.WriteString("key", faction.Key);
                OptionalString(json, "display_name", faction.DisplayName);
                json.WriteString("release_id", releaseId);
                json.WriteString("source", faction.Source);
                OptionalInt(json, "numeric_id", faction.NumericId);
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
        json.WriteStartArray("relation_addresses");
        foreach (var relation in factions.Relations)
        {
            json.WriteStartObject();
            json.WriteString("source", relation.Source);
            json.WriteString("target", relation.Target);
            json.WriteNumber("value", relation.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        OptionalInt(json, "goodwill_min", factions.GoodwillMin);
        OptionalInt(json, "goodwill_max", factions.GoodwillMax);
        OptionalInt(json, "attitude_neutral_threshold", factions.AttitudeNeutralThreshold);
        OptionalInt(json, "attitude_friend_threshold", factions.AttitudeFriendThreshold);
    }

    private static void WriteUpgrades(Utf8JsonWriter json, UpgradeCatalog? upgrades)
    {
        json.WriteStartArray("upgrades");
        foreach (var upgrade in upgrades?.Upgrades ?? [])
        {
            json.WriteStartObject();
            json.WriteString("key", upgrade.Key);
            OptionalString(json, "display_name", upgrade.DisplayName);
            OptionalString(json, "category", upgrade.Category);
            OptionalString(json, "item_key", upgrade.ItemKey);
            json.WriteString("source", upgrade.Source);
            json.WriteString("release_id", upgrade.ReleaseId);
            OptionalString(json, "section", upgrade.Section);
            OptionalString(json, "property", upgrade.PropertyName);
            OptionalString(json, "icon", upgrade.Icon);
            json.WriteStartArray("applicable_item_keys");
            foreach (var key in upgrade.ApplicableItemKeys) json.WriteStringValue(key);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void OptionalString(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null) json.WriteString(name, value);
    }

    private static void OptionalInt(Utf8JsonWriter json, string name, int? value)
    {
        if (value is { } number) json.WriteNumber(name, number);
    }
}
