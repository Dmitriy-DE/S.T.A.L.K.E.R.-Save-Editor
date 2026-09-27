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

    public float? ActorHealth { get; }

    public int? ActorRank { get; }

    public int? ActorReputation { get; }

    public string? ActorName { get; }

    internal IReadOnlyList<XRayRegistryObject> RegistryObjects { get; }

    public ulong GameTime { get; }

    public float TimeFactor { get; }

    public float NormalTimeFactor { get; }

    public IReadOnlyList<XRayInventoryItem> Inventory { get; }

    public IReadOnlyList<XRayStash> Stashes { get; }
}

public sealed record XRayFactionRelation(int CommunityIndex, int Value);

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
        int? stackUpdateCountOffset)
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
    }

    public ushort Handle { get; }

    public ushort ParentId { get; }

    public string TypeKey { get; }

    public int KindCode { get; }

    public string Category { get; }

    public ushort? Count { get; }

    public bool EditableCount { get; }

    public float? Condition => null;

    public IReadOnlyList<string>? Upgrades { get; }

    internal int? StackStateCountOffset { get; }

    internal int? StackUpdateCountOffset { get; }
}
