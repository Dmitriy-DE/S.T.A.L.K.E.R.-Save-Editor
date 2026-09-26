using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayMoneyWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-money");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return [new MoneyVector(
                    vector.GetProperty("releaseId").GetString()!,
                    vector.GetProperty("source").GetString()!,
                    vector.GetProperty("sourceSha256").GetString()!,
                    vector.GetProperty("expected").GetString()!,
                    vector.GetProperty("expectedRaw").GetString()!,
                    vector.GetProperty("money").GetUInt32())];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Money_write_matches_python_container_and_unpacked_bytes(MoneyVector vector)
    {
        var source = ReadFixture(vector.Source);
        var expected = ReadFixture(vector.Expected);
        var expectedRaw = ReadFixture(vector.ExpectedRaw);
        var plan = new EditPlan(vector.SourceSha256, vector.Money);

        var prepared = XRayMoneyWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(expectedRaw, XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(vector.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), prepared.OutputSha256);
        var parsed = vector.ReleaseId.EndsWith("-ee", StringComparison.Ordinal)
            ? XRayEnhancedReader.FromBytes(prepared.Data.Span)
            : XRayTrilogyReader.FromBytes(prepared.Data.Span);
        Assert.Equal(vector.ReleaseId, parsed.FormatId);
        Assert.Equal(vector.Money, parsed.Money);
    }

    [Fact]
    public void Rejects_a_source_whose_sha_does_not_match_the_edit_plan()
    {
        var source = ReadFixture("xray-money-soc-source.sav");
        var plan = new EditPlan(new string('0', 64), 1234);

        Assert.Throws<XRayFormatException>(() => XRayMoneyWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_money_above_the_python_writer_limit()
    {
        var source = ReadFixture("xray-money-soc-source.sav");
        var plan = new EditPlan(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), 2_000_000_001);

        Assert.Throws<XRayFormatException>(() => XRayMoneyWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_a_foreign_s2_container()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-s2.sav"));
        var plan = new EditPlan(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), 1234);

        Assert.Throws<XRayFormatException>(() => XRayMoneyWriter.Prepare(source, plan));
    }

    [Fact]
    public void Capability_maturities_match_the_python_registry()
    {
        using var manifest = ReadManifest();
        foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
        {
            AssertCapabilities(vector.GetProperty("releaseId").GetString()!, vector.GetProperty("capabilities"));
        }

        foreach (var release in manifest.RootElement.GetProperty("capabilities").EnumerateObject())
        {
            AssertCapabilities(release.Name, release.Value);
        }
    }

    [Fact]
    public void Rejects_an_unknown_capability_name()
    {
        Assert.Throws<KeyNotFoundException>(
            () => CapabilityRegistry.Get("stalker-soc", "write_unknown_kind"));
    }

    private static void AssertCapabilities(string releaseId, JsonElement expected)
    {
        foreach (var capability in expected.EnumerateObject())
        {
            var actual = CapabilityRegistry.Get(releaseId, capability.Name);
            Assert.Equal(
                capability.Value.GetString(),
                actual.Maturity.ToString().ToLowerInvariant());
        }
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "xray-money-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    public sealed record MoneyVector(
        string ReleaseId,
        string Source,
        string SourceSha256,
        string Expected,
        string ExpectedRaw,
        uint Money);
}
