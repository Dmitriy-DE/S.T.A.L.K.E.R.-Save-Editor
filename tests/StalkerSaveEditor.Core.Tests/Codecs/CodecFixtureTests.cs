using System.Buffers.Binary;
using System.Text.Json;
using StalkerSaveEditor.Core.Codecs;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Codecs;

public sealed class CodecFixtureTests
{
    [Fact]
    public void Lzo1x_matches_python_bytes_for_every_xray_fixture()
    {
        using var manifest = ReadManifest();
        var vectorArray = manifest.RootElement.GetProperty("vectors");
        var vectors = vectorArray.EnumerateArray();
        var expectedCount = vectorArray.EnumerateArray()
            .Count(vector => GetString(vector, "codec") == "lzo1x");
        var count = 0;

        foreach (var vector in vectors)
        {
            if (GetString(vector, "codec") != "lzo1x")
            {
                continue;
            }

            var container = ReadFixture(GetString(vector, "container"));
            var expected = ReadFixture(GetString(vector, "raw"));
            var streamOffset = vector.GetProperty("streamOffset").GetInt32();
            var streamLength = vector.GetProperty("streamLength").GetInt32();
            var unpackedSize = vector.GetProperty("unpackedSize").GetInt32();
            var stream = container.AsSpan(streamOffset, streamLength);

            Assert.Equal((uint)unpackedSize, BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(8, 4)));
            Assert.Equal(expected.Length, unpackedSize);
            Assert.Equal(expected, Lzo1xCodec.Decompress(stream, unpackedSize));
            Assert.Equal(stream.ToArray(), Lzo1xCodec.Compress(expected));
            count++;
        }

        Assert.Equal(expectedCount, count);
    }

    [Fact]
    public void Lzo1x_compressor_matches_python_literal_vectors_at_length_boundaries()
    {
        using var manifest = ReadManifest();
        var vectors = manifest.RootElement.GetProperty("vectors").EnumerateArray();
        var count = 0;

        foreach (var vector in vectors)
        {
            if (GetString(vector, "codec") != "lzo1x-literal")
            {
                continue;
            }

            var stream = ReadFixture(GetString(vector, "stream"));
            var expected = ReadFixture(GetString(vector, "raw"));
            var unpackedSize = vector.GetProperty("unpackedSize").GetInt32();

            Assert.Equal(expected.Length, unpackedSize);
            Assert.Equal(stream.Length, vector.GetProperty("streamLength").GetInt32());
            Assert.Equal(stream, Lzo1xCodec.Compress(expected));
            Assert.Equal(expected, Lzo1xCodec.Decompress(stream, unpackedSize));
            count++;
        }

        Assert.Equal(5, count);
    }

    [Fact]
    public void Lzo1x_decodes_python_extended_m4_match_vector()
    {
        using var manifest = ReadManifest();
        var vectors = manifest.RootElement.GetProperty("vectors").EnumerateArray();
        var vector = Assert.Single(vectors, item => GetString(item, "codec") == "lzo1x-match");
        var stream = ReadFixture(GetString(vector, "stream"));
        var expected = ReadFixture(GetString(vector, "raw"));
        var unpackedSize = vector.GetProperty("unpackedSize").GetInt32();

        Assert.Equal(stream.Length, vector.GetProperty("streamLength").GetInt32());
        Assert.Equal(expected, Lzo1xCodec.Decompress(stream, unpackedSize));
    }

    [Fact]
    public void Kraken_matches_python_bytes_and_round_trips_native_compression()
    {
        using var manifest = ReadManifest();
        var vectors = manifest.RootElement.GetProperty("vectors").EnumerateArray();
        var vector = Assert.Single(vectors, item => GetString(item, "codec") == "kraken");
        var container = ReadFixture(GetString(vector, "container"));
        var expected = ReadFixture(GetString(vector, "raw"));
        var streamOffset = vector.GetProperty("streamOffset").GetInt32();
        var streamLength = vector.GetProperty("streamLength").GetInt32();
        var unpackedSize = vector.GetProperty("unpackedSize").GetInt32();

        Assert.Equal((uint)unpackedSize, BinaryPrimitives.ReadUInt32LittleEndian(container));
        Assert.Equal(expected.Length, unpackedSize);
        Assert.Equal(expected, KrakenCodec.Decompress(container.AsSpan(streamOffset, streamLength), unpackedSize));

        var compressed = KrakenCodec.Compress(expected);
        Assert.Equal(expected, KrakenCodec.Decompress(compressed, unpackedSize));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(536870913)]
    public void Lzo1x_rejects_invalid_unpacked_sizes(int unpackedSize)
    {
        Assert.Throws<InvalidDataException>(
            () => Lzo1xCodec.Decompress(new byte[] { 0x11, 0, 0 }, unpackedSize));
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(ReadFixture("fixture-vectors.json"));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Fixture property '{property}' is missing.");
}
