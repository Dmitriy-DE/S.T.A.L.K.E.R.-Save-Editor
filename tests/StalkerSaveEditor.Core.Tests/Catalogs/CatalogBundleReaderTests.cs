using System.Text.Json;
using StalkerSaveEditor.Core.Catalogs;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Catalogs;

public sealed class CatalogBundleReaderTests
{
    private const string CatalogsResource = "StalkerSaveEditor.Core.Catalogs.Data.catalogs.json";

    [Fact]
    public void Embedded_bundle_loads_all_python_generated_release_metadata()
    {
        using var source = ReadEmbeddedJson(CatalogsResource);
        var embedded = CatalogBundleReader.LoadEmbedded();
        var rawReleases = source.RootElement.GetProperty("releases");

        Assert.Equal(rawReleases.EnumerateObject().Count(), embedded.Count);
        foreach (var rawRelease in rawReleases.EnumerateObject())
        {
            var releasePayload = OneReleasePayload(rawRelease.Name, rawRelease.Value);
            var parsed = Assert.Single(CatalogBundleReader.Load(releasePayload));
            Assert.Equal(rawRelease.Name, parsed.Key);
            AssertReleaseMatchesPythonJson(rawRelease.Value, parsed.Value);
        }

        Assert.Equal(434, embedded["stalker-cop"].Items.Items.Count);
        Assert.Equal(417, embedded["stalker-cs"].Items.Items.Count);
        Assert.Equal(389, embedded["stalker-soc"].Items.Items.Count);
        Assert.Equal(".45Гидро", embedded["stalker-cop"].Items.Resolve("ammo_11.43x23_hydro")?.DisplayName);
    }

    [Fact]
    public void Catalog_lookup_is_exact_and_ambiguous_display_names_stay_unresolved()
    {
        var parsed = Assert.Single(CatalogBundleReader.Load(SyntheticBundleWithAmbiguousNames())).Value;

        Assert.NotNull(parsed.Items.Resolve("scope_exact"));
        Assert.Null(parsed.Items.Resolve("Scope_Exact"));
        Assert.Null(parsed.Items.ResolveDisplayName("Shared label"));
        Assert.Equal("scope_exact", parsed.Items.ResolveKeyOrDisplayName("  scope_exact  ")?.Key);
        Assert.Null(parsed.Items.ResolveKeyOrDisplayName("unknown key"));
        Assert.Null(parsed.Factions?.ResolveNumeric(500));
        Assert.Equal((0, 1), parsed.Factions?.RelationAddress("actor", "stalker"));
        Assert.Equal(25, parsed.Factions?.DefaultRelation("actor", "stalker"));
        Assert.Equal("upgrade_exact", Assert.Single(parsed.Upgrades!.ForItem("scope_exact")).Key);
        Assert.Null(parsed.Items.Resolve("stalker2_only_item"));
    }

    [Theory]
    [InlineData("{\"schema_version\":2,\"releases\":{}}")]
    [InlineData("{\"schema_version\":1,\"releases\":{\"unknown-release\":{\"items\":[]}}}")]
    [InlineData("{\"schema_version\":1,\"releases\":{\"stalker-cop\":{\"items\":[{\"key\":\"x\"},{\"key\":\"x\"}]}}}")]
    public void Rejects_unsupported_or_malformed_bundle_payloads(string json)
    {
        Assert.Throws<CatalogBundleException>(() => CatalogBundleReader.Load(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Rejects_foreign_faction_and_upgrade_release_ids()
    {
        var foreignFaction = """
            {"schema_version":1,"releases":{"stalker-cop":{"items":[],"factions":[{"key":"actor","source":"fixture","release_id":"stalker-cs"}]}}}
            """;
        var foreignUpgrade = """
            {"schema_version":1,"releases":{"stalker-cop":{"items":[],"upgrades":[{"key":"up","source":"fixture","release_id":"stalker-cs"}]}}}
            """;

        Assert.Throws<CatalogBundleException>(() => CatalogBundleReader.Load(System.Text.Encoding.UTF8.GetBytes(foreignFaction)));
        Assert.Throws<CatalogBundleException>(() => CatalogBundleReader.Load(System.Text.Encoding.UTF8.GetBytes(foreignUpgrade)));
    }

    private static void AssertReleaseMatchesPythonJson(JsonElement rawRelease, CatalogBundle bundle)
    {
        var rawItems = rawRelease.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(rawItems.Length, bundle.Items.Items.Count);
        for (var index = 0; index < rawItems.Length; index++)
        {
            var raw = rawItems[index];
            var actual = bundle.Items.Items[index];
            Assert.Equal(raw.GetProperty("key").GetString(), actual.Key);
            Assert.Equal(OptionalString(raw, "display_name"), actual.DisplayName);
            Assert.Equal(OptionalString(raw, "category"), actual.Category);
            Assert.Equal(OptionalDouble(raw, "unit_weight"), actual.UnitWeight);
            Assert.Null(actual.Width);
            Assert.Null(actual.Height);
            Assert.Equal(OptionalInt(raw, "max_stack"), actual.MaxStack);
            Assert.Equal(StringList(raw, "slots"), actual.Slots);
            Assert.Null(actual.Prototype);
            Assert.Equal("generated-official-metadata", actual.Source);
            Assert.Equal(OptionalString(raw, "serialization_family")?.ToLowerInvariant(), actual.SerializationFamily);
            Assert.Equal(OptionalInt(raw, "icon_x"), actual.IconX);
            Assert.Equal(OptionalInt(raw, "icon_y"), actual.IconY);
            Assert.Equal(OptionalString(raw, "icon_texture")?.Replace('\\', '/'), actual.IconTexture);
        }

        Assert.NotNull(bundle.Factions);
        var rawFactions = rawRelease.GetProperty("factions").EnumerateArray().ToArray();
        Assert.Equal(rawFactions.Length, bundle.Factions!.Factions.Count);
        for (var index = 0; index < rawFactions.Length; index++)
        {
            var raw = rawFactions[index];
            var actual = bundle.Factions.Factions[index];
            Assert.Equal(raw.GetProperty("key").GetString(), actual.Key);
            Assert.Equal(OptionalString(raw, "display_name")?.Trim(), actual.DisplayName);
            Assert.Equal(raw.GetProperty("source").GetString(), actual.Source);
            Assert.Equal(bundle.ReleaseId, actual.ReleaseId);
            Assert.Equal(OptionalInt(raw, "numeric_id"), actual.NumericId);
        }

        var rawRelations = rawRelease.GetProperty("relation_addresses").EnumerateArray().ToArray();
        Assert.Equal(rawRelations.Length, bundle.Factions.RelationAddresses.Count);
        for (var index = 0; index < rawRelations.Length; index++)
        {
            var raw = rawRelations[index];
            var actual = bundle.Factions.RelationAddresses[index];
            Assert.Equal(raw.GetProperty("source").GetString(), actual.Source);
            Assert.Equal(raw.GetProperty("target").GetString(), actual.Target);
            Assert.Equal(raw.GetProperty("row").GetInt32(), actual.Row);
            Assert.Equal(raw.GetProperty("column").GetInt32(), actual.Column);
            Assert.Equal(raw.GetProperty("value").GetInt32(), actual.Value);
        }

        Assert.Equal(OptionalInt(rawRelease, "goodwill_min"), bundle.Factions.GoodwillMin);
        Assert.Equal(OptionalInt(rawRelease, "goodwill_max"), bundle.Factions.GoodwillMax);
        Assert.Equal(OptionalInt(rawRelease, "attitude_neutral_threshold"), bundle.Factions.AttitudeNeutralThreshold);
        Assert.Equal(OptionalInt(rawRelease, "attitude_friend_threshold"), bundle.Factions.AttitudeFriendThreshold);

        Assert.NotNull(bundle.Upgrades);
        var rawUpgrades = rawRelease.GetProperty("upgrades").EnumerateArray().ToArray();
        Assert.Equal(rawUpgrades.Length, bundle.Upgrades!.Upgrades.Count);
        for (var index = 0; index < rawUpgrades.Length; index++)
        {
            var raw = rawUpgrades[index];
            var actual = bundle.Upgrades.Upgrades[index];
            Assert.Equal(raw.GetProperty("key").GetString(), actual.Key);
            Assert.Equal(OptionalString(raw, "display_name")?.Trim(), actual.DisplayName);
            Assert.Equal(OptionalString(raw, "category")?.Trim(), actual.Category);
            Assert.Equal(OptionalString(raw, "item_key")?.Trim(), actual.ItemKey);
            Assert.Equal(raw.GetProperty("source").GetString(), actual.Source);
            Assert.Equal(bundle.ReleaseId, actual.ReleaseId);
            Assert.Equal(OptionalString(raw, "section")?.Trim(), actual.Section);
            Assert.Equal(OptionalString(raw, "property")?.Trim(), actual.PropertyName);
            Assert.Equal(OptionalString(raw, "icon")?.Trim(), actual.Icon);
            Assert.Equal(NormalizedApplicableItems(raw), actual.ApplicableItemKeys);
        }
    }

    private static byte[] OneReleasePayload(string releaseId, JsonElement release) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            releases = new Dictionary<string, JsonElement> { [releaseId] = release.Clone() },
        });

    private static byte[] SyntheticBundleWithAmbiguousNames() => """
        {
          "schema_version":1,
          "releases":{
            "stalker-cop":{
              "items":[
                {"key":"scope_exact","display_name":"Shared label","category":"weapon","unit_weight":1.25,"slots":null,"max_stack":null,"serialization_family":"WEAPON","icon_x":1,"icon_y":2,"icon_texture":"ui\\scope"},
                {"key":"item_second","display_name":"Shared label","category":null,"slots":[],"max_stack":3}
              ],
              "factions":[
                {"key":"actor","display_name":"Actor","numeric_id":0,"source":"fixture#communities"},
                {"key":"stalker","display_name":"Stalker","numeric_id":1,"source":"fixture#communities"}
              ],
              "relation_addresses":[{"source":"actor","target":"stalker","row":0,"column":1,"value":25}],
              "upgrades":[{"key":"upgrade_exact","display_name":"Scope upgrade","item_key":"scope_exact","applicable_item_keys":[],"source":"fixture#upgrade"}]
            }
          }
        }
        """u8.ToArray();

    private static List<string> NormalizedApplicableItems(JsonElement upgrade)
    {
        var values = new List<string>();
        if (upgrade.TryGetProperty("applicable_item_keys", out var rawValues) && rawValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in rawValues.EnumerateArray())
            {
                var normalized = value.GetString()?.Trim();
                if (!string.IsNullOrEmpty(normalized) && !values.Contains(normalized, StringComparer.Ordinal))
                {
                    values.Add(normalized);
                }
            }
        }

        var itemKey = OptionalString(upgrade, "item_key")?.Trim();
        if (!string.IsNullOrEmpty(itemKey) && !values.Contains(itemKey, StringComparer.Ordinal)) values.Insert(0, itemKey);
        return values;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static double? OptionalDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
            ? result
            : null;

    private static string[] StringList(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [];

    private static JsonDocument ReadEmbeddedJson(string resourceName)
    {
        using var stream = typeof(CatalogBundleReader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing embedded resource {resourceName}.");
        return JsonDocument.Parse(stream);
    }
}
