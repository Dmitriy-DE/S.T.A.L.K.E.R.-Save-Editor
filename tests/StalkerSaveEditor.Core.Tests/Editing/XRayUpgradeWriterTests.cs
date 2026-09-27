using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayUpgradeWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-upgrades");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return
                [
                    new UpgradeVector(
                        vector.GetProperty("releaseId").GetString()!,
                        vector.GetProperty("source").GetString()!,
                        vector.GetProperty("sourceSha256").GetString()!,
                        vector.GetProperty("expected").GetString()!,
                        vector.GetProperty("expectedRaw").GetString()!,
                        vector.GetProperty("handle").GetUInt16(),
                        vector.GetProperty("sourceUpgrades").EnumerateArray()
                            .Select(value => value.GetString()!).ToArray(),
                        vector.GetProperty("targetUpgrades").EnumerateArray()
                            .Select(value => value.GetString()!).ToArray())
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Reads_upgrade_vectors_for_clear_sky_and_call_of_pripyat(UpgradeVector vector)
    {
        var source = ReadFixture(vector.Source);
        var parsed = ReadSupported(source);

        Assert.Equal(vector.ReleaseId, parsed.FormatId);
        Assert.Equal(
            vector.SourceUpgrades,
            Assert.Single(parsed.Inventory, item => item.Handle == vector.Handle).Upgrades);
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Upgrade_write_matches_python_container_and_unpacked_bytes(UpgradeVector vector)
    {
        var source = ReadFixture(vector.Source);
        var catalog = ReadCatalogBundle(vector.ReleaseId, "up_a_wpn_test", "up_c_wpn_test");
        var plan = Plan(source, vector.Handle, vector.TargetUpgrades);

        var prepared = XRayEditWriter.Prepare(source, plan, catalog);

        Assert.Equal(ReadFixture(vector.Expected), prepared.Data.ToArray());
        Assert.Equal(ReadFixture(vector.ExpectedRaw), XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(vector.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(prepared.Data.Span)).ToLowerInvariant(), prepared.OutputSha256);
        var parsed = ReadSupported(prepared.Data.Span);
        Assert.Equal(vector.ReleaseId, parsed.FormatId);
        Assert.Equal(
            vector.TargetUpgrades,
            Assert.Single(parsed.Inventory, item => item.Handle == vector.Handle).Upgrades);
    }

    [Fact]
    public void Rejects_unknown_and_item_foreign_upgrade_keys()
    {
        var source = ReadFixture("xray-upgrades-cop-source.sav");
        var catalog = ReadCatalog("stalker-cop", "up_a_wpn_test", "up_c_wpn_test", "up_foreign");

        Assert.Throws<XRayFormatException>(() => XRayUpgradeWriter.Prepare(
            source,
            Plan(source, 0x3456, ["up_unknown_key"]),
            catalog));
        Assert.Throws<XRayFormatException>(() => XRayUpgradeWriter.Prepare(
            source,
            Plan(source, 0x3456, ["up_foreign"]),
            catalog));
    }

    [Fact]
    public void Rejects_upgrade_catalog_for_another_game()
    {
        var source = ReadFixture("xray-upgrades-cop-source.sav");

        Assert.Throws<XRayFormatException>(() => XRayUpgradeWriter.Prepare(
            source,
            Plan(source, 0x3456, ["up_a_wpn_test"]),
            ReadCatalog("stalker-cs", "up_a_wpn_test")));
    }

    [Fact]
    public void Shared_xray_writer_composes_upgrades_with_money()
    {
        var source = ReadFixture("xray-upgrades-cop-source.sav");
        var plan = new EditPlan(
            Sha256(source),
            money: 4321,
            upgrades: new Dictionary<ushort, IReadOnlyList<string>>
            {
                [0x3456] = ["legacy_unknown", "up_c_wpn_test"],
            });

        var prepared = XRayEditWriter.Prepare(
            source,
            plan,
            ReadCatalogBundle("stalker-cop", "up_a_wpn_test", "up_c_wpn_test"));
        var parsed = XRayTrilogyReader.FromBytes(prepared.Data.Span);

        Assert.Equal(4321u, parsed.Money);
        Assert.Equal(
            ["legacy_unknown", "up_c_wpn_test"],
            Assert.Single(parsed.Inventory, item => item.Handle == 0x3456).Upgrades);
    }

    [Fact]
    public void Rejects_handles_outside_actor_inventory_and_formats_without_upgrade_vectors()
    {
        var source = ReadFixture("xray-upgrades-cop-source.sav");
        var catalog = ReadCatalog("stalker-cop", "up_a_wpn_test");
        Assert.Throws<XRayFormatException>(() => XRayUpgradeWriter.Prepare(
            source,
            Plan(source, 0x9999, ["up_a_wpn_test"]),
            catalog));

        var soc = ReadFixture("xray-upgrades-soc-unsupported-source.sav");
        Assert.Throws<XRayFormatException>(() => XRayUpgradeWriter.Prepare(
            soc,
            Plan(soc, 0x1234, ["up_test"]),
            ReadCatalog("stalker-soc", "up_test")));
    }

    [Fact]
    public void Edit_plan_copies_upgrade_vectors_and_rejects_duplicate_keys()
    {
        var keys = new List<string> { "up_a_wpn_test" };
        var staged = new Dictionary<ushort, IReadOnlyList<string>> { [0x3456] = keys };
        var plan = new EditPlan(new string('0', 64), upgrades: staged);
        keys.Add("mutated");
        staged[0x3456] = ["replacement"];

        Assert.Equal(["up_a_wpn_test"], plan.Upgrades[0x3456]);
        Assert.Throws<ArgumentException>(() => new EditPlan(
            new string('0', 64),
            upgrades: new Dictionary<ushort, IReadOnlyList<string>>
            {
                [0x3456] = ["up_a_wpn_test", "up_a_wpn_test"],
            }));
    }

    private static EditPlan Plan(byte[] source, ushort handle, IReadOnlyList<string> keys) => new(
        Sha256(source),
        upgrades: new Dictionary<ushort, IReadOnlyList<string>> { [handle] = keys });

    private static XRayTrilogySave ReadSupported(ReadOnlySpan<byte> source)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(source);
        }
        catch (XRayFormatException)
        {
            return XRayEnhancedReader.FromBytes(source);
        }
    }

    private static UpgradeCatalog ReadCatalog(string formatId, params string[] keys) =>
        Assert.IsType<UpgradeCatalog>(ReadCatalogBundle(formatId, keys).Upgrades);

    private static CatalogBundle ReadCatalogBundle(string formatId, params string[] keys)
    {
        var baseRelease = formatId.EndsWith("-ee", StringComparison.Ordinal)
            ? formatId[..^3]
            : formatId;
        var definitions = keys.Select(key => new
        {
            key,
            release_id = baseRelease,
            item_key = key == "up_foreign" ? "wpn_other" : "wpn_test",
            applicable_item_keys = new[] { key == "up_foreign" ? "wpn_other" : "wpn_test" },
            source = "fixture",
        }).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            releases = new Dictionary<string, object>
            {
                [baseRelease] = new { items = Array.Empty<object>(), upgrades = definitions },
            },
        });
        return CatalogBundleReader.Load(Encoding.UTF8.GetBytes(json))[baseRelease];
    }

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(ReadFixture("xray-upgrades-vectors.json"));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public sealed record UpgradeVector(
        string ReleaseId,
        string Source,
        string SourceSha256,
        string Expected,
        string ExpectedRaw,
        ushort Handle,
        string[] SourceUpgrades,
        string[] TargetUpgrades);
}
