using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayTrilogyReaderTests
{
    [Theory]
    [InlineData("xray-soc.sav", "stalker-soc", 3, 118)]
    [InlineData("xray-clear-sky.sav", "stalker-cs", 5, 124)]
    [InlineData("xray-call-of-pripyat.sav", "stalker-cop", 6, 128)]
    public void Reads_actor_and_inventory_with_python_golden_parity(
        string fixtureName,
        string expectedFormat,
        uint expectedContainerVersion,
        int expectedActorVersion)
    {
        var data = ReadFixture(fixtureName);
        var parsed = XRayTrilogyReader.FromBytes(data);

        Assert.Equal(expectedFormat, parsed.FormatId);
        Assert.Equal(expectedActorVersion, parsed.ActorVersion);
        Assert.Equal(1234u, parsed.Money);
        Assert.Equal(1.0f, parsed.ActorHealth);
        Assert.Equal(-1, parsed.ActorRank);
        Assert.Equal(-1, parsed.ActorReputation);
        Assert.Null(parsed.ActorName);
        var item = Assert.Single(parsed.Inventory);
        Assert.Equal((ushort)0x1234, item.Handle);
        Assert.Equal("ammo_9x39_pab9", item.TypeKey);
        Assert.Equal((ushort)30, item.Count);
        Assert.True(item.EditableCount);

        using var golden = ReadGolden();
        var expected = Assert.Single(
            golden.RootElement.GetProperty("samples").EnumerateArray(),
            sample => GetString(sample, "sha256") == Sha256(data));
        Assert.Equal("ok", GetString(expected, "parse_status"));
        Assert.Equal(expectedFormat, GetString(expected, "format"));
        Assert.Equal(expectedContainerVersion,
            expected.GetProperty("versions").GetProperty("container").GetUInt32());
        Assert.Equal(expectedActorVersion,
            expected.GetProperty("versions").GetProperty("format").GetInt32());
        Assert.Equal(1234u, expected.GetProperty("state").GetProperty("money").GetUInt32());
        AssertJsonEquivalent(
            expected.GetProperty("state").GetProperty("character"),
            JsonSerializer.SerializeToElement(ToGoldenCharacter(parsed)));
        AssertJsonEquivalent(
            expected.GetProperty("state").GetProperty("items"),
            JsonSerializer.SerializeToElement(ToGoldenItems(parsed)));
        AssertJsonEquivalent(
            expected.GetProperty("state").GetProperty("time"),
            JsonSerializer.SerializeToElement(new
            {
                game_time = parsed.GameTime,
                level_name = (string?)null,
                normal_time_factor = parsed.NormalTimeFactor,
                time_factor = parsed.TimeFactor,
            }));
    }

    [Theory]
    [InlineData("xray-soc-ee.sav")]
    [InlineData("xray-clear-sky-ee.sav")]
    [InlineData("xray-call-of-pripyat-ee.sav")]
    public void Does_not_misidentify_enhanced_editions_as_original_games(string fixtureName)
    {
        Assert.Throws<XRayFormatException>(() => XRayTrilogyReader.FromBytes(ReadFixture(fixtureName)));
    }

    [Fact]
    public void Reads_unknown_actor_owned_item_as_read_only_without_inventing_a_count()
    {
        var data = ReadFixture("xray-call-of-pripyat-base-item.sav");
        var parsed = XRayTrilogyReader.FromBytes(data);
        var item = Assert.Single(parsed.Inventory);

        Assert.Equal("bandage_existing", item.TypeKey);
        Assert.Null(item.Count);
        Assert.False(item.EditableCount);
        Assert.Equal(Array.Empty<string>(), item.Upgrades);

        using var golden = ReadGolden();
        var expected = Assert.Single(
            golden.RootElement.GetProperty("samples").EnumerateArray(),
            sample => GetString(sample, "sha256") == Sha256(data));
        AssertJsonEquivalent(
            expected.GetProperty("state").GetProperty("items"),
            JsonSerializer.SerializeToElement(ToGoldenItems(parsed)));
    }

    [Fact]
    public void Rejects_invalid_or_unsupported_input()
    {
        Assert.Throws<XRayFormatException>(() => XRayTrilogyReader.FromBytes([]));
        Assert.Throws<XRayFormatException>(() => XRayTrilogyReader.FromBytes(
            ReadFixture("synthetic-s2.sav")));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    public void Rejects_empty_or_truncated_object_registry(uint objectCount)
    {
        var container = XRayContainer.FromBytes(ReadFixture("xray-soc.sav"));
        var raw = container.Raw.ToArray();
        var objectChunk = Assert.Single(container.Chunks, chunk => chunk.Type == 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            raw.AsSpan(objectChunk.Offset + 8), objectCount);
        var brokenContainer = Repack(container.Version, raw);

        Assert.Throws<XRayFormatException>(() => XRayTrilogyReader.FromBytes(brokenContainer));
    }

    private static object ToGoldenCharacter(XRayTrilogySave parsed) => new
    {
        faction_relations = Array.Empty<object>(),
        health = parsed.ActorHealth,
        name = parsed.ActorName,
        player_faction_editable = parsed.PlayerFactionIndex.HasValue,
        player_faction_index = parsed.PlayerFactionIndex,
        rank = parsed.ActorRank,
        reputation = parsed.ActorReputation,
    };

    private static object[] ToGoldenItems(XRayTrilogySave parsed) => parsed.Inventory
        .Select(item => (object)new
        {
            condition = item.Condition,
            count = item.Count,
            handle = item.Handle,
            handle_hex = $"0x{item.Handle:X8}",
            placement = new
            {
                base_slot = (int?)null,
                cells = Array.Empty<object>(),
                height = (int?)null,
                slot = (int?)null,
                storage = (string?)null,
                type = (string?)null,
                width = (int?)null,
                x = (int?)null,
                y = (int?)null,
            },
            sid_or_section = item.TypeKey,
            type_key = item.TypeKey,
            upgrades = item.Upgrades,
        })
        .ToArray();

    private static JsonDocument ReadGolden() => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "fixture-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static byte[] Repack(uint version, byte[] raw)
    {
        var compressed = StalkerSaveEditor.Core.Codecs.Lzo1xCodec.Compress(raw);
        var result = new byte[12 + compressed.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result, XRayContainer.Signature);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), version);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)raw.Length));
        compressed.CopyTo(result, 12);
        return result;
    }

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Fixture property '{property}' is missing.");

    private static void AssertJsonEquivalent(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProperties = expected.EnumerateObject().ToDictionary(property => property.Name);
                var actualProperties = actual.EnumerateObject().ToDictionary(property => property.Name);
                Assert.Equal(expectedProperties.Keys.Order(), actualProperties.Keys.Order());
                foreach (var (name, expectedValue) in expectedProperties)
                {
                    AssertJsonEquivalent(expectedValue.Value, actualProperties[name].Value);
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
                Assert.Equal(expected.GetDecimal(), actual.GetDecimal());
                break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual.GetString());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), actual.GetBoolean());
                break;
            case JsonValueKind.Null:
                break;
            default:
                throw new InvalidDataException($"Unexpected JSON kind: {expected.ValueKind}.");
        }
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
