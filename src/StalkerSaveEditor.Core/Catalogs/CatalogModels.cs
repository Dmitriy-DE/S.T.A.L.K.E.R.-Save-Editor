using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Core.Catalogs;

public sealed class CatalogBundleException(string message, Exception? innerException = null)
    : FormatException(message, innerException);

public sealed class ItemDefinition
{
    internal ItemDefinition(
        string key,
        string? displayName,
        string? category,
        double? unitWeight,
        int? width,
        int? height,
        int? maxStack,
        IEnumerable<string> slots,
        string source,
        string? serializationFamily,
        int? iconX,
        int? iconY,
        string? iconTexture,
        string? className = null,
        string? displayNameKey = null,
        ReadOnlyMemory<byte>? prototype = null,
        int? cost = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Item key must not be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Item source must not be empty.", nameof(source));
        if (unitWeight is < 0) throw new ArgumentOutOfRangeException(nameof(unitWeight));
        ValidateNonNegative(width, nameof(width));
        ValidateNonNegative(height, nameof(height));
        ValidateNonNegative(maxStack, nameof(maxStack));
        ValidateNonNegative(cost, nameof(cost));
        ValidateNonNegative(iconX, nameof(iconX));
        ValidateNonNegative(iconY, nameof(iconY));

        Key = key;
        DisplayName = displayName;
        Category = category;
        UnitWeight = unitWeight;
        Width = width;
        Height = height;
        MaxStack = maxStack;
        Slots = Array.AsReadOnly(slots.ToArray());
        Source = source;
        SerializationFamily = EmptyToNull(serializationFamily?.ToLowerInvariant());
        IconX = iconX;
        IconY = iconY;
        IconTexture = EmptyToNull(iconTexture?.Replace('\\', '/').Trim());
        ClassName = EmptyToNull(className?.Trim());
        DisplayNameKey = EmptyToNull(displayNameKey?.Trim());
        Prototype = prototype;
        Cost = cost;
    }

    public string Key { get; }

    public string? DisplayName { get; }

    public string? Category { get; }

    public double? UnitWeight { get; }

    /// <summary>Game-defined inventory cost, read from this install's LTX or shipped catalog.</summary>
    public int? Cost { get; }

    public int? Width { get; }

    public int? Height { get; }

    public int? MaxStack { get; }

    public IReadOnlyList<string> Slots { get; }

    public ReadOnlyMemory<byte>? Prototype { get; }

    public string Source { get; }

    public string? ClassName { get; }

    public string? SerializationFamily { get; }

    public int? IconX { get; }

    public int? IconY { get; }

    public string? IconTexture { get; }

    public string? DisplayNameKey { get; }

    internal ItemDefinition WithDisplayName(string? displayName) => new(
        Key,
        displayName,
        Category,
        UnitWeight,
        Width,
        Height,
        MaxStack,
        Slots,
        Source,
        SerializationFamily,
        IconX,
        IconY,
        IconTexture,
        ClassName,
        DisplayNameKey,
        Prototype,
        Cost);

    private static void ValidateNonNegative(int? value, string field)
    {
        if (value is < 0) throw new ArgumentOutOfRangeException(field);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

public sealed class ItemCatalog
{
    private readonly IReadOnlyDictionary<string, ItemDefinition> _byKey;

    internal ItemCatalog(string releaseId, IEnumerable<ItemDefinition> items)
    {
        if (string.IsNullOrWhiteSpace(releaseId)) throw new ArgumentException("Release id must not be empty.", nameof(releaseId));
        var values = items.ToArray();
        var byKey = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            if (!byKey.TryAdd(item.Key, item))
            {
                throw new CatalogBundleException($"Duplicate item key '{item.Key}' in catalog '{releaseId}'.");
            }
        }

        ReleaseId = releaseId;
        Items = Array.AsReadOnly(values);
        _byKey = new ReadOnlyDictionary<string, ItemDefinition>(byKey);
    }

    public string ReleaseId { get; }

    public IReadOnlyList<ItemDefinition> Items { get; }

    public ItemDefinition? Resolve(string key) => _byKey.GetValueOrDefault(key);

    public ItemDefinition? ResolveDisplayName(string displayName)
    {
        var normalized = displayName.Trim();
        if (normalized.Length == 0) return null;
        ItemDefinition? match = null;
        foreach (var item in Items)
        {
            if (!string.Equals(item.DisplayName?.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null) return null;
            match = item;
        }

        return match;
    }

    public ItemDefinition? ResolveKeyOrDisplayName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        var exact = Resolve(normalized);
        if (exact is not null) return exact;

        var nameKeyMatches = Items.Where(item => string.Equals(
            item.DisplayNameKey,
            normalized,
            StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (nameKeyMatches.Length == 1) return nameKeyMatches[0];
        return ResolveDisplayName(normalized);
    }

    internal ItemCatalog WithItems(IEnumerable<ItemDefinition> items) => new(ReleaseId, items);
}

public sealed record FactionDefinition
{
    public FactionDefinition(string key, string? displayName, string source, string releaseId, int? numericId)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Faction key must not be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Faction source must not be empty.", nameof(source));
        if (string.IsNullOrWhiteSpace(releaseId)) throw new ArgumentException("Release id must not be empty.", nameof(releaseId));
        if (numericId is < 0) throw new ArgumentOutOfRangeException(nameof(numericId));
        Key = key;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        Source = source;
        ReleaseId = releaseId;
        NumericId = numericId;
    }

    public string Key { get; init; }

    public string? DisplayName { get; init; }

    public string Source { get; init; }

    public string ReleaseId { get; init; }

    public int? NumericId { get; init; }
}

public sealed record FactionRelation(string Source, string Target, int Value);

public sealed record FactionRelationAddress(
    string Source,
    string Target,
    int Row,
    int Column,
    int Value);

public sealed class FactionCatalog
{
    private readonly ReadOnlyDictionary<string, FactionDefinition> _byKey;

    internal FactionCatalog(
        string releaseId,
        IEnumerable<FactionDefinition> factions,
        IEnumerable<FactionRelation> relations,
        int? goodwillMin,
        int? goodwillMax,
        int? attitudeNeutralThreshold,
        int? attitudeFriendThreshold)
    {
        if (string.IsNullOrWhiteSpace(releaseId)) throw new ArgumentException("Release id must not be empty.", nameof(releaseId));
        var factionValues = factions.ToArray();
        var byKey = new Dictionary<string, FactionDefinition>(StringComparer.Ordinal);
        var numericIds = new HashSet<int>();
        foreach (var faction in factionValues)
        {
            if (!string.Equals(faction.ReleaseId, releaseId, StringComparison.Ordinal))
            {
                throw new CatalogBundleException($"Faction release id does not match catalog '{releaseId}'.");
            }

            if (!byKey.TryAdd(faction.Key, faction))
            {
                throw new CatalogBundleException($"Duplicate faction key '{faction.Key}' in catalog '{releaseId}'.");
            }

            if (faction.NumericId is int numericId && !numericIds.Add(numericId))
            {
                throw new CatalogBundleException($"Duplicate faction numeric id {numericId} in catalog '{releaseId}'.");
            }
        }

        var relationValues = relations.ToArray();
        var relationKeys = new HashSet<(string Source, string Target)>();
        foreach (var relation in relationValues)
        {
            if (!byKey.ContainsKey(relation.Source) || !byKey.ContainsKey(relation.Target))
            {
                throw new CatalogBundleException("Faction relation references an unknown community.");
            }

            if (!relationKeys.Add((relation.Source, relation.Target)))
            {
                throw new CatalogBundleException("Duplicate faction relation in catalog.");
            }
        }

        if (goodwillMin is not null && goodwillMax is not null && goodwillMin > goodwillMax)
        {
            throw new CatalogBundleException("goodwill_min must not exceed goodwill_max.");
        }

        ReleaseId = releaseId;
        Factions = Array.AsReadOnly(factionValues);
        Relations = Array.AsReadOnly(relationValues);
        GoodwillMin = goodwillMin;
        GoodwillMax = goodwillMax;
        AttitudeNeutralThreshold = attitudeNeutralThreshold;
        AttitudeFriendThreshold = attitudeFriendThreshold;
        _byKey = new ReadOnlyDictionary<string, FactionDefinition>(byKey);
    }

    public string ReleaseId { get; }

    public IReadOnlyList<FactionDefinition> Factions { get; }

    public IReadOnlyList<FactionRelation> Relations { get; }

    public int? GoodwillMin { get; }

    public int? GoodwillMax { get; }

    public int? AttitudeNeutralThreshold { get; }

    public int? AttitudeFriendThreshold { get; }

    public FactionDefinition Resolve(string key) =>
        _byKey.TryGetValue(key, out var faction)
            ? faction
            : throw new CatalogLookupException($"Faction key '{key}' is absent from catalog '{ReleaseId}'.");

    public FactionDefinition? ResolveNumeric(int numericId) =>
        Factions.FirstOrDefault(faction => faction.NumericId == numericId);

    public (int Row, int Column) RelationAddress(string source, string target)
    {
        var row = Resolve(source).NumericId;
        var column = Resolve(target).NumericId;
        if (row is null || column is null)
        {
            throw new CatalogLookupException($"Catalog '{ReleaseId}' has no numeric relation address.");
        }

        return (row.Value, column.Value);
    }

    public int? DefaultRelation(string source, string target)
    {
        _ = Resolve(source);
        _ = Resolve(target);
        return Relations.FirstOrDefault(relation => relation.Source == source && relation.Target == target)?.Value;
    }

    public IReadOnlyList<FactionRelationAddress> RelationAddresses => Array.AsReadOnly(
        Relations.Select(relation =>
        {
            var (row, column) = RelationAddress(relation.Source, relation.Target);
            return new FactionRelationAddress(relation.Source, relation.Target, row, column, relation.Value);
        }).ToArray());

    internal FactionCatalog WithFactions(IEnumerable<FactionDefinition> factions) => new(
        ReleaseId,
        factions,
        Relations,
        GoodwillMin,
        GoodwillMax,
        AttitudeNeutralThreshold,
        AttitudeFriendThreshold);
}

public sealed class UpgradeDefinition
{
    internal UpgradeDefinition(
        string key,
        string? displayName,
        string? category,
        string? itemKey,
        string source,
        string releaseId,
        string? section,
        string? propertyName,
        string? icon,
        IEnumerable<string> applicableItemKeys)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Upgrade key must not be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Upgrade source must not be empty.", nameof(source));
        if (string.IsNullOrWhiteSpace(releaseId)) throw new ArgumentException("Release id must not be empty.", nameof(releaseId));
        Key = key;
        DisplayName = EmptyToNull(displayName?.Trim());
        Category = EmptyToNull(category?.Trim());
        ItemKey = EmptyToNull(itemKey?.Trim());
        Source = source;
        ReleaseId = releaseId;
        Section = EmptyToNull(section?.Trim());
        PropertyName = EmptyToNull(propertyName?.Trim());
        Icon = EmptyToNull(icon?.Trim());

        var keys = applicableItemKeys.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (ItemKey is not null && !keys.Contains(ItemKey, StringComparer.Ordinal)) keys.Insert(0, ItemKey);
        ApplicableItemKeys = Array.AsReadOnly(keys.ToArray());
    }

    public string Key { get; }

    public string? DisplayName { get; }

    public string? Category { get; }

    public string? ItemKey { get; }

    public string Source { get; }

    public string ReleaseId { get; }

    public string? Section { get; }

    public string? PropertyName { get; }

    public string? Icon { get; }

    public IReadOnlyList<string> ApplicableItemKeys { get; }

    public bool AppliesTo(string itemKey) => ApplicableItemKeys.Contains(itemKey, StringComparer.Ordinal);

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

public sealed class UpgradeCatalog
{
    private readonly IReadOnlyDictionary<string, UpgradeDefinition> _byKey;

    internal UpgradeCatalog(string releaseId, IEnumerable<UpgradeDefinition> upgrades)
    {
        if (string.IsNullOrWhiteSpace(releaseId)) throw new ArgumentException("Release id must not be empty.", nameof(releaseId));
        var values = upgrades.ToArray();
        var byKey = new Dictionary<string, UpgradeDefinition>(StringComparer.Ordinal);
        foreach (var upgrade in values)
        {
            if (!string.Equals(upgrade.ReleaseId, releaseId, StringComparison.Ordinal))
            {
                throw new CatalogBundleException($"Upgrade release id does not match catalog '{releaseId}'.");
            }

            if (!byKey.TryAdd(upgrade.Key, upgrade))
            {
                throw new CatalogBundleException($"Duplicate upgrade key '{upgrade.Key}' in catalog '{releaseId}'.");
            }
        }

        ReleaseId = releaseId;
        Upgrades = Array.AsReadOnly(values);
        _byKey = new ReadOnlyDictionary<string, UpgradeDefinition>(byKey);
    }

    public string ReleaseId { get; }

    public IReadOnlyList<UpgradeDefinition> Upgrades { get; }

    public UpgradeDefinition? Resolve(string key) => _byKey.GetValueOrDefault(key);

    public IReadOnlyList<UpgradeDefinition> ForItem(string itemKey) => Array.AsReadOnly(
        Upgrades.Where(upgrade => upgrade.AppliesTo(itemKey)).ToArray());
}

public sealed class GameCatalog
{
    internal GameCatalog(string releaseId, ItemCatalog items, FactionCatalog factions, UpgradeCatalog? upgrades)
    {
        if (items.ReleaseId != releaseId || factions.ReleaseId != releaseId ||
            (upgrades is not null && upgrades.ReleaseId != releaseId))
        {
            throw new CatalogBundleException("Game catalog release IDs do not match.");
        }

        ReleaseId = releaseId;
        Items = items;
        Factions = factions;
        Upgrades = upgrades;
    }

    public string ReleaseId { get; }

    public ItemCatalog Items { get; }

    public FactionCatalog Factions { get; }

    public UpgradeCatalog? Upgrades { get; }
}

public sealed class CatalogBundle
{
    internal CatalogBundle(string releaseId, ItemCatalog items, FactionCatalog? factions, UpgradeCatalog? upgrades)
    {
        if (items.ReleaseId != releaseId || (factions is not null && factions.ReleaseId != releaseId) ||
            (upgrades is not null && upgrades.ReleaseId != releaseId))
        {
            throw new CatalogBundleException("Catalog bundle release IDs do not match.");
        }

        ReleaseId = releaseId;
        Items = items;
        Factions = factions;
        Upgrades = upgrades;
    }

    public string ReleaseId { get; }

    public ItemCatalog Items { get; }

    public FactionCatalog? Factions { get; }

    public UpgradeCatalog? Upgrades { get; }

    public GameCatalog? GameCatalog => Factions is null ? null : new GameCatalog(ReleaseId, Items, Factions, Upgrades);

    internal CatalogBundle With(
        ItemCatalog? items = null,
        FactionCatalog? factions = null) => new(
        ReleaseId,
        items ?? Items,
        factions ?? Factions,
        Upgrades);
}

public sealed class CatalogLookupException(string message) : KeyNotFoundException(message);
