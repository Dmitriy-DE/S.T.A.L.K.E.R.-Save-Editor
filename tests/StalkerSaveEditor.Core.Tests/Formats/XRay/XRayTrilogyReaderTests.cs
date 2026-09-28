using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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

        Assert.Empty(parsed.LevelChangers);
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
    public void Exposes_level_changer_records_in_the_public_save_model()
    {
        var source = ReadFixture("xray-soc.sav");
        var save = XRayTrilogyReader.FromBytes(AppendLevelChangerObject(source));

        var changer = Assert.Single(save.LevelChangers);
        Assert.Equal((ushort)0xF001, changer.Handle);
        Assert.Equal((ushort)0, changer.ParentId);
        Assert.Equal("level_changer", changer.Name);
        Assert.Equal(string.Empty, changer.NameReplace);
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

    private static JsonDocument ReadGolden() => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "fixture-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static byte[] AppendLevelChangerObject(byte[] source)
    {
        var container = XRayContainer.FromBytes(source);
        var objectChunk = Assert.Single(container.Chunks, chunk => chunk.Type == 2);
        var objectData = objectChunk.Data.ToArray();
        var objectCount = BinaryPrimitives.ReadUInt32LittleEndian(objectData);
        var offset = sizeof(uint);
        byte[]? templateRecord = null;
        while (offset < objectData.Length)
        {
            var recordStart = offset;
            var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(objectData.AsSpan(offset));
            offset += sizeof(ushort);
            var spawnStart = offset;
            var spawn = objectData.AsSpan(spawnStart, spawnLength);
            offset += spawnLength;
            var updateLength = BinaryPrimitives.ReadUInt16LittleEndian(objectData.AsSpan(offset));
            offset += sizeof(ushort) + updateLength;

            var nameEnd = spawn[2..].IndexOf((byte)0) + 2;
            var name = Encoding.UTF8.GetString(spawn[2..nameEnd]);
            if (name != "actor")
            {
                templateRecord = objectData.AsSpan(recordStart, offset - recordStart).ToArray();
                break;
            }
        }

        Assert.NotNull(templateRecord);
        var oldSpawnLength = BinaryPrimitives.ReadUInt16LittleEndian(templateRecord);
        var oldSpawn = templateRecord.AsSpan(sizeof(ushort), oldSpawnLength);
        var nameEndOffset = oldSpawn[2..].IndexOf((byte)0) + 2;
        var oldNameTerminator = nameEndOffset;
        var newName = "level_changer"u8;
        var newSpawn = new byte[oldSpawn.Length + newName.Length - (oldNameTerminator - 2)];
        oldSpawn[..2].CopyTo(newSpawn);
        newName.CopyTo(newSpawn.AsSpan(2));
        newSpawn[2 + newName.Length] = 0;
        oldSpawn[(oldNameTerminator + 1)..].CopyTo(newSpawn.AsSpan(3 + newName.Length));

        var secondNull = newSpawn.AsSpan(2).IndexOf((byte)0) + 3;
        secondNull += newSpawn.AsSpan(secondNull).IndexOf((byte)0) + 1;
        var objectIdOffset = secondNull + sizeof(ushort) + 6 * sizeof(float) + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(newSpawn.AsSpan(objectIdOffset), 0xF001);
        BinaryPrimitives.WriteUInt16LittleEndian(newSpawn.AsSpan(objectIdOffset + sizeof(ushort)), 0);

        var oldUpdateOffset = sizeof(ushort) + oldSpawnLength;
        var newRecord = new byte[sizeof(ushort) + newSpawn.Length + templateRecord.Length - oldUpdateOffset];
        BinaryPrimitives.WriteUInt16LittleEndian(newRecord, checked((ushort)newSpawn.Length));
        newSpawn.CopyTo(newRecord.AsSpan(sizeof(ushort)));
        templateRecord.AsSpan(oldUpdateOffset).CopyTo(newRecord.AsSpan(sizeof(ushort) + newSpawn.Length));

        using var objectStream = new MemoryStream();
        using (var objectWriter = new BinaryWriter(objectStream, Encoding.UTF8, leaveOpen: true))
        {
            objectWriter.Write(checked(objectCount + 1));
            objectWriter.Write(objectData.AsSpan(sizeof(uint)));
            objectWriter.Write(newRecord);
        }

        using var rawStream = new MemoryStream();
        using (var rawWriter = new BinaryWriter(rawStream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var chunk in container.Chunks)
            {
                var payload = chunk.Type == 2 ? objectStream.ToArray() : chunk.Data.ToArray();
                rawWriter.Write(chunk.Type);
                rawWriter.Write(checked((uint)payload.Length));
                rawWriter.Write(payload);
            }
        }

        return container.Build(rawStream.ToArray());
    }

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
