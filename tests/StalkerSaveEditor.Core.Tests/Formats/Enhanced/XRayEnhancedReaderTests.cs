using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.Enhanced;

public sealed class XRayEnhancedReaderTests
{
    [Theory]
    [InlineData("xray-soc-ee.sav", "stalker-soc-ee", 3, 51, 118)]
    [InlineData("xray-clear-sky-ee.sav", "stalker-cs-ee", 6, 54, 128)]
    [InlineData("xray-call-of-pripyat-ee.sav", "stalker-cop-ee", 6, 54, 128)]
    public void Classifies_each_enhanced_game_and_matches_python_golden(
        string fixtureName,
        string expectedFormat,
        uint expectedContainerVersion,
        uint expectedAlifeVersion,
        int expectedActorVersion)
    {
        var data = ReadFixture(fixtureName);
        var parsed = XRayEnhancedReader.FromBytes(data);
        var container = XRayContainer.FromBytes(data);
        var alifeChunk = Assert.Single(container.Chunks, chunk => chunk.Type == 0);
        var alifeVersion = BinaryPrimitives.ReadUInt32LittleEndian(alifeChunk.Data.Span);

        Assert.Equal(expectedFormat, parsed.FormatId);
        Assert.Equal(expectedContainerVersion, parsed.ContainerVersion);
        Assert.Equal(expectedAlifeVersion, alifeVersion);
        Assert.Equal(expectedActorVersion, parsed.ActorVersion);
        Assert.Equal(1234u, parsed.Money);
        Assert.Equal(1.0f, parsed.ActorHealth);
        Assert.Equal(-1, parsed.ActorRank);
        Assert.Equal(-1, parsed.ActorReputation);
        var item = Assert.Single(parsed.Inventory);
        var expectedItemKey = expectedFormat switch
        {
            "stalker-cs-ee" => "ammo_marsh_test",
            "stalker-cop-ee" => "ammo_zaton_test",
            _ => "ammo_9x39_pab9",
        };
        Assert.Equal(expectedItemKey, item.TypeKey);

        using var golden = ReadGolden();
        var expected = Assert.Single(
            golden.RootElement.GetProperty("samples").EnumerateArray(),
            sample => GetString(sample, "sha256") == Sha256(data));
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
    [InlineData("xray-soc.sav")]
    [InlineData("xray-clear-sky.sav")]
    [InlineData("xray-call-of-pripyat.sav")]
    public void Rejects_original_trilogy_saves(string fixtureName)
    {
        Assert.Throws<XRayFormatException>(() => XRayEnhancedReader.FromBytes(ReadFixture(fixtureName)));
    }

    [Fact]
    public void Rejects_clear_sky_enhanced_object_chunk_with_both_level_markers()
    {
        var ambiguous = AppendToObjectChunk(ReadFixture("xray-clear-sky-ee.sav"), "zaton"u8);

        Assert.Throws<XRayFormatException>(() => XRayEnhancedReader.FromBytes(ambiguous));
    }

    [Fact]
    public void Rejects_clear_sky_enhanced_object_chunk_without_either_level_marker()
    {
        var container = XRayContainer.FromBytes(ReadFixture("xray-clear-sky-ee.sav"));
        var raw = container.Raw.ToArray();
        var markerOffset = raw.AsSpan().IndexOf("marsh"u8);
        Assert.True(markerOffset >= 0);
        "other"u8.CopyTo(raw.AsSpan(markerOffset));

        Assert.Throws<XRayFormatException>(() => XRayEnhancedReader.FromBytes(Repack(container.Version, raw)));
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
                base_slot = item.PlacementBaseSlot,
                cells = Array.Empty<object>(),
                height = (int?)null,
                slot = item.PlacementSlot,
                storage = item.PlacementStorage,
                type = item.PlacementType,
                width = (int?)null,
                x = (int?)null,
                y = (int?)null,
            },
            sid_or_section = item.TypeKey,
            type_key = item.TypeKey,
            upgrades = item.Upgrades,
        })
        .ToArray();

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

    private static byte[] AppendToObjectChunk(byte[] data, ReadOnlySpan<byte> suffix)
    {
        var container = XRayContainer.FromBytes(data);
        var objectChunk = Assert.Single(container.Chunks, chunk => chunk.Type == 2);
        var raw = container.Raw.Span;
        var dataStart = objectChunk.Offset + 8;
        var oldEnd = dataStart + objectChunk.Size;
        var expanded = new byte[raw.Length + suffix.Length];
        raw[..oldEnd].CopyTo(expanded);
        suffix.CopyTo(expanded.AsSpan(oldEnd));
        raw[oldEnd..].CopyTo(expanded.AsSpan(oldEnd + suffix.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            expanded.AsSpan(objectChunk.Offset + 4),
            checked((uint)(objectChunk.Size + suffix.Length)));
        return Repack(container.Version, expanded);
    }

    private static byte[] Repack(uint version, byte[] raw)
    {
        var compressed = Lzo1xCodec.Compress(raw);
        var result = new byte[12 + compressed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, XRayContainer.Signature);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)raw.Length));
        compressed.CopyTo(result, 12);
        return result;
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
