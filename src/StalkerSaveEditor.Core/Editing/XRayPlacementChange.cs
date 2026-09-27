namespace StalkerSaveEditor.Core.Editing;

public sealed record XRayPlacementChange
{
    public XRayPlacementChange(uint handle, string type, int? slotId)
    {
        if (handle is 0 or > ushort.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "X-Ray handles must be in the range 1…65534.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalizedType = type.Trim().ToLowerInvariant();
        if (normalizedType is not ("slot" or "belt" or "ruck"))
        {
            throw new ArgumentException("Placement type must be slot, belt, or ruck.", nameof(type));
        }

        if (normalizedType == "slot")
        {
            if (slotId is null or < 1 or > 13)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId), "Slot id must be in the range 1…13.");
            }
        }
        else if (slotId is not null)
        {
            throw new ArgumentException("Belt and ruck placements must not include a slot id.", nameof(slotId));
        }

        Handle = handle;
        Type = normalizedType;
        SlotId = slotId;
    }

    public uint Handle { get; }

    public string Type { get; }

    public int? SlotId { get; }
}
