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
        float? actorHealth,
        int? actorRank,
        int? actorReputation,
        string? actorName,
        ulong gameTime,
        float timeFactor,
        float normalTimeFactor,
        IReadOnlyList<XRayInventoryItem> inventory)
    {
        FormatId = formatId;
        ContainerVersion = containerVersion;
        Container = container;
        ActorVersion = actorVersion;
        ActorId = actorId;
        Money = money;
        MoneyOffset = moneyOffset;
        PlayerFactionIndex = playerFactionIndex;
        ActorHealth = actorHealth;
        ActorRank = actorRank;
        ActorReputation = actorReputation;
        ActorName = actorName;
        GameTime = gameTime;
        TimeFactor = timeFactor;
        NormalTimeFactor = normalTimeFactor;
        Inventory = inventory;
    }

    public string FormatId { get; }

    public uint ContainerVersion { get; }

    internal XRayContainer Container { get; }

    public int ActorVersion { get; }

    public ushort ActorId { get; }

    public uint Money { get; }

    internal int MoneyOffset { get; }

    public int? PlayerFactionIndex { get; }

    public float? ActorHealth { get; }

    public int? ActorRank { get; }

    public int? ActorReputation { get; }

    public string? ActorName { get; }

    public ulong GameTime { get; }

    public float TimeFactor { get; }

    public float NormalTimeFactor { get; }

    public IReadOnlyList<XRayInventoryItem> Inventory { get; }
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
        IReadOnlyList<string>? upgrades)
    {
        Handle = handle;
        ParentId = parentId;
        TypeKey = typeKey;
        KindCode = kindCode;
        Category = category;
        Count = count;
        EditableCount = editableCount;
        Upgrades = upgrades;
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
}
