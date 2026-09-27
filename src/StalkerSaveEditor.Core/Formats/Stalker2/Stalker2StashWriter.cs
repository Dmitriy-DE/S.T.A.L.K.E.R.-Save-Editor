using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public static class Stalker2StashWriter
{
    private const int GridRecordSize = 8;
    private const int GridWidth = 8;
    private const int GridHeight = 128;
    private const int RecordFlagOffset = 15;
    private const int RecordMarkerOffset = 18;
    private const int RecordCountOffset = 19;
    private const int RecordWeightOffset = 24;
    private const int RecordFlagsOffset = 28;
    private const int RecordKindOffset = 31;
    private const int MinimumRecordSize = 36;
    private const byte StashFlag = 0x01;
    private const byte StashFlagBit = 0x08;
    private const uint Tombstone = uint.MaxValue;
    private static readonly HashSet<byte> KnownKinds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11];

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Stalker2StashTakeHandle is not { } handle)
        {
            throw Error("EditPlan must include an S2 stash item handle.");
        }

        if (plan.EditKinds != EditKind.Stalker2StashTransfer)
        {
            throw Error("S2 stash-only writer does not accept other edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = Stalker2SaveReader.FromBytes(sourceBytes);
        var capability = CapabilityRegistry.Get(parsed.ReleaseId, "move_items");
        if (!capability.Writable)
        {
            throw Error($"S2 stash transfers are not enabled ({capability.Maturity}).");
        }

        var raw = TransferToPlayerRaw(parsed.Raw.Span, handle);
        byte[] compressed;
        try
        {
            compressed = KrakenCodec.Compress(raw, level: 5);
        }
        catch (InvalidDataException exception)
        {
            throw new Stalker2FormatException(
                $"S.T.A.L.K.E.R. 2 stash edit: Kraken compression failed: {exception.Message}",
                exception);
        }

        var output = Stalker2ContainerWriter.Build(raw, compressed);
        var roundTrip = Stalker2SaveReader.FromBytes(output);
        if (!roundTrip.CrcOk || !raw.AsSpan().SequenceEqual(roundTrip.Raw.Span))
        {
            throw Error("Stash transfer did not pass its container, CRC and raw-byte round-trip.");
        }

        return new PreparedEdit(plan, output);
    }

    internal static byte[] TransferToPlayerRaw(ReadOnlySpan<byte> raw, uint handle)
    {
        if (handle is 0 or Tombstone)
        {
            throw Error("S2 stash item handle must not be zero or the tombstone value.");
        }

        var stash = Stalker2StashReader.Locate(raw);
        var stashSlots = stash.OwnedHandles
            .Select((value, index) => (value, index))
            .Where(pair => pair.value == handle)
            .Select(pair => pair.index)
            .ToArray();
        if (stashSlots.Length != 1)
        {
            throw Error($"Handle 0x{handle:X8} must occur exactly once in the S2 stash.");
        }

        var player = Stalker2InventoryReader.LocateLayout(raw);
        if (player.UnresolvedHandles.Count > 0)
        {
            throw Error("Transfer stopped because player inventory has unresolved handles.");
        }

        if (player.OwnedHandles.Contains(handle))
        {
            throw Error($"Handle 0x{handle:X8} already belongs to the player.");
        }

        var record = FindUniqueObjectRecord(raw, handle);
        if (!KnownKinds.Contains(record.Kind))
        {
            throw Error($"Transfer is disabled for unknown object kind={record.Kind}.");
        }

        var stashFlag = raw[record.Offset + RecordFlagOffset];
        var objectFlags = raw[record.Offset + RecordFlagsOffset];
        if (stashFlag != StashFlag || (objectFlags & StashFlagBit) == 0)
        {
            throw Error($"Object 0x{handle:X8} is not marked as a stash item.");
        }

        var itemCells = stash.GridCells.Where(cell => cell.Handle == handle).ToArray();
        if (itemCells.Length == 0)
        {
            throw Error($"Handle 0x{handle:X8} has no cells in the S2 stash grid.");
        }

        var baseX = itemCells.Min(cell => cell.X);
        var baseY = itemCells.Min(cell => cell.Y);
        var shape = itemCells
            .Select(cell => (X: (int)cell.X - baseX, Y: (int)cell.Y - baseY))
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .ToArray();
        if (shape.Distinct().Count() != shape.Length)
        {
            throw Error($"Handle 0x{handle:X8} has duplicate cells in the S2 stash grid.");
        }

        var occupied = player.GridCells.Select(cell => (cell.X, cell.Y)).ToHashSet();
        var position = FindFreeSpot(occupied, shape, handle);
        var mutable = raw.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(mutable.AsSpan(record.Offset + 11), position.X);
        BinaryPrimitives.WriteUInt16LittleEndian(mutable.AsSpan(record.Offset + 13), position.Y);
        mutable[record.Offset + RecordFlagOffset] = 0;
        mutable[record.Offset + RecordFlagsOffset] &= unchecked((byte)~StashFlagBit);

        var stashHandles = stash.OwnedHandles.ToArray();
        stashHandles[stashSlots[0]] = Tombstone;
        var remainingStashCells = stash.GridCells.Where(cell => cell.Handle != handle).ToArray();
        var stashEnd = RebuildArrays(
            mutable,
            stash.OwnedCountOffset,
            stash.GridEndOffset,
            stashHandles,
            remainingStashCells);

        player = Stalker2InventoryReader.LocateLayout(stashEnd);
        var playerHandles = player.OwnedHandles.Append(handle).ToArray();
        var playerCells = player.GridCells.Concat(shape.Select(cell => new Stalker2GridCell(
            handle,
            checked((ushort)(position.X + cell.X)),
            checked((ushort)(position.Y + cell.Y))))).ToArray();
        var result = RebuildArrays(
            stashEnd,
            player.OwnedCountOffset,
            player.GridEndOffset,
            playerHandles,
            playerCells);

        VerifyTransfer(result, handle, position, shape);
        return result;
    }

    private static ObjectRecord FindUniqueObjectRecord(ReadOnlySpan<byte> raw, uint handle)
    {
        Span<byte> needle = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(needle, handle);
        ObjectRecord? found = null;
        var searchFrom = 0;
        while (searchFrom <= raw.Length - needle.Length)
        {
            var relative = raw[searchFrom..].IndexOf(needle);
            if (relative < 0) break;
            var offset = searchFrom + relative;
            searchFrom = offset + 1;
            if (raw.Length - offset < MinimumRecordSize || raw[offset + RecordMarkerOffset] != 0x38) continue;

            var count = BinaryPrimitives.ReadUInt32LittleEndian(raw[(offset + RecordCountOffset)..]);
            var weight = BinaryPrimitives.ReadSingleLittleEndian(raw[(offset + RecordWeightOffset)..]);
            var kind = raw[offset + RecordKindOffset];
            if (count is < 1 or > 10_000_000 || !float.IsFinite(weight) || weight < 0 || weight > 10_000_000) continue;
            if (found is not null)
            {
                throw Error($"Object record 0x{handle:X8} has multiple candidates.");
            }

            found = new ObjectRecord(offset, kind);
        }

        return found ?? throw Error($"Object record 0x{handle:X8} was not found unambiguously.");
    }

    private static (ushort X, ushort Y) FindFreeSpot(
        HashSet<(ushort X, ushort Y)> occupied,
        (int X, int Y)[] shape,
        uint handle)
    {
        for (var y = 0; y < GridHeight; y++)
        {
            for (var x = 0; x < GridWidth; x++)
            {
                var fits = true;
                foreach (var cell in shape)
                {
                    var candidateX = x + cell.X;
                    var candidateY = y + cell.Y;
                    if (candidateX >= GridWidth || candidateY >= GridHeight ||
                        occupied.Contains(((ushort)candidateX, (ushort)candidateY)))
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits) return ((ushort)x, (ushort)y);
            }
        }

        throw Error($"No free player-grid spot is available for stash handle 0x{handle:X8}.");
    }

    private static byte[] RebuildArrays(
        ReadOnlySpan<byte> raw,
        int countOffset,
        int oldEndOffset,
        IReadOnlyList<uint> handles,
        IReadOnlyList<Stalker2GridCell> cells)
    {
        if (handles.Count > ushort.MaxValue || cells.Count > ushort.MaxValue)
        {
            throw Error("Inventory or stash array exceeds the u16 count limit.");
        }

        var handleBytes = checked(handles.Count * sizeof(uint));
        var gridBytes = checked(cells.Count * GridRecordSize);
        var replacementLength = checked(sizeof(ushort) + handleBytes + sizeof(ushort) + gridBytes);
        var oldLength = oldEndOffset - countOffset;
        var output = new byte[checked(raw.Length - oldLength + replacementLength)];
        raw[..countOffset].CopyTo(output);

        var offset = countOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), checked((ushort)handles.Count));
        offset += sizeof(ushort);
        foreach (var handle in handles)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset), handle);
            offset += sizeof(uint);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), checked((ushort)cells.Count));
        offset += sizeof(ushort);
        foreach (var cell in cells)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset), cell.Handle);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + sizeof(uint)), cell.X);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + sizeof(uint) + sizeof(ushort)), cell.Y);
            offset += GridRecordSize;
        }

        raw[oldEndOffset..].CopyTo(output.AsSpan(offset));
        return output;
    }

    private static void VerifyTransfer(
        ReadOnlySpan<byte> raw,
        uint handle,
        (ushort X, ushort Y) position,
        (int X, int Y)[] shape)
    {
        var stash = Stalker2StashReader.Locate(raw);
        if (stash.LiveHandles.Contains(handle) || stash.GridCells.Any(cell => cell.Handle == handle))
        {
            throw Error($"Stash transfer round-trip left handle 0x{handle:X8} in the stash.");
        }

        var player = Stalker2InventoryReader.LocateLayout(raw);
        if (player.UnresolvedHandles.Count > 0 || player.OwnedHandles.Count(item => item == handle) != 1)
        {
            throw Error($"Stash transfer round-trip did not add handle 0x{handle:X8} to the player.");
        }

        var movedCells = player.GridCells.Where(cell => cell.Handle == handle).ToArray();
        var expectedCells = shape
            .Select(cell => ((ushort)(position.X + cell.X), (ushort)(position.Y + cell.Y)))
            .OrderBy(cell => cell.Item2)
            .ThenBy(cell => cell.Item1)
            .ToArray();
        var actualCells = movedCells
            .Select(cell => (cell.X, cell.Y))
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
        if (!expectedCells.SequenceEqual(actualCells))
        {
            throw Error($"Stash transfer round-trip produced an invalid grid footprint for 0x{handle:X8}.");
        }

        var record = FindUniqueObjectRecord(raw, handle);
        if (raw[record.Offset + RecordFlagOffset] != 0 ||
            (raw[record.Offset + RecordFlagsOffset] & StashFlagBit) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(raw[(record.Offset + 11)..]) != position.X ||
            BinaryPrimitives.ReadUInt16LittleEndian(raw[(record.Offset + 13)..]) != position.Y)
        {
            throw Error($"Stash transfer round-trip did not clear the stash object flags for 0x{handle:X8}.");
        }
    }

    private static Stalker2FormatException Error(string message) =>
        new($"S.T.A.L.K.E.R. 2 stash edit: {message}");

    private sealed record ObjectRecord(int Offset, byte Kind);
}
