using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayArchiveReaderTests
{
    [Fact]
    public void Lzhuf_test_encoder_round_trips_literals_through_the_ported_decoder()
    {
        var expected = Encoding.UTF8.GetBytes("synthetic compressed FAT entry");
        Assert.Equal(expected, XRayArchiveHeaderCodec.DecodeLzhuf(LzhufTestEncoder.Encode(expected)));
    }

    [Fact]
    public void Lzhuf_decoder_expands_a_back_reference()
    {
        const byte value = (byte)'A';

        Assert.Equal(
            [value, value, value, value],
            XRayArchiveHeaderCodec.DecodeLzhuf(LzhufTestEncoder.EncodeLiteralThenThreeByteMatch(value)));
    }

    [Fact]
    public void Lists_entries_and_reads_uncompressed_and_lzo_files_by_name()
    {
        var config = Encoding.UTF8.GetBytes("[items]\nname = fixture\n");
        var texture = Enumerable.Range(0, 160).Select(value => (byte)value).ToArray();
        var archiveBytes = BuildUncompressedArchive(
            [
                new ArchiveFile("gamedata/config/items.ltx", config),
                new ArchiveFile("gamedata\\textures\\ui\\icon.dds", texture, Compress: true),
            ],
            withMetadata: true);

        using var archive = XRayArchiveReader.Open(new MemoryStream(archiveBytes));

        Assert.Equal(
            ["gamedata/config/items.ltx", "gamedata/textures/ui/icon.dds"],
            archive.Entries.Select(entry => entry.Name));
        Assert.Equal(config, archive.ReadFile("GAMEDATA\\CONFIG\\ITEMS.LTX"));
        Assert.Equal(texture, archive.ReadFile("gamedata/textures/ui/icon.dds"));
        Assert.Throws<KeyNotFoundException>(() => archive.ReadFile("missing.ltx"));
    }

    [Fact]
    public void Reads_the_synthetic_archive_fixture_verified_by_the_python_oracle()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "xray-archive");
        using var manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixtureDirectory, "manifest.json")));
        var archiveName = manifest.RootElement.GetProperty("archive").GetString()
            ?? throw new InvalidDataException("Archive fixture name is missing.");
        using var archive = XRayArchiveReader.Open(
            new MemoryStream(File.ReadAllBytes(Path.Combine(fixtureDirectory, archiveName))));
        var expectedEntries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();

        Assert.Equal(
            expectedEntries.Select(entry => entry.GetProperty("name").GetString()),
            archive.Entries.Select(entry => entry.Name));

        foreach (var expectedEntry in expectedEntries)
        {
            var name = expectedEntry.GetProperty("name").GetString()
                ?? throw new InvalidDataException("Archive fixture entry name is missing.");
            var dataFile = expectedEntry.GetProperty("data").GetString()
                ?? throw new InvalidDataException("Archive fixture data file is missing.");
            var expected = File.ReadAllBytes(Path.Combine(fixtureDirectory, dataFile));
            var actual = archive.ReadFile(name);

            Assert.Equal(expectedEntry.GetProperty("size").GetInt32(), actual.Length);
            Assert.Equal(expected, actual);
            Assert.Equal(
                expectedEntry.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant());
        }
    }

    [Fact]
    public void Reads_a_cp1251_file_name()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var nameEncoding = Encoding.GetEncoding(1251);
        var data = "[items]\nname = fixture\n"u8.ToArray();
        var archiveBytes = BuildUncompressedArchive(
            [new ArchiveFile("gamedata/config/предметы.ltx", data, NameEncoding: nameEncoding)]);

        using var archive = XRayArchiveReader.Open(new MemoryStream(archiveBytes));

        Assert.Equal("gamedata/config/предметы.ltx", Assert.Single(archive.Entries).Name);
        Assert.Equal(data, archive.ReadFile("gamedata/config/предметы.ltx"));
    }

    [Fact]
    public void Reads_a_data_chunk_with_the_compressed_flag_set()
    {
        var data = "archive body"u8.ToArray();
        var archiveBytes = BuildUncompressedArchive(
            [new ArchiveFile("gamedata/config/flagged.ltx", data)],
            dataChunkType: 0x80000000);

        using var archive = XRayArchiveReader.Open(new MemoryStream(archiveBytes));

        Assert.Equal(data, archive.ReadFile("gamedata/config/flagged.ltx"));
    }

    [Fact]
    public void Reads_a_lzhuf_compressed_header()
    {
        var expected = Encoding.UTF8.GetBytes("synthetic compressed FAT entry");
        var archiveBytes = BuildLzhufArchive(
            new ArchiveFile("gamedata/config/compressed.ltx", expected));

        using var archive = XRayArchiveReader.Open(new MemoryStream(archiveBytes));

        Assert.Equal(expected, archive.ReadFile("gamedata/config/compressed.ltx"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_both_xray_scrambled_lzhuf_header_variants(bool worldWide)
    {
        var expected = Encoding.UTF8.GetBytes("synthetic scrambled FAT entry");
        var archiveBytes = BuildLzhufArchive(
            new ArchiveFile("gamedata/config/scrambled.ltx", expected),
            worldWide);

        using var archive = XRayArchiveReader.Open(new MemoryStream(archiveBytes));

        Assert.Equal(expected, archive.ReadFile("gamedata/config/scrambled.ltx"));
    }

    [Fact]
    public void Rejects_a_truncated_archive_chunk()
    {
        using var stream = new MemoryStream([1, 0, 0, 0, 16, 0, 0, 0, 1, 2]);

        Assert.Throws<XRayFormatException>(() => XRayArchiveReader.Open(stream));
    }

    [Fact]
    public void Rejects_a_file_with_a_bad_crc()
    {
        var archiveBytes = BuildUncompressedArchive(
            [new ArchiveFile("gamedata/config/bad.ltx", "bad crc"u8.ToArray())],
            corruptFirstCrc: true);
        using var archive = XRayArchiveReader.Open(new MemoryStream(archiveBytes));

        Assert.Throws<XRayFormatException>(() => archive.ReadFile("gamedata/config/bad.ltx"));
    }

    [Fact]
    public void Rejects_an_entry_that_points_outside_the_data_chunk()
    {
        var archiveBytes = BuildUncompressedArchive(
            [new ArchiveFile("gamedata/config/outside.ltx", "outside"u8.ToArray())],
            invalidFirstOffset: true);

        Assert.Throws<XRayFormatException>(() => XRayArchiveReader.Open(new MemoryStream(archiveBytes)));
    }

    private static byte[] BuildUncompressedArchive(
        IReadOnlyList<ArchiveFile> files,
        bool withMetadata = false,
        bool corruptFirstCrc = false,
        bool invalidFirstOffset = false,
        uint dataChunkType = 0)
    {
        var storedFiles = files.Select(file =>
        {
            var stored = file.Compress ? Lzo1xCodec.Compress(file.Data) : file.Data;
            return (file.Name, Encoding: file.NameEncoding ?? Encoding.UTF8, file.Data, Stored: stored);
        }).ToArray();
        var metadata = withMetadata
            ? Chunk(666, Encoding.UTF8.GetBytes("[header]\nentry_point = $fs_root$\\gamedata\\\n"))
            : [];
        var headerSize = storedFiles.Sum(file => 14 + file.Encoding.GetByteCount(file.Name) + 4);
        var dataStart = checked((uint)(metadata.Length + 8 + headerSize + 8));
        using var header = new MemoryStream(headerSize);
        using var data = new MemoryStream();
        for (var index = 0; index < storedFiles.Length; index++)
        {
            var file = storedFiles[index];
            var name = file.Encoding.GetBytes(file.Name);
            WriteUInt16(header, checked((ushort)(16 + name.Length)));
            WriteUInt32(header, checked((uint)file.Data.Length));
            WriteUInt32(header, checked((uint)file.Stored.Length));
            var crc = Crc32(file.Data);
            WriteUInt32(header, corruptFirstCrc && index == 0 ? crc ^ 1 : crc);
            header.Write(name);
            var offset = checked(dataStart + (uint)data.Position);
            WriteUInt32(header, invalidFirstOffset && index == 0 ? offset + 1 : offset);
            data.Write(file.Stored);
        }

        return [.. metadata, .. Chunk(1, header.ToArray()), .. Chunk(dataChunkType, data.ToArray())];
    }

    private static byte[] BuildLzhufArchive(ArchiveFile file, bool? worldWide = null)
    {
        var stored = file.Compress ? Lzo1xCodec.Compress(file.Data) : file.Data;
        var dataStart = sizeof(uint) * 2;
        using var header = new MemoryStream();
        var name = Encoding.UTF8.GetBytes(file.Name);
        WriteUInt16(header, checked((ushort)(16 + name.Length)));
        WriteUInt32(header, checked((uint)file.Data.Length));
        WriteUInt32(header, checked((uint)stored.Length));
        WriteUInt32(header, Crc32(file.Data));
        header.Write(name);
        WriteUInt32(header, checked((uint)dataStart));

        var encoded = LzhufTestEncoder.Encode(header.ToArray());
        if (worldWide is not null)
        {
            encoded = XRayScrambleTestEncoder.Encrypt(encoded, worldWide.Value);
        }

        return [.. Chunk(0, stored), .. Chunk(0x80000001, encoded)];
    }

    private static byte[] Chunk(uint type, byte[] body)
    {
        var result = new byte[sizeof(uint) * 2 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, type);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sizeof(uint)), checked((uint)body.Length));
        body.CopyTo(result, sizeof(uint) * 2);
        return result;
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    private sealed record ArchiveFile(
        string Name,
        byte[] Data,
        bool Compress = false,
        Encoding? NameEncoding = null);
}
