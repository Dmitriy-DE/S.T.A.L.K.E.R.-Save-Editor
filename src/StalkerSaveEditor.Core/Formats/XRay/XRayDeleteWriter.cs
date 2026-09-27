using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayDeleteWriter
{
    private const int ClientPlaceOffset = 1;
    private const int SlotCount = 14;
    private const ushort ActorParentId = 0;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.DetachHandles.Count == 0)
        {
            throw Error("EditPlan must include at least one object to remove.");
        }

        if (plan.Money is not null || plan.StackCounts.Count != 0 || plan.Adds.Count > 0 || plan.Upgrades.Count > 0 ||
            plan.StashTakes.Count > 0 || plan.StashPuts.Count > 0)
        {
            throw Error("Delete-only writer does not accept money, stack, add, or stash-transfer edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        var originalFormatId = parsed.FormatId;
        if (!CapabilityRegistry.Get(parsed.FormatId, "remove_items").Writable)
        {
            throw Error($"Item removal is not enabled for {parsed.FormatId}.");
        }

        var working = sourceBytes;
        var removedHandles = new HashSet<ushort>();
        foreach (var handle in plan.DetachHandles)
        {
            if (!removedHandles.Add(handle))
            {
                throw Error($"Object 0x{handle:X4} is listed for removal more than once.");
            }

            parsed = ReadSupported(working);
            if (!string.Equals(parsed.FormatId, originalFormatId, StringComparison.Ordinal))
            {
                throw Error("Removal changed the detected X-Ray release.");
            }

            var target = parsed.RegistryObjects.FirstOrDefault(item => item.ObjectId == handle);
            if (target is null)
            {
                throw Error($"Object 0x{handle:X4} is unresolved or missing.");
            }

            if (target.ObjectId == parsed.ActorId)
            {
                throw Error("The actor object cannot be removed.");
            }

            if (target.ParentId != parsed.ActorId)
            {
                throw Error($"Object 0x{handle:X4} is not directly owned by the actor.");
            }

            if (IsKnownEquipped(parsed.Container.Raw.Span, target))
            {
                throw Error($"Equipped object 0x{handle:X4} cannot be removed.");
            }

            if (target.Name.StartsWith("ammo_", StringComparison.OrdinalIgnoreCase) &&
                parsed.Inventory.FirstOrDefault(item => item.Handle == handle)?.EditableCount != true)
            {
                throw Error($"Ammo object 0x{handle:X4} is unresolved and cannot be removed.");
            }

            if (parsed.RegistryObjects.Any(item =>
                item.ObjectId != target.ObjectId && item.ParentId == target.ObjectId))
            {
                throw Error($"Object 0x{handle:X4} has dependent registry children.");
            }

            working = RemoveRecord(parsed.Container, target);
        }

        var roundTrip = ReadSupported(working);
        if (!string.Equals(roundTrip.FormatId, parsed.FormatId, StringComparison.Ordinal) ||
            removedHandles.Any(handle => roundTrip.RegistryObjects.Any(item => item.ObjectId == handle)))
        {
            throw Error("Removal did not pass its format and object-registry round-trip.");
        }

        return new PreparedEdit(plan, working);
    }

    private static bool IsKnownEquipped(ReadOnlySpan<byte> data, XRayRegistryObject item)
    {
        if (item.ClientDataOffset is not { } clientDataOffset || item.ClientDataLength < ClientPlaceOffset + sizeof(ushort))
        {
            return false;
        }

        var placeOffset = checked(clientDataOffset + ClientPlaceOffset);
        if (placeOffset < 0 || placeOffset + sizeof(ushort) > data.Length)
        {
            return false;
        }

        var place = BinaryPrimitives.ReadUInt16LittleEndian(data[placeOffset..]);
        var placeType = place & 0x0F;
        var slot = (place >> 4) & 0x3F;
        var baseSlot = (place >> 10) & 0x3F;
        return placeType == 1 && slot is >= 1 and < SlotCount && baseSlot is >= 1 and < SlotCount;
    }

    private static byte[] RemoveRecord(XRayContainer container, XRayRegistryObject target)
    {
        XRayChunk? objectChunk = null;
        foreach (var chunk in container.Chunks)
        {
            if (chunk.Type != 2)
            {
                continue;
            }

            if (objectChunk is not null)
            {
                throw Error("OBJECT chunk occurs more than once.");
            }

            objectChunk = chunk;
        }

        if (objectChunk is null)
        {
            throw Error("OBJECT chunk is missing.");
        }

        var data = objectChunk.Data.Span;
        if (data.Length < sizeof(uint))
        {
            throw Error("OBJECT chunk is shorter than its object count.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count <= 1)
        {
            throw Error("OBJECT registry cannot be left without its actor.");
        }

        var recordStart = checked(target.RecordOffset - (objectChunk.Offset + 8));
        var recordEnd = checked(recordStart + target.RecordLength);
        if (recordStart < sizeof(uint) || recordEnd > data.Length || recordStart >= recordEnd)
        {
            throw Error($"Object 0x{target.ObjectId:X4} has invalid registry record boundaries.");
        }

        var objectPayload = GC.AllocateUninitializedArray<byte>(data.Length - target.RecordLength);
        BinaryPrimitives.WriteUInt32LittleEndian(objectPayload, count - 1);
        data[sizeof(uint)..recordStart].CopyTo(objectPayload.AsSpan(sizeof(uint)));
        data[recordEnd..].CopyTo(objectPayload.AsSpan(recordStart));

        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[sizeof(uint) * 2];
        foreach (var chunk in container.Chunks)
        {
            var payload = chunk.Offset == objectChunk.Offset ? objectPayload : chunk.Data.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(header, chunk.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(header[sizeof(uint)..], checked((uint)payload.Length));
            output.Write(header);
            output.Write(payload);
        }

        return container.Build(output.ToArray());
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

    private static XRayFormatException Error(string message) => new($"X-Ray delete edit: {message}");
}
