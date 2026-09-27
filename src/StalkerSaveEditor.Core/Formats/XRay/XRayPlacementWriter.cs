using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayPlacementWriter
{
    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Placements.Count == 0)
        {
            throw Error("EditPlan must include at least one placement change.");
        }

        if (plan.EditKinds != EditKind.Placement)
        {
            throw Error("Placement-only writer does not accept money, stack, durability, add, delete, or stash edits.");
        }

        var sourceBytes = source.ToArray();
        if (!string.Equals(Sha256(sourceBytes), plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        if (!CapabilityRegistry.Get(parsed.FormatId, "edit_placement").Writable)
        {
            throw Error($"Placement editing is not enabled for {parsed.FormatId}.");
        }

        var raw = parsed.Container.Raw.ToArray();
        var changedOffsets = new HashSet<int>();
        foreach (var change in plan.Placements)
        {
            var item = parsed.Inventory.FirstOrDefault(candidate => candidate.Handle == change.Handle);
            if (item is null || !item.PlacementEditable || item.PlacementOffset is not { } offset)
            {
                throw Error($"Object 0x{change.Handle:X4} has no confirmed actor client-data placement field.");
            }

            if (!IsAllowed(parsed.FormatId, item, change))
            {
                throw Error(
                    $"Object 0x{change.Handle:X4} ({item.TypeKey}) cannot be moved to " +
                    $"{change.Type}{(change.SlotId is { } slot ? $" {slot}" : string.Empty)}.");
            }

            if (offset < 0 || offset > raw.Length - sizeof(ushort))
            {
                throw Error($"Object 0x{change.Handle:X4} placement field exceeds the unpacked save bounds.");
            }

            var current = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset));
            var replacement = change.Type switch
            {
                "slot" => (ushort)((current & 0xFC00) | (change.SlotId!.Value << 4) | 1),
                "belt" => (ushort)((current & 0xFFF0) | 2),
                "ruck" => (ushort)((current & 0xFFF0) | 3),
                _ => throw Error($"Unknown placement type '{change.Type}'."),
            };
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(offset), replacement);
            changedOffsets.Add(offset);
            changedOffsets.Add(offset + 1);
        }

        var output = parsed.Container.Build(raw);
        var roundTrip = ReadSupported(output);
        if (!string.Equals(roundTrip.FormatId, parsed.FormatId, StringComparison.Ordinal) ||
            !raw.AsSpan().SequenceEqual(roundTrip.Container.Raw.Span))
        {
            throw Error("Placement write did not pass its format and unpacked-byte round-trip.");
        }

        var roundTripRaw = roundTrip.Container.Raw.Span;
        for (var index = 0; index < roundTripRaw.Length; index++)
        {
            if (!changedOffsets.Contains(index) && roundTripRaw[index] != parsed.Container.Raw.Span[index])
            {
                throw Error($"Placement write changed unrelated unpacked byte {index}.");
            }
        }

        foreach (var change in plan.Placements)
        {
            var item = roundTrip.Inventory.FirstOrDefault(candidate => candidate.Handle == change.Handle);
            if (item is null || item.PlacementType != change.Type ||
                (change.Type == "slot" && item.PlacementSlot != change.SlotId))
            {
                throw Error($"Placement round-trip for object 0x{change.Handle:X4} did not match.");
            }
        }

        return new PreparedEdit(plan, output);
    }

    private static bool IsAllowed(string releaseId, XRayInventoryItem item, XRayPlacementChange change)
    {
        if (change.Type == "ruck")
        {
            return true;
        }

        if (change.Type == "belt")
        {
            return item.TypeKey.StartsWith("af_", StringComparison.OrdinalIgnoreCase);
        }

        var maximumSlot = releaseId == "stalker-cop" ? 12 : 10;
        return change.Type == "slot" &&
            change.SlotId is { } slot && slot <= maximumSlot &&
            item.PlacementBaseSlot == slot;
    }

    private static XRayTrilogySave ReadSupported(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException originalFailure)
        {
            try
            {
                return XRayEnhancedReader.FromBytes(data);
            }
            catch (XRayFormatException)
            {
                throw originalFailure;
            }
        }
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static XRayFormatException Error(string message) => new($"X-Ray placement edit: {message}");
}
