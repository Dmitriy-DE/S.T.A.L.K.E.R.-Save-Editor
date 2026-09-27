using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayStashWriter
{
    private const int ClientPlaceOffset = 1;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.StashTakes.Count == 0 && plan.StashPuts.Count == 0)
        {
            throw Error("EditPlan must include at least one stash transfer.");
        }

        if (plan.Money is not null || plan.StackCounts.Count > 0 ||
            plan.DetachHandles.Count > 0 || plan.Adds.Count > 0)
        {
            throw Error("Stash-only writer does not accept money, stack, delete, or add requests.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var before = ReadSupported(sourceBytes);
        if (!CapabilityRegistry.Get(before.FormatId, "add_items").Writable)
        {
            throw Error($"Stash item transfers are not enabled for {before.FormatId}.");
        }

        var sourceRaw = before.Container.Raw.Span;
        var itemHandles = plan.StashTakes
            .Concat(plan.StashPuts.Select(request => request.ObjectId))
            .Distinct()
            .ToArray();
        var itemStates = new Dictionary<ushort, byte[][]>();
        foreach (var handle in itemHandles)
        {
            itemStates.Add(handle, CaptureState(before, handle, sourceRaw));
        }

        var working = sourceBytes;
        foreach (var handle in plan.StashTakes)
        {
            var current = ReadSameFormat(working, before.FormatId);
            var item = FindObject(current, handle);
            var box = item.ParentId == ushort.MaxValue ? null : FindObjectOrNull(current, item.ParentId);
            if (box is null || !IsInventoryBox(box))
            {
                throw Error($"Object 0x{handle:X4} is not in an inventory_box stash.");
            }

            working = ChangeParent(current, item, current.ActorId);
            current = ReadSameFormat(working, before.FormatId);
            item = FindObject(current, handle);
            var resetRecord = XRayAddWriter.ResetAddedState(
                current.Container.Raw.Span,
                item,
                keepUpgrades: true);
            var originalRecord = current.Container.Raw.Span.Slice(item.RecordOffset, item.RecordLength);
            if (!resetRecord.AsSpan().SequenceEqual(originalRecord))
            {
                working = XRayAddWriter.ReplaceRecord(current.Container, item, resetRecord);
            }
        }

        foreach (var request in plan.StashPuts)
        {
            var current = ReadSameFormat(working, before.FormatId);
            var item = FindObject(current, request.ObjectId);
            if (item.ParentId != current.ActorId)
            {
                throw Error($"Object 0x{request.ObjectId:X4} is not directly owned by the actor.");
            }

            if (!IsBackpackItem(current, item))
            {
                throw Error($"Object 0x{request.ObjectId:X4} must be in the backpack before moving to a stash.");
            }

            var box = FindObjectOrNull(current, request.BoxId);
            if (box is null || !IsInventoryBox(box))
            {
                throw Error($"Destination 0x{request.BoxId:X4} is missing or is not an inventory_box.");
            }

            working = ChangeParent(current, item, box.ObjectId);
        }

        var after = ReadSameFormat(working, before.FormatId);
        foreach (var handle in plan.StashTakes)
        {
            var moved = FindObject(after, handle);
            if (moved.ParentId != after.ActorId)
            {
                throw Error($"Stash take round-trip failed for object 0x{handle:X4}.");
            }

            VerifyStateUnchanged(after, moved, itemStates[handle]);
        }

        foreach (var request in plan.StashPuts)
        {
            var moved = FindObject(after, request.ObjectId);
            if (moved.ParentId != request.BoxId)
            {
                throw Error($"Stash put round-trip failed for object 0x{request.ObjectId:X4}.");
            }

            VerifyStateUnchanged(after, moved, itemStates[request.ObjectId]);
        }

        return new PreparedEdit(plan, working);
    }

    private static byte[] ChangeParent(XRayTrilogySave save, XRayRegistryObject item, ushort parentId)
    {
        var raw = save.Container.Raw.Span;
        var spawnOffset = checked(item.RecordOffset + sizeof(ushort));
        var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[item.RecordOffset..]);
        var spawn = XRayAddWriter.PatchSpawnIdentity(
            raw.Slice(spawnOffset, spawnLength),
            item.Name,
            item.ObjectId,
            parentId);
        var update = raw.Slice(item.UpdateOffset, item.UpdateLength);
        var record = XRayAddWriter.FrameRecord(spawn, update);
        return XRayAddWriter.ReplaceRecord(save.Container, item, record);
    }

    private static bool IsBackpackItem(XRayTrilogySave save, XRayRegistryObject item)
    {
        if (item.ClientDataOffset is not { } clientDataOffset || item.ClientDataLength < ClientPlaceOffset + 1)
        {
            return false;
        }

        var raw = save.Container.Raw.Span;
        if (clientDataOffset < 0 || item.ClientDataLength > raw.Length - clientDataOffset)
        {
            return false;
        }

        if (save.FormatId is "stalker-cs" or "stalker-cs-ee")
        {
            return raw[clientDataOffset] == 2 && raw[clientDataOffset + ClientPlaceOffset] == 3;
        }

        if (item.ClientDataLength < ClientPlaceOffset + sizeof(ushort) ||
            !XRayAddWriter.TryReadPlacement(raw[(clientDataOffset + ClientPlaceOffset)..], out var place))
        {
            return false;
        }

        return (place & 0x0F) == 3;
    }

    private static XRayRegistryObject FindObject(XRayTrilogySave save, ushort handle) =>
        FindObjectOrNull(save, handle)
        ?? throw Error($"Object 0x{handle:X4} is missing from the X-Ray registry.");

    private static XRayRegistryObject? FindObjectOrNull(XRayTrilogySave save, ushort handle) =>
        save.RegistryObjects.FirstOrDefault(item => item.ObjectId == handle);

    private static bool IsInventoryBox(XRayRegistryObject item) =>
        string.Equals(item.Name, "inventory_box", StringComparison.Ordinal);

    private static byte[][] CaptureState(
        XRayTrilogySave save,
        ushort handle,
        ReadOnlySpan<byte> raw)
    {
        var item = FindObject(save, handle);
        return
        [
            raw.Slice(item.StateOffset, item.StateLength).ToArray(),
            raw.Slice(item.UpdateOffset, item.UpdateLength).ToArray(),
        ];
    }

    private static void VerifyStateUnchanged(
        XRayTrilogySave save,
        XRayRegistryObject item,
        byte[][] expected)
    {
        var raw = save.Container.Raw.Span;
        if (!raw.Slice(item.StateOffset, item.StateLength).SequenceEqual(expected[0]) ||
            !raw.Slice(item.UpdateOffset, item.UpdateLength).SequenceEqual(expected[1]))
        {
            throw Error($"Stash transfer changed serialized state for object 0x{item.ObjectId:X4}.");
        }
    }

    private static XRayTrilogySave ReadSameFormat(ReadOnlySpan<byte> data, string expectedFormatId)
    {
        var parsed = ReadSupported(data);
        if (!string.Equals(parsed.FormatId, expectedFormatId, StringComparison.Ordinal))
        {
            throw Error("Stash transfer changed the detected X-Ray release.");
        }

        return parsed;
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

    private static XRayFormatException Error(string message) => new($"X-Ray stash edit: {message}");
}
