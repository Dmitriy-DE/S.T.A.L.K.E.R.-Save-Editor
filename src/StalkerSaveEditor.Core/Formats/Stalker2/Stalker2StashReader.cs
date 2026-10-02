using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public sealed class Stalker2StashLayout
{
    internal Stalker2StashLayout(
        int ownedCountOffset,
        IEnumerable<uint> ownedHandles,
        IEnumerable<Stalker2GridCell> gridCells,
        int gridEndOffset)
    {
        OwnedCountOffset = ownedCountOffset;
        OwnedHandles = Array.AsReadOnly(ownedHandles.ToArray());
        LiveHandles = Array.AsReadOnly(OwnedHandles.Where(handle => handle != uint.MaxValue).ToArray());
        GridCells = Array.AsReadOnly(gridCells.ToArray());
        GridEndOffset = gridEndOffset;
    }

    public int OwnedCountOffset { get; }

    public IReadOnlyList<uint> OwnedHandles { get; }

    public IReadOnlyList<uint> LiveHandles { get; }

    public IReadOnlyList<Stalker2GridCell> GridCells { get; }

    public int GridEndOffset { get; }
}

public static class Stalker2StashReader
{
    private const int SearchWindow = 512;
    private const int MaximumOwnedHandles = 4096;
    private const int MaximumGridCells = 8192;
    private const int GridRecordSize = 8;
    private static readonly byte[] Marker = [0xFF, 0xFF, 0xFF, 0xFF, 0x06, 0x01, 0x00, 0x00, 0x00, 0x06];
    private static readonly byte[] HeaderTail = [0x03, 0x00, 0x00, 0x00];

    public static Stalker2StashLayout Locate(ReadOnlySpan<byte> raw) =>
        Locate(raw, Stalker2InventoryReader.LocateLayout(raw));

    /// <summary>For callers that already located the player's inventory in the same bytes (it is a scan of the save).</summary>
    public static Stalker2StashLayout Locate(ReadOnlySpan<byte> raw, Stalker2InventoryLayout player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var searchStart = player.GridEndOffset;
        var searchEnd = Math.Min(raw.Length, checked(searchStart + SearchWindow));
        var markerOffset = FindUniqueMarker(raw, searchStart, searchEnd);
        if (markerOffset < 0)
        {
            throw Error("stash header was not found unambiguously.");
        }

        if (raw.Length - markerOffset < 20 || !raw.Slice(markerOffset + 16, HeaderTail.Length).SequenceEqual(HeaderTail))
        {
            throw Error("unknown stash header shape.");
        }

        var ownedCountOffset = checked(markerOffset + 20);
        RequireRange(raw, ownedCountOffset, sizeof(ushort), "stash owned-handle count is truncated.");
        var ownedCount = BinaryPrimitives.ReadUInt16LittleEndian(raw[ownedCountOffset..]);
        if (ownedCount > MaximumOwnedHandles)
        {
            throw Error($"suspicious owned-handle count {ownedCount}.");
        }

        var handlesOffset = checked(ownedCountOffset + sizeof(ushort));
        var handlesBytes = checked(ownedCount * sizeof(uint));
        RequireRange(raw, handlesOffset, handlesBytes + sizeof(ushort), "stash owned-handle array is truncated.");
        var handles = new uint[ownedCount];
        for (var index = 0; index < handles.Length; index++)
        {
            handles[index] = BinaryPrimitives.ReadUInt32LittleEndian(raw[(handlesOffset + index * sizeof(uint))..]);
        }

        var gridCountOffset = checked(handlesOffset + handlesBytes);
        var gridCount = BinaryPrimitives.ReadUInt16LittleEndian(raw[gridCountOffset..]);
        if (gridCount > MaximumGridCells)
        {
            throw Error($"suspicious grid-cell count {gridCount}.");
        }

        var gridOffset = checked(gridCountOffset + sizeof(ushort));
        var gridBytes = checked(gridCount * GridRecordSize);
        RequireRange(raw, gridOffset, gridBytes, "stash grid is truncated.");
        var cells = new Stalker2GridCell[gridCount];
        for (var index = 0; index < cells.Length; index++)
        {
            var offset = checked(gridOffset + index * GridRecordSize);
            cells[index] = new Stalker2GridCell(
                BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]),
                BinaryPrimitives.ReadUInt16LittleEndian(raw[(offset + sizeof(uint))..]),
                BinaryPrimitives.ReadUInt16LittleEndian(raw[(offset + sizeof(uint) + sizeof(ushort))..]));
        }

        var live = handles.Where(handle => handle != uint.MaxValue).ToHashSet();
        if (live.Any(handle => (handle >> 16) != 0x3000) || cells.Any(cell => !live.Contains(cell.Handle)))
        {
            throw Error("stash owned-handle list and grid are inconsistent.");
        }

        return new Stalker2StashLayout(
            ownedCountOffset,
            handles,
            cells,
            checked(gridOffset + gridBytes));
    }

    private static int FindUniqueMarker(ReadOnlySpan<byte> raw, int start, int end)
    {
        if (start < 0 || end < start) return -1;
        var found = -1;
        var searchFrom = start;
        while (searchFrom <= end - Marker.Length)
        {
            var relative = raw.Slice(searchFrom, end - searchFrom).IndexOf(Marker);
            if (relative < 0) break;
            found = searchFrom + relative;
            if (searchFrom != start) return -1;
            searchFrom = found + 1;
        }

        return found;
    }

    private static void RequireRange(ReadOnlySpan<byte> raw, int offset, int length, string message)
    {
        if (offset < 0 || length < 0 || offset > raw.Length || raw.Length - offset < length) throw Error(message);
    }

    private static Stalker2FormatException Error(string message) => new($"S.T.A.L.K.E.R. 2 stash: {message}");
}
