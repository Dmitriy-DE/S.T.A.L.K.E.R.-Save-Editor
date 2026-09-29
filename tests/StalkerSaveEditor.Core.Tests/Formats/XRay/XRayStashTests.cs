using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayStashTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "xray-stashes");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return
                [
                    new StashVector(
                        vector.GetProperty("releaseId").GetString()!,
                        vector.GetProperty("source").GetString()!,
                        vector.GetProperty("sourceSha256").GetString()!,
                        vector.GetProperty("expectedTake").GetString()!,
                        vector.GetProperty("expectedTakeRaw").GetString()!,
                        vector.GetProperty("expectedTakeSha256").GetString()!,
                        vector.GetProperty("actorId").GetUInt16(),
                        vector.GetProperty("boxId").GetUInt16(),
                        vector.GetProperty("boxName").GetString()!,
                        vector.GetProperty("level").ValueKind == JsonValueKind.Null
                            ? null
                            : vector.GetProperty("level").GetString(),
                        vector.GetProperty("stashItemName").GetString()!,
                        vector.GetProperty("stashItemUpgrades").EnumerateArray()
                            .Select(value => value.GetString()!).ToArray(),
                        vector.GetProperty("stashItemId").GetUInt16(),
                        vector.GetProperty("backpackItemId").GetUInt16())
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Reads_stashes_and_box_contents_from_python_synthetic_vectors(StashVector vector)
    {
        var parsed = ReadSupported(ReadFixture(vector.Source));

        var stash = Assert.Single(parsed.Stashes);
        Assert.Equal(vector.BoxId, stash.Handle);
        Assert.Equal(vector.BoxName, stash.Name);
        Assert.Equal(vector.Level, stash.Level);
        var item = Assert.Single(stash.Items);
        Assert.Equal(vector.StashItemId, item.Handle);
        Assert.Equal(vector.BoxId, item.ParentId);
        Assert.Equal(vector.StashItemName, item.TypeKey);
        Assert.Equal(vector.StashItemUpgrades, item.Upgrades ?? []);
        Assert.DoesNotContain(parsed.Inventory, item => item.Handle == vector.StashItemId);
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Taking_a_stash_item_matches_python_packed_and_unpacked_bytes(StashVector vector)
    {
        var source = ReadFixture(vector.Source);
        var plan = new EditPlan(Sha256(source), stashTakes: [vector.StashItemId]);

        var prepared = XRayEditWriter.Prepare(source, plan);

        Assert.Equal(vector.SourceSha256, prepared.SourceSha256);
        Assert.Equal(vector.ExpectedTakeSha256, prepared.OutputSha256);
        Assert.Equal(ReadFixture(vector.ExpectedTake), prepared.Data.ToArray());
        Assert.Equal(ReadFixture(vector.ExpectedTakeRaw), XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        var moved = ReadSupported(prepared.Data.Span);
        Assert.Equal(moved.ActorId, Assert.Single(moved.Inventory, item => item.Handle == vector.StashItemId).ParentId);
        Assert.Empty(moved.Stashes);
        Assert.Equal(vector.StashItemUpgrades,
            Assert.Single(moved.Inventory, item => item.Handle == vector.StashItemId).Upgrades ?? []);
    }

    [Fact]
    public void Edit_writer_composes_money_and_stash_transfers_without_losing_the_original_plan()
    {
        var source = ReadFixture("xray-stash-cop-source.sav");
        var plan = new EditPlan(
            Sha256(source),
            money: 4321,
            stashTakes: [0x2345]);

        var prepared = XRayEditWriter.Prepare(source, plan);

        var parsed = ReadSupported(prepared.Data.Span);
        Assert.Equal(4321u, parsed.Money);
        Assert.Equal(parsed.ActorId, Assert.Single(parsed.Inventory, item => item.Handle == 0x2345).ParentId);
        Assert.Same(plan, prepared.Plan);
        Assert.Equal(Sha256(source), prepared.SourceSha256);
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Putting_a_backpack_item_in_a_box_changes_only_its_parent(StashVector vector)
    {
        var source = ReadFixture(vector.Source);
        var container = XRayContainer.FromBytes(source);
        var expectedRaw = container.Raw.ToArray();
        var parentOffset = FindParentIdOffset(container, vector.BackpackItemId);
        BinaryPrimitives.WriteUInt16LittleEndian(expectedRaw.AsSpan(parentOffset), vector.BoxId);
        var plan = new EditPlan(
            Sha256(source),
            stashPuts: [new StashPutRequest(vector.BackpackItemId, vector.BoxId)]);

        var prepared = XRayStashWriter.Prepare(source, plan);

        Assert.Equal(expectedRaw, XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        var saved = ReadSupported(prepared.Data.Span);
        var stash = Assert.Single(saved.Stashes);
        Assert.Contains(stash.Items, item => item.Handle == vector.BackpackItemId);
        Assert.DoesNotContain(saved.Inventory, item => item.Handle == vector.BackpackItemId);
        Assert.Equal(vector.StashItemUpgrades,
            Assert.Single(stash.Items, item => item.Handle == vector.BackpackItemId).Upgrades ?? []);
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Read_back_accepts_a_written_put_and_rejects_one_that_did_not_happen(StashVector vector)
    {
        var source = ReadFixture(vector.Source);
        var plan = new EditPlan(Sha256(source), stashPuts: [new StashPutRequest(vector.BackpackItemId, vector.BoxId)]);

        var prepared = XRayStashWriter.Prepare(source, plan);

        EditService.VerifyReadBack(prepared.Data.Span, vector.ReleaseId, plan);
        Assert.Throws<InvalidDataException>(() => EditService.VerifyReadBack(source, vector.ReleaseId, plan));
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Adding_an_item_directly_to_a_box_uses_the_box_as_parent(StashVector vector)
    {
        var source = ReadFixture(vector.Source);
        var catalog = ReadCatalog(vector.ReleaseId);
        var plan = new EditPlan(
            Sha256(source),
            adds: [new ItemAddRequest("bandage_added", destination: $"stash:{vector.BoxId}")]);

        var prepared = XRayAddWriter.Prepare(source, plan, catalog);

        var saved = ReadSupported(prepared.Data.Span);
        var stash = Assert.Single(saved.Stashes);
        var added = Assert.Single(stash.Items, item => item.TypeKey == "bandage_added");
        Assert.NotEqual(vector.BoxId, added.Handle);
        Assert.Equal(vector.BoxId, added.ParentId);
        Assert.Empty(added.Upgrades ?? []);
        Assert.DoesNotContain(saved.Inventory, item => item.Handle == added.Handle);
    }

    [Fact]
    public void Rejects_taking_an_item_that_is_not_in_a_stash()
    {
        var source = ReadFixture("xray-stash-cop-source.sav");
        var plan = new EditPlan(Sha256(source), stashTakes: [0x3456]);

        Assert.Throws<XRayFormatException>(() => XRayStashWriter.Prepare(source, plan));
    }

    [Theory]
    [InlineData("xray-stash-cop-source.sav", false)]
    [InlineData("xray-stash-cs-source.sav", true)]
    public void Rejects_putting_an_equipped_item_in_a_stash(string fixtureName, bool clearSkyBytePlacement)
    {
        var source = ReadFixture(fixtureName);
        var container = XRayContainer.FromBytes(source);
        var raw = container.Raw.ToArray();
        var clientPlaceOffset = FindClientPlaceOffset(container, 0x3456);
        raw[clientPlaceOffset] = 1;
        if (!clearSkyBytePlacement)
        {
            raw[clientPlaceOffset + 1] = 4;
        }

        var equipped = container.Build(raw);
        var plan = new EditPlan(
            Sha256(equipped),
            stashPuts: [new StashPutRequest(0x3456, 0x0010)]);

        Assert.Throws<XRayFormatException>(() => XRayStashWriter.Prepare(equipped, plan));
    }

    [Fact]
    public void Rejects_foreign_and_non_box_destinations_for_put_and_add()
    {
        var source = ReadFixture("xray-stash-cop-source.sav");
        var catalog = ReadCatalog("stalker-cop");

        Assert.Throws<XRayFormatException>(() => XRayStashWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                stashPuts: [new StashPutRequest(0x3456, 0x9999)])));
        Assert.Throws<XRayFormatException>(() => XRayStashWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                stashPuts: [new StashPutRequest(0x3456, 0x3456)])));
        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                adds: [new ItemAddRequest("bandage_added", destination: "stash:39321")]),
            catalog));
        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                adds: [new ItemAddRequest("bandage_added", destination: "stash:13398")]),
            catalog));
    }

    [Fact]
    public void Rejects_a_stale_source_before_moving_a_stash_item()
    {
        var source = ReadFixture("xray-stash-cop-source.sav");

        Assert.Throws<XRayFormatException>(() => XRayStashWriter.Prepare(
            source,
            new EditPlan(new string('0', 64), stashTakes: [0x2345])));
    }

    private static XRayTrilogySave ReadSupported(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException)
        {
            return XRayEnhancedReader.FromBytes(data);
        }
    }

    private static ItemCatalog ReadCatalog(string releaseId)
    {
        var originalRelease = releaseId switch
        {
            "stalker-soc-ee" => "stalker-soc",
            "stalker-cs-ee" => "stalker-cs",
            "stalker-cop-ee" => "stalker-cop",
            _ => releaseId,
        };
        var json = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            releases = new Dictionary<string, object>
            {
                [originalRelease] = new
                {
                    items = new[]
                    {
                        new { key = "bandage_added", serialization_family = "base" },
                    },
                },
            },
        });
        return CatalogBundleReader.Load(System.Text.Encoding.UTF8.GetBytes(json))[originalRelease].Items;
    }

    private static int FindParentIdOffset(XRayContainer container, ushort objectId)
    {
        var objectChunk = Assert.Single(container.Chunks, chunk => chunk.Type == 2);
        var data = objectChunk.Data.Span;
        var position = sizeof(uint);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        for (var index = 0; index < count; index++)
        {
            var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            var spawnStart = position + sizeof(ushort);
            var spawn = data.Slice(spawnStart, spawnLength);
            var idOffset = FindIdentityOffset(spawn, objectId);
            if (idOffset >= 0)
            {
                return checked(objectChunk.Offset + sizeof(uint) * 2 + spawnStart + idOffset + sizeof(ushort));
            }

            var updateSizeOffset = spawnStart + spawnLength;
            var updateLength = BinaryPrimitives.ReadUInt16LittleEndian(data[updateSizeOffset..]);
            position = updateSizeOffset + sizeof(ushort) + updateLength;
        }

        throw new InvalidDataException($"Object 0x{objectId:X4} was not found.");
    }

    private static int FindClientPlaceOffset(XRayContainer container, ushort objectId)
    {
        var objectChunk = Assert.Single(container.Chunks, chunk => chunk.Type == 2);
        var data = objectChunk.Data.Span;
        var position = sizeof(uint);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        for (var index = 0; index < count; index++)
        {
            var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            var spawnStart = position + sizeof(ushort);
            var spawn = data.Slice(spawnStart, spawnLength);
            var idOffset = FindIdentityOffset(spawn, objectId);
            if (idOffset >= 0)
            {
                const int CoPClientDataStartOffset = 16;
                return checked(
                    objectChunk.Offset + sizeof(uint) * 2 + spawnStart + idOffset + CoPClientDataStartOffset + 1);
            }

            var updateSizeOffset = spawnStart + spawnLength;
            var updateLength = BinaryPrimitives.ReadUInt16LittleEndian(data[updateSizeOffset..]);
            position = updateSizeOffset + sizeof(ushort) + updateLength;
        }

        throw new InvalidDataException($"Object 0x{objectId:X4} was not found.");
    }

    private static int FindIdentityOffset(ReadOnlySpan<byte> spawn, ushort objectId)
    {
        var position = sizeof(ushort);
        SkipString(spawn, ref position);
        SkipString(spawn, ref position);
        position += 2 + (6 * sizeof(float)) + sizeof(ushort);
        if (BinaryPrimitives.ReadUInt16LittleEndian(spawn[position..]) != objectId)
        {
            return -1;
        }

        return position;
    }

    private static void SkipString(ReadOnlySpan<byte> data, ref int position)
    {
        var terminator = data[position..].IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new InvalidDataException("SPAWN contains an unterminated string.");
        }

        position += terminator + 1;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonDocument ReadManifest() => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(FixtureDirectory, "xray-stash-vectors.json")));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    public sealed record StashVector(
        string ReleaseId,
        string Source,
        string SourceSha256,
        string ExpectedTake,
        string ExpectedTakeRaw,
        string ExpectedTakeSha256,
        ushort ActorId,
        ushort BoxId,
        string BoxName,
        string? Level,
        string StashItemName,
        string[] StashItemUpgrades,
        ushort StashItemId,
        ushort BackpackItemId);
}
