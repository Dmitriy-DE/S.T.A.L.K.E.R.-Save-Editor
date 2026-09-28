using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Core.Inspection;

/// <summary>Total count of one item type in a save and its display name.</summary>
public sealed record SaveItemTotal(string Key, string Name, int Count);

/// <summary>Format-independent facts about a save: enough for a library preview and for comparison.</summary>
public sealed record SaveOverview(
    string FormatId,
    string ReleaseId,
    long? Money,
    float? Health,
    int? Rank,
    int? Reputation,
    int ItemCount,
    float? AverageCondition,
    IReadOnlyDictionary<string, SaveItemTotal> ItemTotals);

/// <summary>Reads any supported save into a <see cref="SaveOverview"/> (port of the Python oracle's inspection summary).</summary>
public static class SaveInspector
{
    public static SaveOverview Inspect(ReadOnlySpan<byte> data, CatalogBundle? catalog = null)
    {
        var format = EditService.DetectFormat(data) ?? throw new InvalidDataException("Unsupported or damaged save file.");
        if (format == "stalker2")
        {
            var save = Stalker2SaveReader.FromBytes(data);
            return Build(
                save.FormatId,
                save.ReleaseId,
                save.Money,
                null,
                null,
                null,
                // S2 type codes are per-save serialization ids, not item identities; the SID/name is stable.
                save.Inventory.Select(item => (item.DisplayName ?? item.TypeKey, item.DisplayName ?? item.TypeKey, (int)Math.Max(1, item.Count), item.Condition)));
        }

        var xray = TryReadXRay(data) ?? throw new InvalidDataException("Unsupported or damaged save file.");
        var releaseId = ReleaseOf(xray.FormatId);
        catalog ??= CatalogBundleReader.LoadEmbedded().GetValueOrDefault(releaseId.Replace("-ee", string.Empty, StringComparison.Ordinal));
        return Build(
            xray.FormatId,
            releaseId,
            xray.Money,
            xray.ActorHealth,
            xray.ActorRank,
            xray.ActorReputation,
            xray.Inventory.Select(item => (
                item.TypeKey,
                catalog?.Items.Resolve(item.TypeKey)?.DisplayName ?? item.TypeKey,
                Math.Max(1, (int)(item.Count ?? 1)),
                item.Condition)));
    }

    private static XRayTrilogySave? TryReadXRay(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException)
        {
        }

        try
        {
            return XRayEnhancedReader.FromBytes(data);
        }
        catch (XRayFormatException)
        {
            return null;
        }
    }

    /// <summary>Maps an X-Ray format id to its release id (formats are named after releases).</summary>
    private static string ReleaseOf(string formatId) => formatId;

    private static SaveOverview Build(
        string formatId,
        string releaseId,
        long? money,
        float? health,
        int? rank,
        int? reputation,
        IEnumerable<(string Key, string Name, int Count, float? Condition)> items)
    {
        var totals = new Dictionary<string, SaveItemTotal>(StringComparer.Ordinal);
        var conditions = new List<float>();
        var itemCount = 0;
        foreach (var (key, name, count, condition) in items)
        {
            itemCount++;
            if (condition is { } value) conditions.Add(value);
            totals[key] = totals.TryGetValue(key, out var existing)
                ? existing with { Count = existing.Count + count }
                : new SaveItemTotal(key, name, count);
        }

        return new SaveOverview(
            formatId,
            releaseId,
            money,
            health,
            rank,
            reputation,
            itemCount,
            conditions.Count > 0 ? conditions.Average() : null,
            totals);
    }
}

/// <summary>One difference between two saves: kind is money, health, rank, reputation or item.</summary>
public sealed record SaveDifference(string Kind, string Label, string? Before, string? After);

/// <summary>Compares two saves the way the Python editor did: items by type key and total count, because handles are reassigned.</summary>
public static class SaveComparer
{
    public static IReadOnlyList<SaveDifference> Compare(SaveOverview before, SaveOverview after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var rows = new List<SaveDifference>();
        if (before.Money != after.Money)
        {
            rows.Add(new SaveDifference("money", "money", Text(before.Money), Text(after.Money)));
        }

        if (before.Health != after.Health)
        {
            rows.Add(new SaveDifference("health", "health", Percent(before.Health), Percent(after.Health)));
        }

        if (before.Rank != after.Rank)
        {
            rows.Add(new SaveDifference("rank", "rank", Text(before.Rank), Text(after.Rank)));
        }

        if (before.Reputation != after.Reputation)
        {
            rows.Add(new SaveDifference("reputation", "reputation", Text(before.Reputation), Text(after.Reputation)));
        }

        var keys = before.ItemTotals.Keys.Union(after.ItemTotals.Keys, StringComparer.Ordinal)
            .OrderBy(key => (before.ItemTotals.GetValueOrDefault(key) ?? after.ItemTotals[key]).Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var key in keys)
        {
            var old = before.ItemTotals.GetValueOrDefault(key);
            var now = after.ItemTotals.GetValueOrDefault(key);
            if ((old?.Count ?? 0) == (now?.Count ?? 0)) continue;
            rows.Add(new SaveDifference("item", (old ?? now)!.Name, old is null ? null : Text(old.Count), now is null ? null : Text(now.Count)));
        }

        return rows.AsReadOnly();
    }

    private static string? Text<T>(T? value) where T : struct => value?.ToString() is { } text ? text : null;

    private static string? Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Percent(float? value) =>
        value is { } number ? FormattableString.Invariant($"{Math.Round(number * 100)}%") : null;
}
