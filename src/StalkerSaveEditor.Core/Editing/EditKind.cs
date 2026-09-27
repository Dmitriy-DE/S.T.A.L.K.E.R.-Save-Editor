namespace StalkerSaveEditor.Core.Editing;

[Flags]
public enum EditKind
{
    None = 0,
    Money = 1 << 0,
    StackCounts = 1 << 1,
    Delete = 1 << 2,
    Add = 1 << 3,
    XRayStashTransfer = 1 << 4,
    Stalker2StashTransfer = 1 << 5,
    Upgrades = 1 << 6,
    Durability = 1 << 7,
    Placement = 1 << 8,
    Faction = 1 << 9,
}
