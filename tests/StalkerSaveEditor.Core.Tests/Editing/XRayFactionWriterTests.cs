using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayFactionWriterTests
{
    [Fact]
    public void Faction_capabilities_match_the_python_golden_snapshot()
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "golden",
            "xray-faction-vectors.json")));

        foreach (var vector in snapshot.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var releaseId = vector.GetProperty("releaseId").GetString()
                ?? throw new InvalidDataException("Faction snapshot release id is missing.");
            foreach (var capability in vector.GetProperty("pythonCapabilities").EnumerateObject())
            {
                var actual = CapabilityRegistry.Get(releaseId, capability.Name)
                    .Maturity.ToString().ToLowerInvariant();
                Assert.Equal(capability.Value.GetString(), actual);
            }
        }
    }

    [Theory]
    [InlineData("stalker-soc")]
    [InlineData("stalker-cs")]
    [InlineData("stalker-cop")]
    [InlineData("stalker-cs-ee")]
    [InlineData("stalker-cop-ee")]
    public void Reads_player_community_and_actor_goodwill_from_registry(string releaseId)
    {
        var parsed = ReadFixtureByReleaseId(releaseId);

        Assert.Equal(0, parsed.PlayerFactionIndex);
        Assert.Contains(parsed.FactionRelations, entry => entry.CommunityIndex == 0 && entry.Value == 100);
        Assert.Contains(parsed.FactionRelations, entry => entry.CommunityIndex == 1 && entry.Value == -100);
        Assert.True(parsed.FactionRelationsEditable);

        using var snapshot = JsonDocument.Parse(ReadFixtureBytes("xray-faction-vectors.json"));
        var vector = Assert.Single(
            snapshot.RootElement.GetProperty("vectors").EnumerateArray(),
            item => item.GetProperty("releaseId").GetString() == releaseId);
        Assert.Equal(
            vector.GetProperty("playerFactionIndex").GetInt32(),
            parsed.PlayerFactionIndex);
        Assert.Equal(
            vector.GetProperty("actorRelations").EnumerateArray()
                .Select(item => new XRayFactionRelation(item[0].GetInt32(), item[1].GetInt32())),
            parsed.FactionRelations);
    }

    [Theory]
    [InlineData("stalker-soc")]
    [InlineData("stalker-cs")]
    [InlineData("stalker-cop")]
    public void Player_faction_write_matches_python_bytes(string releaseId)
    {
        var slug = releaseId["stalker-".Length..];
        var source = ReadFixtureBytes($"{slug}-source.sav");
        var expected = ReadFixtureBytes($"{slug}-player-faction.sav");
        var catalogs = CatalogBundleReader.LoadEmbedded()[releaseId];
        var catalog = Assert.IsType<FactionCatalog>(catalogs.Factions);
        var plan = new EditPlan(Sha256(source), playerFaction: "bandit");

        var prepared = XRayEditWriter.Prepare(source, plan, catalogs);

        Assert.Equal(
            XRayContainer.FromBytes(expected).Raw.ToArray(),
            XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(catalog.Resolve("bandit").NumericId, XRayTrilogyReader.FromBytes(prepared.Data.Span).PlayerFactionIndex);
    }

    [Theory]
    [InlineData("stalker-soc")]
    [InlineData("stalker-cs")]
    [InlineData("stalker-cop")]
    public void Faction_relation_write_matches_python_bytes(string releaseId)
    {
        var slug = releaseId["stalker-".Length..];
        var source = ReadFixtureBytes($"{slug}-source.sav");
        var expected = ReadFixtureBytes($"{slug}-relations.sav");
        var catalogs = CatalogBundleReader.LoadEmbedded()[releaseId];
        var catalog = Assert.IsType<FactionCatalog>(catalogs.Factions);
        var plan = new EditPlan(
            Sha256(source),
            factionRelations: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["bandit"] = 375,
            });

        var prepared = XRayEditWriter.Prepare(source, plan, catalogs);
        var banditIndex = catalog.Resolve("bandit").NumericId;

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Contains(
            XRayTrilogyReader.FromBytes(prepared.Data.Span).FactionRelations,
            entry => entry.CommunityIndex == banditIndex && entry.Value == 375);
    }

    [Theory]
    [InlineData("stalker-soc")]
    [InlineData("stalker-cs")]
    [InlineData("stalker-cop")]
    public void Combined_player_faction_and_relations_match_python_bytes(string releaseId)
    {
        var slug = releaseId["stalker-".Length..];
        var source = ReadFixtureBytes($"{slug}-source.sav");
        var expected = ReadFixtureBytes($"{slug}-expected.sav");
        var catalogs = CatalogBundleReader.LoadEmbedded()[releaseId];
        var catalog = Assert.IsType<FactionCatalog>(catalogs.Factions);
        var plan = new EditPlan(
            Sha256(source),
            playerFaction: "bandit",
            factionRelations: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["bandit"] = 375,
            });

        var prepared = XRayEditWriter.Prepare(source, plan, catalogs);
        var after = XRayTrilogyReader.FromBytes(prepared.Data.Span);

        Assert.Equal(expected, prepared.Data.ToArray());
        var banditIndex = catalog.Resolve("bandit").NumericId;
        Assert.Equal(banditIndex, after.PlayerFactionIndex);
        Assert.Contains(after.FactionRelations, entry => entry.CommunityIndex == banditIndex && entry.Value == 375);
        Assert.Equal(Sha256(expected), prepared.OutputSha256);
    }

    [Fact]
    public void Refuses_unknown_faction_keys_and_enhanced_writer_capabilities()
    {
        Assert.Equal(CapabilityMaturity.Experimental,
            CapabilityRegistry.Get("stalker-cop", "edit_relations").Maturity);
        Assert.Equal(CapabilityMaturity.Experimental,
            CapabilityRegistry.Get("stalker-cop", "edit_player_faction").Maturity);
        Assert.Equal(CapabilityMaturity.Unsupported,
            CapabilityRegistry.Get("stalker-cop-ee", "edit_relations").Maturity);
        Assert.Equal(CapabilityMaturity.Unsupported,
            CapabilityRegistry.Get("stalker-cop-ee", "edit_player_faction").Maturity);
        var source = ReadFixtureBytes("cop-source.sav");
        var catalogs = CatalogBundleReader.LoadEmbedded()["stalker-cop"];

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            new EditPlan(Sha256(source), playerFaction: "foreign-faction-key"),
            catalogs));
        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                factionRelations: new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["foreign-faction-key"] = 100,
                }),
            catalogs));
        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            new EditPlan(
                Sha256(source),
                factionRelations: new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["bandit"] = 1001,
                }),
            catalogs));

        var enhanced = ReadFixtureBytes("cop-ee-source.sav");
        var enhancedCatalogs = CatalogBundleReader.LoadEmbedded()["stalker-cop"];
        Assert.NotNull(enhancedCatalogs.Factions);
        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            enhanced,
            new EditPlan(Sha256(enhanced), playerFaction: "bandit"),
            enhancedCatalogs));
    }

    [Fact]
    public void Rejects_truncated_relation_registry_without_changing_source()
    {
        var source = ReadFixtureBytes("cop-source.sav");
        var container = XRayContainer.FromBytes(source);
        var raw = container.Raw.ToArray();
        var registry = Assert.Single(container.Chunks, chunk => chunk.Type == 9);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            raw.AsSpan(registry.Offset + sizeof(uint) * 2 + sizeof(uint)),
            3);
        var malformed = Repack(container.Version, raw);
        var catalogs = CatalogBundleReader.LoadEmbedded()["stalker-cop"];
        var original = malformed.ToArray();
        var parsed = XRayTrilogyReader.FromBytes(malformed);

        Assert.False(parsed.FactionRelationsEditable);
        Assert.Empty(parsed.FactionRelations);
        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            malformed,
            new EditPlan(
                Sha256(malformed),
                factionRelations: new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["bandit"] = 250,
                }),
            catalogs));
        Assert.Equal(original, malformed);
    }

    [Fact]
    public void Invalid_relation_registry_does_not_allocate_for_an_exception_per_read()
    {
        var source = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "xray-call-of-pripyat-ee.sav"));
        for (var index = 0; index < 3; index++)
        {
            _ = XRayEnhancedReader.FromBytes(source);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = XRayEnhancedReader.FromBytes(source);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, 4_000);
    }

    private static XRayTrilogySave ReadFixtureByReleaseId(string releaseId)
    {
        var slug = releaseId["stalker-".Length..];
        var data = ReadFixtureBytes($"{slug}-source.sav");
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException)
        {
            return XRayEnhancedReader.FromBytes(data);
        }
    }

    private static byte[] ReadFixtureBytes(string name) =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "writer-factions",
            name));

    private static byte[] Repack(uint version, byte[] raw)
    {
        var compressed = StalkerSaveEditor.Core.Codecs.Lzo1xCodec.Compress(raw);
        var output = new byte[12 + compressed.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output, XRayContainer.Signature);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), version);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), checked((uint)raw.Length));
        compressed.CopyTo(output, 12);
        return output;
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
