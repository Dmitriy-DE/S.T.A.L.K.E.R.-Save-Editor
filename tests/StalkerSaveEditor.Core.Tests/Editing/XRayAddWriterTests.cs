using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayAddWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-add");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return
                [
                    new AddVector(
                        vector.GetProperty("releaseId").GetString()!,
                        vector.GetProperty("source").GetString()!,
                        vector.GetProperty("sourceSha256").GetString()!,
                        vector.GetProperty("expected").GetString()!,
                        vector.GetProperty("expectedRaw").GetString()!,
                        vector.GetProperty("itemKey").GetString()!,
                        vector.GetProperty("quantity").GetUInt32(),
                        vector.GetProperty("templateId").GetUInt16(),
                        vector.GetProperty("addedId").GetUInt16(),
                        vector.GetProperty("placeEncoding").GetString()!,
                        vector.GetProperty("expectedPlace").GetUInt16(),
                        vector.GetProperty("sourcePlace").GetUInt16(),
                        vector.GetProperty("templateUpgrades").EnumerateArray()
                            .Select(value => value.GetString()!).ToArray())
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Add_write_matches_python_bytes_and_resets_cloned_state(AddVector vector)
    {
        var source = ReadFixture(vector.Source);
        var expected = ReadFixture(vector.Expected);
        var expectedRaw = ReadFixture(vector.ExpectedRaw);
        var catalog = ReadCatalog(vector.ReleaseId);
        var plan = Plan(source, vector.ItemKey, vector.Quantity);

        var prepared = XRayAddWriter.Prepare(source, plan, catalog);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(expectedRaw, XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(vector.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), prepared.OutputSha256);
        var parsed = ReadSupported(prepared.Data.Span);
        Assert.Equal(vector.ReleaseId, parsed.FormatId);
        var added = Assert.Single(parsed.Inventory, item => item.Handle == vector.AddedId);
        Assert.Equal(vector.ItemKey, added.TypeKey);
        Assert.Equal(parsed.ActorId, added.ParentId);
        Assert.Empty(added.Upgrades ?? []);
        if (vector.ItemKey.StartsWith("ammo_", StringComparison.Ordinal))
        {
            Assert.Equal(checked((ushort)vector.Quantity), added.Count);
        }

        var before = ReadSupported(source);
        var originalTemplate = Assert.Single(before.Inventory, item => item.Handle == vector.TemplateId);
        var preservedTemplate = Assert.Single(parsed.Inventory, item => item.Handle == vector.TemplateId);
        Assert.Equal(vector.TemplateUpgrades, originalTemplate.Upgrades ?? Array.Empty<string>());
        Assert.Equal(originalTemplate.Upgrades, preservedTemplate.Upgrades);

        if (vector.PlaceEncoding != "none")
        {
            var clientData = ReadClientData(prepared.Data.Span, vector.AddedId);
            Assert.Equal((byte)2, clientData[0]);
            if (vector.PlaceEncoding == "u8")
            {
                Assert.Equal((byte)vector.ExpectedPlace, clientData[1]);
            }
            else
            {
                Assert.Equal(vector.ExpectedPlace, BinaryPrimitives.ReadUInt16LittleEndian(clientData.AsSpan(1)));
            }
        }
    }

    [Fact]
    public void Equipped_call_of_pripyat_template_does_not_transfer_its_slot_or_upgrades()
    {
        var vector = PythonOracleVectors.Select(row => (AddVector)row[0]!)
            .Single(value => value.ReleaseId == "stalker-cop" && value.PlaceEncoding == "u16");
        var source = ReadFixture(vector.Source);
        var sourceSave = XRayTrilogyReader.FromBytes(source);
        var wornTemplate = Assert.Single(sourceSave.Inventory, item => item.Handle == vector.TemplateId);
        Assert.Equal(vector.TemplateUpgrades, wornTemplate.Upgrades);

        var prepared = XRayAddWriter.Prepare(source, Plan(source, vector.ItemKey), ReadCatalog(vector.ReleaseId));
        Assert.Equal(ReadFixture(vector.Expected), prepared.Data.ToArray());

        var saved = XRayTrilogyReader.FromBytes(prepared.Data.Span);
        var added = Assert.Single(saved.Inventory, item => item.Handle == vector.AddedId);
        Assert.Empty(added.Upgrades ?? Array.Empty<string>());
        var clientData = ReadClientData(prepared.Data.Span, vector.AddedId);
        var place = BinaryPrimitives.ReadUInt16LittleEndian(clientData.AsSpan(1));
        Assert.Equal(3, place & 0x0F);
        Assert.Equal((vector.SourcePlace & 0xFFF0) | 3, place);
    }

    [Fact]
    public void Rejects_item_keys_outside_the_catalog_and_unknown_serialization_families()
    {
        var vector = PythonOracleVectors.Select(row => (AddVector)row[0]!)
            .Single(value => value.ReleaseId == "stalker-cop" && value.PlaceEncoding == "u16");
        var source = ReadFixture(vector.Source);
        var missingKeyPlan = Plan(source, "unknown_item_key");
        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            missingKeyPlan,
            ReadCatalog(vector.ReleaseId)));

        const string customCatalogJson = """
            {"schema_version":1,"releases":{"stalker-cop":{"items":[{"key":"opaque_item","serialization_family":"unconfirmed"}]}}}
            """;
        var catalog = CatalogBundleReader.Load(Encoding.UTF8.GetBytes(customCatalogJson))["stalker-cop"].Items;
        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            Plan(source, "opaque_item"),
            catalog));
    }

    [Fact]
    public void Rejects_ammo_quantity_over_the_official_catalog_max_stack()
    {
        var source = ReadFixture("../writer-stacks/xray-stack-cop-source.sav");
        var catalog = ReadCatalog("stalker-cop");
        var definition = Assert.IsType<ItemDefinition>(catalog.Resolve("ammo_9x39_pab9"));
        Assert.Equal(30, definition.MaxStack);

        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            Plan(source, "ammo_9x39_pab9", quantity: 31),
            catalog));
    }

    [Fact]
    public void Rejects_a_catalog_for_another_release_and_mixed_capabilities()
    {
        var vector = PythonOracleVectors.Select(row => (AddVector)row[0]!)
            .Single(value => value.ReleaseId == "stalker-cop" && value.PlaceEncoding == "u16");
        var source = ReadFixture(vector.Source);

        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            Plan(source, vector.ItemKey),
            ReadCatalog("stalker-soc")));

        var mixedPlan = new EditPlan(
            Sha256(source),
            money: 4321,
            adds: [new ItemAddRequest(vector.ItemKey)]);
        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            mixedPlan,
            ReadCatalog(vector.ReleaseId)));
    }

    [Fact]
    public void Rejects_stale_sources_and_destinations_outside_actor_inventory()
    {
        var vector = PythonOracleVectors.Select(row => (AddVector)row[0]!)
            .Single(value => value.ReleaseId == "stalker-cop" && value.PlaceEncoding == "u16");
        var source = ReadFixture(vector.Source);
        var catalog = ReadCatalog(vector.ReleaseId);

        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            new EditPlan(new string('0', 64), adds: [new ItemAddRequest(vector.ItemKey)]),
            catalog));
        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                adds: [new ItemAddRequest(vector.ItemKey, destination: "stash:4660")]),
            catalog));
    }

    [Fact]
    public void Rejects_a_known_family_when_the_save_has_no_registry_template()
    {
        var vector = PythonOracleVectors.Select(row => (AddVector)row[0]!)
            .Single(value => value.ReleaseId == "stalker-cop" && value.PlaceEncoding == "u16");
        var source = ReadFixture(vector.Source);
        const string catalogJson = """
            {"schema_version":1,"releases":{"stalker-cop":{"items":[{"key":"document_note","serialization_family":"document"}]}}}
            """;
        var catalog = CatalogBundleReader.Load(Encoding.UTF8.GetBytes(catalogJson))["stalker-cop"].Items;

        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            Plan(source, "document_note"),
            catalog));
    }

    [Fact]
    public void Rejects_a_catalog_key_without_a_confirmed_family_even_if_a_base_template_exists()
    {
        var vector = PythonOracleVectors.Select(row => (AddVector)row[0]!)
            .Single(value => value.ReleaseId == "stalker-cop" && value.PlaceEncoding == "u16");
        var source = ReadFixture(vector.Source);
        const string catalogJson = """
            {"schema_version":1,"releases":{"stalker-cop":{"items":[{"key":"opaque_item"},{"key":"outfit_cop_template","serialization_family":"base"}]}}}
            """;
        var catalog = CatalogBundleReader.Load(Encoding.UTF8.GetBytes(catalogJson))["stalker-cop"].Items;

        Assert.Throws<XRayFormatException>(() => XRayAddWriter.Prepare(
            source,
            Plan(source, "opaque_item"),
            catalog));
    }

    [Fact]
    public void Add_capability_maturities_match_the_python_registry()
    {
        using var manifest = ReadManifest();
        foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
        {
            Assert.Equal(
                vector.GetProperty("capabilities").GetProperty("add_items").GetString(),
                CapabilityRegistry.Get(vector.GetProperty("releaseId").GetString()!, "add_items")
                    .Maturity.ToString().ToLowerInvariant());
        }
    }

    [Fact]
    public void Ammo_add_oracle_vectors_cover_all_six_supported_releases()
    {
        var releaseIds = PythonOracleVectors
            .Select(row => (AddVector)row[0]!)
            .Where(vector => vector.ItemKey.StartsWith("ammo_", StringComparison.Ordinal))
            .Select(vector => vector.ReleaseId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["stalker-cop", "stalker-cop-ee", "stalker-cs", "stalker-cs-ee", "stalker-soc", "stalker-soc-ee"],
            releaseIds);
    }

    [Fact]
    public void Edit_plan_snapshots_add_requests_without_reordering_them()
    {
        var sourceSha = new string('0', 64);
        var request = new ItemAddRequest("exo_outfit", quantity: 2);
        var requests = new List<ItemAddRequest> { request };
        var plan = new EditPlan(sourceSha, adds: requests);
        requests.Clear();

        Assert.Equal([request], plan.Adds);
        Assert.Throws<NotSupportedException>(() => ((IList<ItemAddRequest>)plan.Adds)[0] = request);
        Assert.Equal(
            [request, new ItemAddRequest("exo_outfit", quantity: 3)],
            new EditPlan(sourceSha, adds: [request, new ItemAddRequest("exo_outfit", quantity: 3)]).Adds);
    }

    private static XRayTrilogySave ReadSupported(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException originalFailure)
        {
            try
            {
                return XRayEnhancedReader.FromBytes(data);
            }
            catch (XRayFormatException)
            {
                throw originalFailure;
            }
        }
    }

    private static ItemCatalog ReadCatalog(string releaseId)
    {
        var key = releaseId switch
        {
            "stalker-soc-ee" => "stalker-soc",
            "stalker-cs-ee" => "stalker-cs",
            "stalker-cop-ee" => "stalker-cop",
            _ => releaseId,
        };
        return CatalogBundleReader.LoadEmbedded()[key].Items;
    }

    private static EditPlan Plan(byte[] source, string itemKey, uint quantity = 1) =>
        new(Sha256(source), adds: [new ItemAddRequest(itemKey, quantity)]);

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "xray-add-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static byte[] ReadClientData(ReadOnlySpan<byte> save, ushort objectId)
    {
        var raw = XRayContainer.FromBytes(save).Raw.Span;
        var objectChunk = Assert.Single(XRayContainer.ParseChunks(raw), chunk => chunk.Type == 2);
        var payload = objectChunk.Data.Span;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        var position = sizeof(uint);
        for (var index = 0; index < count; index++)
        {
            var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[position..]);
            position += sizeof(ushort);
            var spawn = payload.Slice(position, spawnLength);
            position += spawnLength;
            var spawnInfo = ReadSpawn(spawn);
            var updateLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[position..]);
            position += sizeof(ushort) + updateLength;
            if (spawnInfo.ObjectId == objectId) return spawnInfo.ClientData;
        }

        throw new Xunit.Sdk.XunitException($"Object 0x{objectId:X4} was not found in OBJECT chunk.");
    }

    private static SpawnInfo ReadSpawn(ReadOnlySpan<byte> spawn)
    {
        var reader = new SpawnReader(spawn);
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var name = reader.ReadZeroTerminatedString();
        _ = reader.ReadZeroTerminatedString();
        reader.Skip(sizeof(byte) * 2 + sizeof(float) * 6 + sizeof(ushort));
        var objectId = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // parent id
        _ = reader.ReadUInt16(); // phantom id
        _ = reader.ReadUInt16(); // flags
        var version = reader.ReadUInt16();
        if (version > 120) reader.Skip(sizeof(ushort));
        if (version > 69) reader.Skip(sizeof(ushort));
        var clientDataLength = version > 93 ? reader.ReadUInt16() : reader.ReadByte();
        return new SpawnInfo(name, objectId, reader.ReadBytes(clientDataLength));
    }

    public sealed record AddVector(
        string ReleaseId,
        string Source,
        string SourceSha256,
        string Expected,
        string ExpectedRaw,
        string ItemKey,
        uint Quantity,
        ushort TemplateId,
        ushort AddedId,
        string PlaceEncoding,
        ushort ExpectedPlace,
        ushort SourcePlace,
        string[] TemplateUpgrades);

    private sealed record SpawnInfo(string Name, ushort ObjectId, byte[] ClientData);

    private ref struct SpawnReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public ushort ReadUInt16()
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_position..]);
            _position += sizeof(ushort);
            return value;
        }

        public byte ReadByte() => _data[_position++];

        public byte[] ReadBytes(int length)
        {
            var value = _data.Slice(_position, length).ToArray();
            _position += length;
            return value;
        }

        public string ReadZeroTerminatedString()
        {
            var length = _data[_position..].IndexOf((byte)0);
            if (length < 0) throw new InvalidDataException("Unterminated SPAWN string in fixture.");
            var value = Encoding.UTF8.GetString(_data.Slice(_position, length));
            _position += length + 1;
            return value;
        }

        public void Skip(int length) => _position += length;
    }
}
