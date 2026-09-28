using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace StalkerSaveEditor.Core.Catalogs;

/// <summary>Official name, description and icon of one S.T.A.L.K.E.R. 2 item SID.</summary>
public sealed record Stalker2ItemEntry(
    string Sid,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string> Descriptions,
    string? Icon,
    string? VariantOf);

/// <summary>
/// Knowledge base of S2 item SIDs (port of the Python oracle's <c>s2_items</c>): official Russian names
/// from the game's string table, English names and icon file names from the wikis (see the file's source).
/// </summary>
public sealed class Stalker2ItemCatalog
{
    private const string EmbeddedResourceName = "StalkerSaveEditor.Core.Catalogs.Data.s2_items.json";
    private static readonly Lazy<Stalker2ItemCatalog> Embedded = new(LoadEmbeddedCore, LazyThreadSafetyMode.ExecutionAndPublication);

    // Items without their own picture borrow one from the same family.
    private static readonly (string Marker, string Icon)[] FamilyIcons =
    [
        ("pda", "s2/KozimkovPDA.png"),
        ("blueprint_", "s2/Blueprint_Gvintar_Upgrade_1.png"),
    ];

    private readonly FrozenDictionary<string, Stalker2ItemEntry> _bySid;
    private readonly FrozenDictionary<string, string> _folded;

    private Stalker2ItemCatalog(IEnumerable<Stalker2ItemEntry> entries)
    {
        var bySid = new Dictionary<string, Stalker2ItemEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) bySid[entry.Sid] = entry;
        _bySid = bySid.ToFrozenDictionary(StringComparer.Ordinal);
        _folded = bySid.Keys.GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _bySid.Count;

    public static Stalker2ItemCatalog LoadEmbedded() => Embedded.Value;

    public static Stalker2ItemCatalog Load(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
        {
            throw new CatalogBundleException("S2 item catalog has no items object.");
        }

        var entries = new List<Stalker2ItemEntry>();
        foreach (var item in items.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object) continue;
            entries.Add(new Stalker2ItemEntry(
                item.Name,
                Strings(item.Value, "names"),
                Strings(item.Value, "descriptions"),
                OptionalString(item.Value, "icon"),
                OptionalString(item.Value, "variant_of")));
        }

        return new Stalker2ItemCatalog(entries);
    }

    /// <summary>The knowledge-base key for a save SID (<c>GuardGunX</c> → <c>GunX</c>, <c>X_Player</c> → <c>X</c>, case-insensitive).</summary>
    public string? CanonicalSid(string? sid)
    {
        var value = sid?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        var candidates = new List<string> { value };
        if (value.EndsWith("_Player", StringComparison.Ordinal)) candidates.Add(value[..^"_Player".Length]);
        if (value.StartsWith("GuardGun", StringComparison.Ordinal)) candidates.Add("Gun" + value["GuardGun".Length..]);
        foreach (var candidate in candidates)
        {
            if (_bySid.ContainsKey(candidate)) return candidate;
            if (_folded.TryGetValue(candidate, out var folded)) return folded;
        }

        return null;
    }

    public Stalker2ItemEntry? Resolve(string? sid) => CanonicalSid(sid) is { } key ? _bySid[key] : null;

    /// <summary>Official name in the UI language, else English (a non-Russian UI never gets Cyrillic).</summary>
    public string? Name(string? sid, string language = "ru")
    {
        var entry = Resolve(sid);
        if (entry is null) return null;
        return entry.Names.GetValueOrDefault(language) ?? entry.Names.GetValueOrDefault("en");
    }

    public string? Description(string? sid, string language = "ru")
    {
        var entry = Resolve(sid);
        if (entry is null) return null;
        return entry.Descriptions.GetValueOrDefault(language) ?? entry.Descriptions.GetValueOrDefault("en");
    }

    /// <summary>Icon file relative to the icon root (for example <c>s2/A012A.png</c>), or a family fallback.</summary>
    public string? Icon(string? sid)
    {
        var icon = Resolve(sid)?.Icon;
        if (icon is not null || string.IsNullOrEmpty(sid)) return icon;
        var folded = sid.ToLowerInvariant();
        return FamilyIcons.FirstOrDefault(family => folded.Contains(family.Marker, StringComparison.Ordinal)).Icon;
    }

    private static Stalker2ItemCatalog LoadEmbeddedCore()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new CatalogBundleException("Embedded S2 item catalog is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    private static FrozenDictionary<string, string> Strings(JsonElement element, string property)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { Length: > 0 } text) values[entry.Name] = text;
            }
        }

        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
