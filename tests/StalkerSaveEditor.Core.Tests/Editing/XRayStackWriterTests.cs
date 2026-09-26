using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayStackWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-stacks");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return [new StackVector(
                    vector.GetProperty("releaseId").GetString()!,
                    vector.GetProperty("source").GetString()!,
                    vector.GetProperty("sourceSha256").GetString()!,
                    vector.GetProperty("expected").GetString()!,
                    vector.GetProperty("expectedRaw").GetString()!,
                    vector.GetProperty("handle").GetUInt16(),
                    vector.GetProperty("count").GetUInt32())];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Stack_write_matches_python_container_and_unpacked_bytes(StackVector vector)
    {
        var source = ReadFixture(vector.Source);
        var expected = ReadFixture(vector.Expected);
        var expectedRaw = ReadFixture(vector.ExpectedRaw);
        var plan = new EditPlan(
            vector.SourceSha256,
            stackCounts: new Dictionary<ushort, uint> { [vector.Handle] = vector.Count });

        var prepared = XRayStackWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(expectedRaw, XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(vector.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), prepared.OutputSha256);
        var parsed = vector.ReleaseId.EndsWith("-ee", StringComparison.Ordinal)
            ? XRayEnhancedReader.FromBytes(prepared.Data.Span)
            : XRayTrilogyReader.FromBytes(prepared.Data.Span);
        Assert.Equal(vector.ReleaseId, parsed.FormatId);
        Assert.Equal((ushort?)vector.Count, Assert.Single(parsed.Inventory).Count);
    }

    [Fact]
    public void Rejects_a_stack_handle_that_does_not_exist()
    {
        var source = ReadFixture("xray-stack-soc-source.sav");
        var plan = Plan(source, 0xFFFF, 12);

        Assert.Throws<XRayFormatException>(() => XRayStackWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_a_read_only_non_ammo_inventory_kind()
    {
        var source = ReadFixture("xray-stack-unknown-kind-source.sav");
        var item = Assert.Single(XRayTrilogyReader.FromBytes(source).Inventory);
        var plan = Plan(source, item.Handle, 12);

        Assert.Throws<XRayFormatException>(() => XRayStackWriter.Prepare(source, plan));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Rejects_counts_outside_the_python_ammo_stack_range(uint count)
    {
        var source = ReadFixture("xray-stack-soc-source.sav");
        var plan = Plan(source, 0x1234, count);

        Assert.Throws<XRayFormatException>(() => XRayStackWriter.Prepare(source, plan));
    }

    [Fact]
    public void Money_writer_rejects_stack_edits_in_a_money_only_plan()
    {
        var source = ReadFixture("xray-stack-soc-source.sav");
        var sha = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var plan = new EditPlan(
            sha,
            money: 2000,
            stackCounts: new Dictionary<ushort, uint> { [0x1234] = 12 });

        Assert.Throws<XRayFormatException>(() => XRayMoneyWriter.Prepare(source, plan));
    }

    [Fact]
    public void Edit_plan_copies_stack_counts_and_exposes_a_read_only_snapshot()
    {
        var counts = new Dictionary<ushort, uint> { [0x1234] = 12 };
        var plan = new EditPlan(new string('0', 64), stackCounts: counts);

        counts[0x1234] = 20;

        Assert.Equal(12u, plan.StackCounts[0x1234]);
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<ushort, uint>)plan.StackCounts)[0x1234] = 20);
    }

    [Fact]
    public void Rejects_a_source_whose_sha_does_not_match_the_edit_plan()
    {
        var source = ReadFixture("xray-stack-soc-source.sav");
        var plan = new EditPlan(
            new string('0', 64),
            stackCounts: new Dictionary<ushort, uint> { [0x1234] = 12 });

        Assert.Throws<XRayFormatException>(() => XRayStackWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_money_in_a_stack_only_writer_plan()
    {
        var source = ReadFixture("xray-stack-soc-source.sav");
        var sha = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var plan = new EditPlan(
            sha,
            money: 2000,
            stackCounts: new Dictionary<ushort, uint> { [0x1234] = 12 });

        Assert.Throws<XRayFormatException>(() => XRayStackWriter.Prepare(source, plan));
    }

    [Fact]
    public void Stack_capability_maturities_match_the_python_registry()
    {
        using var manifest = ReadManifest();
        foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
        {
            AssertCapability(
                vector.GetProperty("releaseId").GetString()!,
                vector.GetProperty("capabilities").GetProperty("edit_stacks").GetString()!);
        }

        foreach (var release in manifest.RootElement.GetProperty("capabilities").EnumerateObject())
        {
            AssertCapability(
                release.Name,
                release.Value.GetProperty("edit_stacks").GetString()!);
        }
    }

    private static void AssertCapability(string releaseId, string expected) =>
        Assert.Equal(expected, CapabilityRegistry.Get(releaseId, "edit_stacks").Maturity.ToString().ToLowerInvariant());

    private static EditPlan Plan(byte[] source, ushort handle, uint count) =>
        new(
            Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
            stackCounts: new Dictionary<ushort, uint> { [handle] = count });

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "xray-stack-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    public sealed record StackVector(
        string ReleaseId,
        string Source,
        string SourceSha256,
        string Expected,
        string ExpectedRaw,
        ushort Handle,
        uint Count);
}
