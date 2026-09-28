using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Codecs;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public sealed class Stalker2FormatException(string message, Exception? innerException = null)
    : FormatException(message, innerException);

public static class Stalker2SaveReader
{
    public const int MaximumUnpackedSize = KrakenCodec.MaximumUnpackedSize;
    private static readonly byte[] WalletAnchor =
    [
        0x00, 0x38, 0x01, 0x00, 0x00, 0x00, 0x01, 0x10,
        0xCA, 0xCF, 0xA8, 0x48, 0xC8, 0x95, 0x21, 0x49,
        0xB5, 0x1B, 0x94, 0x44, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00,
    ];

    public static Stalker2Save FromBytes(ReadOnlySpan<byte> data)
    {
        var container = ReadContainer(data);
        if (container.MoneyAnchorCount != 1)
        {
            throw Error($"Wallet anchor occurs {container.MoneyAnchorCount} times; expected exactly one.");
        }

        var layout = Stalker2InventoryReader.LocateLayout(container.Raw);
        var inventory = Stalker2InventoryReader.Read(container.Raw, layout);
        var moneyOffset = layout.MoneyOffset;
        var money = BinaryPrimitives.ReadUInt32LittleEndian(container.Raw.AsSpan(moneyOffset));
        return new Stalker2Save(
            container.Original,
            container.Raw,
            container.StoredCrc32,
            container.ComputedCrc32,
            container.PackedSize,
            money,
            container.MoneyAnchorCount,
            inventory);
    }

    public static bool Detect(ReadOnlySpan<byte> data)
    {
        try
        {
            return ReadContainer(data).MoneyAnchorCount == 1;
        }
        catch (Stalker2FormatException)
        {
            return false;
        }
    }

    internal static byte[] GetWalletAnchor() => WalletAnchor.ToArray();

    /// <summary>Unpacks any S2 container file (campaign index, thumbnails) after its CRC check.</summary>
    internal static byte[] Unpack(ReadOnlySpan<byte> data) => ReadContainer(data).Raw;

    private static ContainerData ReadContainer(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw Error("Container is shorter than the size and CRC fields.");
        }

        var snapshot = data.ToArray();
        var body = snapshot.AsSpan(0, snapshot.Length - sizeof(uint));
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.AsSpan(snapshot.Length - sizeof(uint)));
        var computedCrc = Crc32(body);
        if (storedCrc != computedCrc)
        {
            throw Error($"CRC32 mismatch: stored={storedCrc:x8}, computed={computedCrc:x8}.");
        }

        var unpackedSize = BinaryPrimitives.ReadUInt32LittleEndian(snapshot);
        if (unpackedSize == 0 || unpackedSize > MaximumUnpackedSize)
        {
            throw Error($"Invalid unpacked size {unpackedSize}.");
        }

        var stream = snapshot.AsSpan(sizeof(uint), snapshot.Length - 2 * sizeof(uint));
        byte[] raw;
        try
        {
            raw = KrakenCodec.Decompress(stream, checked((int)unpackedSize));
        }
        catch (InvalidDataException exception)
        {
            throw Error($"Kraken/Oodle decompression failed: {exception.Message}", exception);
        }

        var anchorCount = CountOccurrences(raw, WalletAnchor);
        return new ContainerData(
            snapshot,
            raw,
            storedCrc,
            computedCrc,
            snapshot.Length,
            anchorCount);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static int CountOccurrences(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        var count = 0;
        var offset = 0;
        while (offset <= data.Length - needle.Length)
        {
            var relative = data[offset..].IndexOf(needle);
            if (relative < 0) break;
            count++;
            offset += relative + 1;
        }

        return count;
    }

    private static Stalker2FormatException Error(string message, Exception? innerException = null) =>
        new($"S.T.A.L.K.E.R. 2 save: {message}", innerException);

    private sealed record ContainerData(
        byte[] Original,
        byte[] Raw,
        uint StoredCrc32,
        uint ComputedCrc32,
        int PackedSize,
        int MoneyAnchorCount);
}

public sealed class Stalker2Save
{
    internal Stalker2Save(
        byte[] original,
        byte[] raw,
        uint storedCrc32,
        uint computedCrc32,
        int packedSize,
        uint money,
        int moneyAnchorCount,
        Stalker2InventoryResult inventory)
    {
        Original = original;
        Raw = raw;
        StoredCrc32 = storedCrc32;
        ComputedCrc32 = computedCrc32;
        PackedSize = packedSize;
        Money = money;
        MoneyAnchorCount = moneyAnchorCount;
        Layout = inventory.Layout;
        Inventory = inventory.Items;
        Orphans = inventory.Orphans;
        UnresolvedHandles = inventory.UnresolvedHandles;
        Warnings = inventory.Warnings;
        NameTables = inventory.NameTables;
    }

    public string FormatId => "stalker2";

    public string ReleaseId => "stalker2";

    public int? FormatVersion => null;

    public int? ContainerVersion => null;

    public int PackedSize { get; }

    public int UnpackedSize => Raw.Length;

    public string Sha256 => Convert.ToHexString(SHA256.HashData(Original.Span)).ToLowerInvariant();

    public uint StoredCrc32 { get; }

    public uint ComputedCrc32 { get; }

    public bool CrcOk => StoredCrc32 == ComputedCrc32;

    public uint Money { get; }

    public int MoneyAnchorCount { get; }

    public Stalker2InventoryLayout Layout { get; }

    public IReadOnlyList<uint> OwnedHandles => Layout.OwnedHandles;

    public int GridCellCount => Layout.DeclaredGridCount;

    public int GridHandleCount => Layout.GridHandleCount;

    public IReadOnlyList<Stalker2InventoryItem> Inventory { get; }

    public IReadOnlyList<Stalker2OrphanItem> Orphans { get; }

    public IReadOnlyList<uint> UnresolvedHandles { get; }

    public IReadOnlyList<string> Warnings { get; }

    public Stalker2NameTables? NameTables { get; }

    public ReadOnlyMemory<byte> Original { get; }

    public ReadOnlyMemory<byte> Raw { get; }
}
