namespace StalkerSaveEditor.Core.Formats.XRay;

public sealed class XRayTrilogySave
{
    internal XRayTrilogySave(
        string formatId,
        uint containerVersion,
        XRayContainer container,
        int actorVersion,
        ushort actorId,
        uint money,
        int moneyOffset,
        int? playerFactionIndex,
        int? playerFactionOffset,
        XRayRelationRegistry? relationRegistry,
        IReadOnlyList<XRayFactionRelation> factionRelations,
        float? actorHealth,
        int? actorRank,
        int? actorReputation,
        string? actorName,
        IReadOnlyList<XRayRegistryObject> registryObjects,
        IReadOnlyList<XRayLevelChanger> levelChangers,
        ulong gameTime,
        float timeFactor,
        float normalTimeFactor,
        IReadOnlyList<XRayInventoryItem> inventory,
        IReadOnlyList<XRayStash> stashes)
    {
        FormatId = formatId;
        ContainerVersion = containerVersion;
        Container = container;
        ActorVersion = actorVersion;
        ActorId = actorId;
        Money = money;
        MoneyOffset = moneyOffset;
        PlayerFactionIndex = playerFactionIndex;
        PlayerFactionOffset = playerFactionOffset;
        RelationRegistry = relationRegistry;
        FactionRelations = factionRelations;
        FactionRelationsEditable = relationRegistry is not null;
        ActorHealth = actorHealth;
        ActorRank = actorRank;
        ActorReputation = actorReputation;
        ActorName = actorName;
        RegistryObjects = registryObjects;
        LevelChangers = levelChangers;
        GameTime = gameTime;
        TimeFactor = timeFactor;
        NormalTimeFactor = normalTimeFactor;
        Inventory = inventory;
        Stashes = stashes;
    }

    public string FormatId { get; }

    public uint ContainerVersion { get; }

    internal XRayContainer Container { get; }

    public int ActorVersion { get; }

    public ushort ActorId { get; }

    public uint Money { get; }

    internal int MoneyOffset { get; }

    public int? PlayerFactionIndex { get; }

    internal int? PlayerFactionOffset { get; }

    internal XRayRelationRegistry? RelationRegistry { get; }

    public IReadOnlyList<XRayFactionRelation> FactionRelations { get; }

    public bool FactionRelationsEditable { get; }

    /// <summary>Info portions the actor knows (story and quest flags such as <c>esc_wolf_dead</c>); empty when the registry is not read.</summary>
    public IReadOnlyList<string> ActorKnownInfo => RelationRegistry?.InfoFor(ActorId)?.Names ?? [];

    public float? ActorHealth { get; }

    public int? ActorRank { get; }

    public int? ActorReputation { get; }

    public string? ActorName { get; }

    internal IReadOnlyList<XRayRegistryObject> RegistryObjects { get; }

    /// <summary>
    /// Level-changer identities discovered in the X-Ray object registry.
    /// This collection contains only registry fields; destination state offsets are not inferred.
    /// </summary>
    public IReadOnlyList<XRayLevelChanger> LevelChangers { get; }

    public ulong GameTime { get; }

    public float TimeFactor { get; }

    public float NormalTimeFactor { get; }

    public IReadOnlyList<XRayInventoryItem> Inventory { get; }

    public IReadOnlyList<XRayStash> Stashes { get; }

    /// <summary>Health and death markers of every registry object whose section name is <paramref name="section"/> and whose STATE parses as a creature.</summary>
    public IReadOnlyList<XRayCreatureVitals> FindCreatureVitals(string section)
    {
        var raw = Container.Raw.Span;
        var result = new List<XRayCreatureVitals>();
        foreach (var record in RegistryObjects)
        {
            if ((string.Equals(record.Name, section, StringComparison.Ordinal) || string.Equals(record.NameReplace, section, StringComparison.Ordinal)) &&
                XRayTrilogyReader.ReadCreatureVitals(raw, record) is { } vitals)
            {
                result.Add(vitals);
            }
        }

        return result;
    }

    /// <summary>Returns a read-only exact object-record window when the handle is present.</summary>
    public XRayObjectRecord? FindObjectRecord(ushort handle)
    {
        foreach (var record in RegistryObjects)
        {
            if (record.ObjectId == handle)
            {
                return new XRayObjectRecord(
                    record.ObjectId,
                    record.Name,
                    record.NameReplace,
                    record.RecordOffset,
                    Container.Raw.Slice(record.RecordOffset, record.RecordLength));
            }
        }

        return null;
    }
}

/// <summary>Read-only life state of a creature object; a health of zero or less means it is dead.</summary>
public sealed record XRayCreatureVitals(ushort Handle, string Section, float Health, ushort? KillerId, ulong? DeathTime)
{
    public bool IsDead => Health <= 0f;

    internal int HealthOffset { get; init; }
}

public sealed record XRayFactionRelation(int CommunityIndex, int Value);

/// <summary>Registry identity for one level-changer object in an X-Ray save.</summary>
public sealed record XRayLevelChanger(
    ushort Handle,
    ushort ParentId,
    int ObjectVersion,
    string Name,
    string NameReplace);

public sealed record XRayObjectRecord(
    ushort Handle,
    string Name,
    string NameReplace,
    int Offset,
    ReadOnlyMemory<byte> Bytes);

internal sealed record XRayRegistryObject(
    string Name,
    string NameReplace,
    ushort ObjectId,
    ushort ParentId,
    ushort Version,
    int RecordOffset,
    int RecordLength,
    int StateOffset,
    int StateLength,
    int UpdateOffset,
    int UpdateLength,
    int? ClientDataOffset,
    int ClientDataLength);

internal readonly record struct XRayPlacementAnchor(ushort Value, int Offset);

public sealed class XRayStash
{
    internal XRayStash(
        ushort handle,
        string name,
        string? level,
        IReadOnlyList<XRayInventoryItem> items)
    {
        Handle = handle;
        Name = name;
        Level = level;
        Items = items;
    }

    public ushort Handle { get; }

    public string Name { get; }

    public string? Level { get; }

    public IReadOnlyList<XRayInventoryItem> Items { get; }
}

public sealed class XRayInventoryItem
{
    private readonly ushort _placementValue;
    private readonly int _placementOffset;

    internal XRayInventoryItem(
        ushort handle,
        ushort parentId,
        string typeKey,
        int kindCode,
        string category,
        ushort? count,
        bool editableCount,
        IReadOnlyList<string>? upgrades,
        int? stackStateCountOffset,
        int? stackUpdateCountOffset,
        float? condition,
        int? conditionStateOffset,
        int? conditionUpdateOffset,
        int? clientConditionOffset,
        XRayPlacementAnchor? placement)
    {
        Handle = handle;
        ParentId = parentId;
        TypeKey = typeKey;
        KindCode = kindCode;
        Category = category;
        Count = count;
        EditableCount = editableCount;
        Upgrades = upgrades;
        StackStateCountOffset = stackStateCountOffset;
        StackUpdateCountOffset = stackUpdateCountOffset;
        Condition = condition;
        ConditionStateOffset = conditionStateOffset;
        ConditionUpdateOffset = conditionUpdateOffset;
        ClientConditionOffset = clientConditionOffset;
        _placementValue = placement?.Value ?? 0;
        _placementOffset = placement?.Offset ?? -1;
    }

    public ushort Handle { get; }

    public ushort ParentId { get; }

    public string TypeKey { get; }

    public int KindCode { get; }

    public string Category { get; }

    public ushort? Count { get; }

    public bool EditableCount { get; }

    public float? Condition { get; }

    public bool ConditionEditable => Condition is not null && ConditionStateOffset is not null;

    public IReadOnlyList<string>? Upgrades { get; }

    public string? PlacementType => _placementOffset < 0
        ? null
        : (_placementValue & 0x0F) switch
        {
            1 => "slot",
            2 => "belt",
            3 => "ruck",
            _ => null,
        };

    public int? PlacementSlot => _placementOffset < 0 ? null : (_placementValue >> 4) & 0x3F;

    public int? PlacementBaseSlot => _placementOffset < 0 ? null : (_placementValue >> 10) & 0x3F;

    public string? PlacementStorage => _placementOffset < 0
        ? null
        : (_placementValue & 0x0F) == 1 ? "equipped" : "inventory";

    public bool PlacementEditable => _placementOffset >= 0;

    internal int? StackStateCountOffset { get; }

    internal int? StackUpdateCountOffset { get; }

    internal int? ConditionStateOffset { get; }

    internal int? ConditionUpdateOffset { get; }

    internal int? ClientConditionOffset { get; }

    internal int? PlacementOffset => _placementOffset >= 0 ? _placementOffset : null;
}
