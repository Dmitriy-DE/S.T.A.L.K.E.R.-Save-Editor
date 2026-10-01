using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.Stalker2;

public sealed class Stalker2SaveReaderTests
{
    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("a", 0xE8B7BE43u)]
    [InlineData("123456789", 0xCBF43926u)]
    [InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
    public void Crc32_matches_the_standard_check_values(string text, uint expected) =>
        Assert.Equal(expected, Stalker2SaveReader.Crc32(System.Text.Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void Crc32_gives_the_same_result_for_every_length_and_alignment()
    {
        var data = new byte[257];
        new Random(7).NextBytes(data);
        for (var start = 0; start < 9; start++)
        {
            for (var length = 0; length <= data.Length - start; length += 3)
            {
                var slice = data.AsSpan(start, length);
                var crc = uint.MaxValue;
                foreach (var value in slice)
                {
                    crc ^= value;
                    for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }

                Assert.Equal(~crc, Stalker2SaveReader.Crc32(slice));
            }
        }
    }

    [Fact]
    public void Reads_python_synthetic_save_and_matches_all_reader_golden_fields()
    {
        var data = ReadFixture("synthetic-s2.sav");
        var parsed = Stalker2SaveReader.FromBytes(data);

        Assert.True(Stalker2SaveReader.Detect(data));
        Assert.Equal("stalker2", parsed.FormatId);
        Assert.Equal(100u, parsed.Money);
        Assert.Equal(1, parsed.MoneyAnchorCount);
        Assert.True(parsed.CrcOk);
        Assert.Equal(parsed.StoredCrc32, parsed.ComputedCrc32);
        Assert.Equal(4, parsed.OwnedHandles.Count);
        Assert.Equal(2, parsed.GridCellCount);
        Assert.Equal(2, parsed.GridHandleCount);
        Assert.Equal(new uint[] { 0x30000004 }, parsed.UnresolvedHandles);
        Assert.Equal(2, parsed.Inventory.Count);
        Assert.Equal(2, parsed.Orphans.Count);
        Assert.Equal(0x30000003u, parsed.Orphans[0].Handle);
        Assert.Equal(0x30000004u, parsed.Orphans[1].Handle);
        Assert.Equal((byte)99, parsed.Orphans[1].KindCode);
        Assert.Null(parsed.NameTables);

        using var golden = ReadGolden();
        var expected = Assert.Single(
            golden.RootElement.GetProperty("samples").EnumerateArray(),
            sample => GetString(sample, "sha256") == Sha256(data));
        Assert.Equal("ok", GetString(expected, "parse_status"));
        Assert.Equal("stalker2", GetString(expected, "release"));
        Assert.Equal("stalker2", GetString(expected, "format"));
        Assert.Equal(JsonValueKind.Null, expected.GetProperty("versions").GetProperty("format").ValueKind);
        Assert.Equal(JsonValueKind.Null, expected.GetProperty("versions").GetProperty("container").ValueKind);

        var expectedState = expected.GetProperty("state");
        Assert.Equal(100u, expectedState.GetProperty("money").GetUInt32());
        AssertJsonEquivalent(
            expectedState.GetProperty("items"),
            JsonSerializer.SerializeToElement(ToGoldenItems(parsed)));
        AssertJsonEquivalent(
            expectedState.GetProperty("warnings"),
            JsonSerializer.SerializeToElement(parsed.Warnings));
        AssertJsonEquivalent(
            expectedState.GetProperty("integrity"),
            JsonSerializer.SerializeToElement(new
            {
                name = "CRC-32",
                crc_present = true,
                crc_ok = parsed.CrcOk,
            }));
        AssertJsonEquivalent(
            expectedState.GetProperty("character"),
            JsonSerializer.SerializeToElement(new
            {
                name = (string?)null,
                health = (float?)null,
                rank = (int?)null,
                reputation = (int?)null,
                player_faction_index = (int?)null,
                player_faction_editable = false,
                faction_relations = Array.Empty<object>(),
            }));
        AssertJsonEquivalent(
            expectedState.GetProperty("time"),
            JsonSerializer.SerializeToElement(new
            {
                game_time = (ulong?)null,
                time_factor = (float?)null,
                normal_time_factor = (float?)null,
                level_name = (string?)null,
            }));
    }

    [Fact]
    public void Resolves_chained_save_local_name_tables_without_treating_keys_as_sids()
    {
        var raw = ReadFixture("synthetic-s2.raw");
        var itemOffset = FindObjectRecord(raw, 0x30000001);
        raw[itemOffset + 8] = 4;
        raw[itemOffset + 9] = 1;
        raw[itemOffset + 10] = 0;
        raw = AppendNameTable(raw, "GunAK74_ST", "Bandage");
        raw = AppendNameTable(raw, "ModuleNames", "Medkit");

        var tables = Stalker2NameTableReader.Locate(
            raw,
            [new byte[] { 4, 1, 0 }, new byte[] { 5, 1, 0 }]);

        Assert.NotNull(tables);
        Assert.Equal("Bandage", tables!.Resolve([4, 1, 0]));
        Assert.Equal("Medkit", tables.Resolve([5, 1, 0]));
        Assert.Null(tables.Resolve([4, 255, 127]));

        var reparsed = Stalker2SaveReader.FromBytes(BuildContainer(raw));
        var item = Assert.Single(reparsed.Inventory, value => value.Handle == 0x30000001);
        Assert.Equal("040100", item.TypeKey);
        Assert.Equal("Bandage", item.DisplayName);
    }

    [Fact]
    public void Keeps_unknown_name_keys_unresolved_and_rejects_invalid_table_bytes()
    {
        var raw = ReadFixture("synthetic-s2.raw");
        var itemOffset = FindObjectRecord(raw, 0x30000001);
        raw[itemOffset + 8] = 4;
        raw[itemOffset + 9] = 0xFF;
        raw[itemOffset + 10] = 0x7F;
        raw = AppendNameTable(raw, "GunAK74_ST", "Bandage");

        Assert.Null(Stalker2NameTableReader.Locate(raw, [new byte[] { 4, 0xFF, 0x7F }]));
        var parsed = Stalker2SaveReader.FromBytes(BuildContainer(raw));
        var item = Assert.Single(parsed.Inventory, value => value.Handle == 0x30000001);
        Assert.Equal("04ff7f", item.TypeKey);
        Assert.Null(item.DisplayName);
        Assert.Null(parsed.NameTables);

        var invalid = AppendInvalidNameTable(ReadFixture("synthetic-s2.raw"));
        Assert.Null(Stalker2NameTableReader.Locate(invalid));
        var nonPrintable = AppendNameTable(ReadFixture("synthetic-s2.raw"), "GunAK74_ST", "\u200B");
        Assert.Null(Stalker2NameTableReader.Locate(nonPrintable));
    }

    [Fact]
    public void Reads_stash_layout_from_the_python_synthetic_fixture()
    {
        var raw = ReadFixture("synthetic-s2-stash.raw");
        var parsed = Stalker2StashReader.Locate(raw);

        Assert.Equal(new uint[] { uint.MaxValue, 0x30000010 }, parsed.OwnedHandles);
        Assert.Equal(new uint[] { 0x30000010 }, parsed.LiveHandles);
        Assert.Equal(new[]
        {
            new Stalker2GridCell(0x30000010, 3, 0),
            new Stalker2GridCell(0x30000010, 3, 1),
        }, parsed.GridCells);
        Assert.True(parsed.GridEndOffset > parsed.OwnedCountOffset);
    }

    [Fact]
    public void Rejects_original_enhanced_and_garbage_containers()
    {
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2SaveReader.FromBytes(ReadFixture("xray-soc.sav")));
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2SaveReader.FromBytes(ReadFixture("xray-soc-ee.sav")));
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2SaveReader.FromBytes("not a save"u8));
        Assert.False(Stalker2SaveReader.Detect(ReadFixture("xray-call-of-pripyat.sav")));
        Assert.False(Stalker2SaveReader.Detect(ReadFixture("xray-call-of-pripyat-ee.sav")));
        Assert.False(Stalker2SaveReader.Detect("garbage"u8));
    }

    [Fact]
    public void Rejects_bad_crc_and_non_unique_wallet_anchor()
    {
        var original = ReadFixture("synthetic-s2.sav");
        var badCrc = original.ToArray();
        badCrc[^1] ^= 0x01;
        Assert.Throws<Stalker2FormatException>(() => Stalker2SaveReader.FromBytes(badCrc));
        Assert.False(Stalker2SaveReader.Detect(badCrc));

        var zeroLength = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(zeroLength.AsSpan(4), Crc32(zeroLength.AsSpan(0, 4)));
        Assert.Throws<Stalker2FormatException>(() => Stalker2SaveReader.FromBytes(zeroLength));

        var raw = ReadFixture("synthetic-s2.raw");
        var duplicateAnchor = raw.Concat(MoneyAnchor).ToArray();
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2SaveReader.FromBytes(BuildContainer(duplicateAnchor)));
    }

    [Fact]
    public void Refuses_missing_or_ambiguous_stash_headers_and_truncated_arrays()
    {
        var noStash = ReadFixture("synthetic-s2.raw");
        Assert.Throws<Stalker2FormatException>(() => Stalker2StashReader.Locate(noStash));

        var stash = ReadFixture("synthetic-s2-stash.raw");
        var marker = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x06, 0x01, 0, 0, 0, 0x06 };
        var duplicate = stash.Concat(marker).ToArray();
        Assert.Throws<Stalker2FormatException>(() => Stalker2StashReader.Locate(duplicate));

        var stashLayout = Stalker2StashReader.Locate(stash);
        var truncated = stash.AsSpan(0, stashLayout.GridEndOffset - 1).ToArray();
        Assert.Throws<Stalker2FormatException>(() => Stalker2StashReader.Locate(truncated));

        var inconsistent = stash.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            inconsistent.AsSpan(stashLayout.GridEndOffset - 2 * 8),
            0x30000011);
        Assert.Throws<Stalker2FormatException>(() => Stalker2StashReader.Locate(inconsistent));
    }

    [Fact]
    public void Rejects_suspicious_inventory_counts_before_reading_arrays()
    {
        var raw = ReadFixture("synthetic-s2.raw");
        var layout = Stalker2InventoryReader.LocateLayout(raw);

        var suspiciousOwned = raw.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            suspiciousOwned.AsSpan(layout.OwnedCountOffset),
            ushort.MaxValue);
        Assert.Throws<Stalker2FormatException>(() => Stalker2InventoryReader.LocateLayout(suspiciousOwned));

        var suspiciousGrid = raw.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            suspiciousGrid.AsSpan(layout.GridCountOffset),
            8193);
        Assert.Throws<Stalker2FormatException>(() => Stalker2InventoryReader.LocateLayout(suspiciousGrid));
    }

    private static object[] ToGoldenItems(Stalker2Save parsed) => parsed.Inventory
        .OrderBy(item => item.Handle)
        .Select(item => (object)new
        {
            handle = item.Handle,
            handle_hex = $"0x{item.Handle:X8}",
            type_key = item.TypeKey,
            sid_or_section = item.TypeKey,
            count = item.Count,
            condition = (float?)null,
            placement = new
            {
                x = item.X,
                y = item.Y,
                width = item.Width,
                height = item.Height,
                cells = item.Cells.Select(cell => new[] { (int)cell.X, (int)cell.Y }).ToArray(),
                storage = "inventory",
                type = (string?)null,
                slot = (int?)null,
                base_slot = (int?)null,
            },
            upgrades = (string[]?)null,
        })
        .ToArray();

    private static byte[] AppendNameTable(byte[] raw, params string[] names)
    {
        using var output = new MemoryStream();
        output.Write(raw);
        Span<byte> number = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(number, checked((ushort)names.Length));
        output.Write(number);
        foreach (var name in names)
        {
            var value = System.Text.Encoding.UTF8.GetBytes(name);
            BinaryPrimitives.WriteUInt16LittleEndian(number, checked((ushort)value.Length));
            output.Write(number);
            output.Write(value);
        }

        return output.ToArray();
    }

    private static byte[] AppendInvalidNameTable(byte[] raw)
    {
        using var output = new MemoryStream();
        output.Write(raw);
        Span<byte> number = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(number, 2);
        output.Write(number);
        BinaryPrimitives.WriteUInt16LittleEndian(number, 10);
        output.Write(number);
        output.Write("GunAK74_ST"u8);
        BinaryPrimitives.WriteUInt16LittleEndian(number, 1);
        output.Write(number);
        output.WriteByte(0xFF);
        return output.ToArray();
    }

    private static int FindObjectRecord(ReadOnlySpan<byte> raw, uint handle)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(needle, handle);
        var start = 0;
        while (start <= raw.Length - needle.Length)
        {
            var relative = raw[start..].IndexOf(needle);
            if (relative < 0) break;
            var offset = start + relative;
            if (offset + 36 <= raw.Length && raw[offset + 18] == 0x38) return offset;
            start = offset + 1;
        }

        throw new InvalidDataException($"Synthetic item handle 0x{handle:X8} was not found.");
    }

    private static byte[] BuildContainer(byte[] raw)
    {
        var compressed = KrakenCodec.Compress(raw);
        var output = new byte[sizeof(uint) + compressed.Length + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(output, checked((uint)raw.Length));
        compressed.CopyTo(output, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(output.Length - sizeof(uint)), Crc32(output.AsSpan(0, output.Length - sizeof(uint))));
        return output;
    }

    private static readonly byte[] MoneyAnchor =
    [
        0x00, 0x38, 0x01, 0x00, 0x00, 0x00, 0x01, 0x10,
        0xCA, 0xCF, 0xA8, 0x48, 0xC8, 0x95, 0x21, 0x49,
        0xB5, 0x1B, 0x94, 0x44, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00,
    ];

    private static uint Crc32(ReadOnlySpan<byte> value)
    {
        var crc = uint.MaxValue;
        foreach (var item in value)
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static JsonDocument ReadGolden() => JsonDocument.Parse(
        ReadFile(Path.Combine("golden", "fixture-vectors.json")));

    private static byte[] ReadFixture(string name) => ReadFile(Path.Combine("Fixtures", name));

    private static byte[] ReadFile(string relativePath) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, relativePath));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Fixture property '{property}' is missing.");

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertJsonEquivalent(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProperties = expected.EnumerateObject().ToArray();
                var actualProperties = actual.EnumerateObject().ToArray();
                Assert.Equal(expectedProperties.Length, actualProperties.Length);
                foreach (var property in expectedProperties)
                {
                    Assert.True(actual.TryGetProperty(property.Name, out var actualValue),
                        $"Missing JSON property '{property.Name}'.");
                    AssertJsonEquivalent(property.Value, actualValue);
                }

                break;
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToArray();
                var actualItems = actual.EnumerateArray().ToArray();
                Assert.Equal(expectedItems.Length, actualItems.Length);
                for (var index = 0; index < expectedItems.Length; index++)
                {
                    AssertJsonEquivalent(expectedItems[index], actualItems[index]);
                }

                break;
            case JsonValueKind.Number:
                Assert.Equal(expected.GetRawText(), actual.GetRawText());
                break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual.GetString());
                break;
            default:
                Assert.Equal(expected.GetRawText(), actual.GetRawText());
                break;
        }
    }
}
