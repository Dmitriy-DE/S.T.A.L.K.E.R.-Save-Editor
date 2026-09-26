using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayDeleteWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-delete");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return [new DeleteVector(
                    vector.GetProperty("releaseId").GetString()!,
                    vector.GetProperty("source").GetString()!,
                    vector.GetProperty("sourceSha256").GetString()!,
                    vector.GetProperty("expected").GetString()!,
                    vector.GetProperty("expectedRaw").GetString()!,
                    vector.GetProperty("handle").GetUInt16())];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public void Delete_write_matches_python_container_and_unpacked_bytes(DeleteVector vector)
    {
        var source = ReadFixture(vector.Source);
        var expected = ReadFixture(vector.Expected);
        var expectedRaw = ReadFixture(vector.ExpectedRaw);

        var prepared = XRayDeleteWriter.Prepare(source, Plan(source, vector.Handle));

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(expectedRaw, XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(vector.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), prepared.OutputSha256);
        var parsed = vector.ReleaseId.EndsWith("-ee", StringComparison.Ordinal)
            ? XRayEnhancedReader.FromBytes(prepared.Data.Span)
            : XRayTrilogyReader.FromBytes(prepared.Data.Span);
        Assert.Equal(vector.ReleaseId, parsed.FormatId);
        Assert.DoesNotContain(parsed.Inventory, item => item.Handle == vector.Handle);
    }

    [Fact]
    public void Rejects_a_handle_that_does_not_exist()
    {
        var source = ReadFixture("xray-delete-soc-source.sav");

        Assert.Throws<XRayFormatException>(
            () => XRayDeleteWriter.Prepare(source, Plan(source, 0xFFFF)));
    }

    [Fact]
    public void Rejects_a_known_equipped_item()
    {
        var source = ReadFixture("xray-delete-equipped-source.sav");

        Assert.Throws<XRayFormatException>(
            () => XRayDeleteWriter.Prepare(source, Plan(source, 0x3456)));
    }

    [Fact]
    public void Rejects_an_item_with_registry_children()
    {
        var source = ReadFixture("xray-delete-dependent-source.sav");

        Assert.Throws<XRayFormatException>(
            () => XRayDeleteWriter.Prepare(source, Plan(source, 0x3456)));
    }

    [Fact]
    public void Rejects_the_actor_object()
    {
        var source = ReadFixture("xray-delete-soc-source.sav");

        Assert.Throws<XRayFormatException>(
            () => XRayDeleteWriter.Prepare(source, Plan(source, 0)));
    }

    [Fact]
    public void Rejects_an_object_owned_by_another_registry_object()
    {
        var source = ReadFixture("xray-delete-dependent-source.sav");

        Assert.Throws<XRayFormatException>(
            () => XRayDeleteWriter.Prepare(source, Plan(source, 0x4567)));
    }

    [Fact]
    public void Rejects_a_stale_source_hash()
    {
        var source = ReadFixture("xray-delete-soc-source.sav");
        var plan = new EditPlan(new string('0', 64), detachHandles: [0x1234]);

        Assert.Throws<XRayFormatException>(() => XRayDeleteWriter.Prepare(source, plan));
    }

    [Fact]
    public void Delete_plan_rejects_mixed_edits()
    {
        var source = ReadFixture("xray-delete-soc-source.sav");
        var plan = new EditPlan(
            Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
            money: 500,
            detachHandles: [0x1234]);

        Assert.Throws<XRayFormatException>(() => XRayDeleteWriter.Prepare(source, plan));
    }

    [Fact]
    public void Existing_single_capability_writers_reject_delete_plans()
    {
        var source = ReadFixture("xray-delete-soc-source.sav");
        var sha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var moneyPlan = new EditPlan(sha256, money: 500, detachHandles: [0x1234]);
        var stackPlan = new EditPlan(
            sha256,
            stackCounts: new Dictionary<uint, uint> { [0x1234] = 12 },
            detachHandles: [0x1234]);

        Assert.Throws<XRayFormatException>(() => XRayMoneyWriter.Prepare(source, moneyPlan));
        Assert.Throws<XRayFormatException>(() => XRayStackWriter.Prepare(source, stackPlan));
    }

    [Fact]
    public void Delete_plan_copies_handles_and_exposes_a_read_only_snapshot()
    {
        var handles = new List<ushort> { 0x1234 };
        var plan = new EditPlan(new string('0', 64), detachHandles: handles);
        handles[0] = 0x4321;

        Assert.Equal(new ushort[] { 0x1234 }, plan.DetachHandles);
        Assert.Throws<NotSupportedException>(
            () => ((IList<ushort>)plan.DetachHandles)[0] = 0x4321);
    }

    [Fact]
    public void Delete_capability_maturities_match_the_python_registry()
    {
        using var manifest = ReadManifest();
        foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
        {
            AssertCapability(
                vector.GetProperty("releaseId").GetString()!,
                vector.GetProperty("capabilities").GetProperty("remove_items").GetString()!);
        }

        foreach (var release in manifest.RootElement.GetProperty("capabilities").EnumerateObject())
        {
            AssertCapability(
                release.Name,
                release.Value.GetProperty("remove_items").GetString()!);
        }
    }

    private static void AssertCapability(string releaseId, string expected) =>
        Assert.Equal(expected, CapabilityRegistry.Get(releaseId, "remove_items").Maturity.ToString().ToLowerInvariant());

    private static EditPlan Plan(byte[] source, ushort handle) =>
        new(
            Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
            detachHandles: [handle]);

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "xray-delete-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    public sealed record DeleteVector(
        string ReleaseId,
        string Source,
        string SourceSha256,
        string Expected,
        string ExpectedRaw,
        ushort Handle);
}
