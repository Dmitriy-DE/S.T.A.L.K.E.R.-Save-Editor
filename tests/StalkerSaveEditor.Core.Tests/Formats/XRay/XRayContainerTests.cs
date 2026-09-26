using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayContainerTests
{
    [Fact]
    public void Parses_python_xray_fixtures_and_matches_oracle_json()
    {
        using var manifest = JsonDocument.Parse(ReadFixture("fixture-vectors.json"));
        using var golden = ReadGolden();
        var vectors = manifest.RootElement.GetProperty("vectors").EnumerateArray();
        var xrayCount = 0;

        foreach (var vector in vectors)
        {
            if (GetString(vector, "codec") != "lzo1x")
            {
                continue;
            }

            var original = ReadFixture(GetString(vector, "container"));
            var expectedRaw = ReadFixture(GetString(vector, "raw"));
            var expected = vector.GetProperty("containerSummary");
            var parsed = XRayContainer.FromBytes(original);

            Assert.Equal(expectedRaw, parsed.Raw.ToArray());
            Assert.Equal(original, parsed.Original.ToArray());

            var actualSummary = new
            {
                magic = parsed.Magic,
                version = parsed.Version,
                unpackedSize = parsed.UnpackedSize,
                rawSha256 = Sha256(parsed.Raw.Span),
                chunkTypes = parsed.ChunkTypes,
                chunks = parsed.Chunks.Select(chunk => new
                {
                    type = chunk.Type,
                    offset = chunk.Offset,
                    size = chunk.Size,
                    dataSha256 = Sha256(chunk.Data.Span),
                }),
            };

            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actualSummary));

            var oracleSample = Assert.Single(
                golden.RootElement.GetProperty("samples").EnumerateArray(),
                sample => GetString(sample, "sha256") == Sha256(original));
            Assert.Equal("ok", GetString(oracleSample, "parse_status"));
            Assert.Equal(
                parsed.Version,
                oracleSample.GetProperty("versions").GetProperty("container").GetUInt32());
            xrayCount++;
        }

        Assert.Equal(6, xrayCount);
        Assert.Equal(6, golden.RootElement.GetProperty("samples").GetArrayLength());
    }

    [Fact]
    public void Rejects_invalid_header_and_compressed_payload()
    {
        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes([]));
        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes(new byte[11]));

        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes(
            BuildContainer(0, 3, 1, [0x11, 0, 0])));
        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes(
            BuildContainer(XRayContainer.Signature, 4, 1, [0x11, 0, 0])));
        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes(
            BuildContainer(XRayContainer.Signature, 3, 10, "bad"u8.ToArray())));
        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes(
            BuildContainer(XRayContainer.Signature, 3, 4, Lzo1xCodec.Compress("bad"u8))));
        Assert.Throws<XRayFormatException>(() => XRayContainer.FromBytes(
            BuildContainer(XRayContainer.Signature, 3, XRayContainer.MaximumUnpackedSize + 1, [])));
    }

    [Fact]
    public void Rejects_empty_and_truncated_chunk_tables()
    {
        Assert.Throws<XRayFormatException>(() => XRayContainer.ParseChunks([]));

        var truncatedChunk = new byte[10];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(truncatedChunk, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(truncatedChunk.AsSpan(4), 4);
        truncatedChunk[8] = 3;
        truncatedChunk[9] = 0;
        Assert.Throws<XRayFormatException>(() => XRayContainer.ParseChunks(truncatedChunk));
    }

    [Fact]
    public void Keeps_an_owned_copy_of_the_original_container()
    {
        var input = ReadFixture("xray-soc.sav");
        var expected = input.ToArray();

        var parsed = XRayContainer.FromBytes(input);
        input[0] = 0;

        Assert.Equal(expected, parsed.Original.ToArray());
    }

    private static byte[] BuildContainer(uint magic, uint version, uint unpackedSize, byte[] stream)
    {
        var output = new byte[12 + stream.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output, magic);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), version);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), unpackedSize);
        stream.CopyTo(output, 12);
        return output;
    }

    private static JsonDocument ReadGolden() => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "fixture-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Fixture property '{property}' is missing.");

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
