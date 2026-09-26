using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class Stalker2StackWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-stacks");

    [Fact]
    public void Stack_write_matches_python_container_and_unpacked_bytes()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var expected = ReadFixture(GetString(vector, "expected"));
        var expectedRaw = ReadFixture(GetString(vector, "expectedRaw"));
        var handle = vector.GetProperty("handle").GetUInt32();
        var count = vector.GetProperty("count").GetUInt32();
        var plan = new EditPlan(
            GetString(vector, "sourceSha256"),
            stackCounts: new Dictionary<uint, uint> { [handle] = count });

        var prepared = Stalker2StackWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(expectedRaw, Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(GetString(vector, "sourceSha256"), prepared.SourceSha256);
        Assert.Equal(
            GetString(vector, "expectedSha256"),
            Convert.ToHexString(SHA256.HashData(prepared.Data.Span)).ToLowerInvariant());
        Assert.Equal(GetString(vector, "expectedRawSha256"), Sha256(expectedRaw));

        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.True(parsed.CrcOk);
        Assert.True(handle > ushort.MaxValue, "S2 object handles must remain 32-bit in the edit plan.");
        Assert.Equal(count, Assert.Single(parsed.Inventory, item => item.Handle == handle).Count);
    }

    [Fact]
    public void Rejects_a_foreign_stack_handle()
    {
        var source = ReadFixture("s2-stacks-source.sav");

        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(
            source,
            new EditPlan(Sha256(source), stackCounts: new Dictionary<uint, uint>
            {
                [0x3000_FFFF] = 7,
            })));
    }

    [Fact]
    public void Rejects_a_count_above_the_item_max_stack()
    {
        var source = ReadFixture("s2-stacks-source.sav");
        var parsed = Stalker2SaveReader.FromBytes(source);
        using var vector = ReadManifest();
        var handle = vector.RootElement.GetProperty("handle").GetUInt32();
        var item = Assert.Single(parsed.Inventory, candidate => candidate.Handle == handle);

        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(
            source,
            new EditPlan(Sha256(source), stackCounts: new Dictionary<uint, uint>
            {
                [handle] = checked((uint)item.CountMax + 1),
            })));
    }

    [Fact]
    public void Rejects_an_unknown_inventory_kind()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var handle = vector.GetProperty("unknownKindHandle").GetUInt32();
        var parsed = Stalker2SaveReader.FromBytes(source);
        Assert.Contains(parsed.Orphans, item => item.Handle == handle && item.KindCode == 99);

        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(
            source,
            new EditPlan(Sha256(source), stackCounts: new Dictionary<uint, uint>
            {
                [handle] = 7,
            })));
    }

    [Fact]
    public void Rejects_zero_counts_and_plans_without_stack_edits()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var sourceSha = Sha256(source);
        var handle = vector.GetProperty("handle").GetUInt32();

        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(
            source,
            new EditPlan(sourceSha, stackCounts: new Dictionary<uint, uint> { [handle] = 0 })));
        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(source, new EditPlan(sourceSha)));
        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(
            source,
            new EditPlan(sourceSha, money: 123, stackCounts: new Dictionary<uint, uint> { [handle] = 7 })));
    }

    [Fact]
    public void Rejects_a_source_whose_sha_does_not_match_the_edit_plan()
    {
        var source = ReadFixture("s2-stacks-source.sav");

        Assert.Throws<Stalker2FormatException>(() => Stalker2StackWriter.Prepare(
            source,
            new EditPlan(new string('0', 64), stackCounts: new Dictionary<uint, uint>
            {
                [0x3000_0001] = 7,
            })));
    }

    [Fact]
    public void S2_stack_capability_matches_the_python_registry_snapshot()
    {
        using var manifest = ReadManifest();
        var expected = GetString(manifest.RootElement, "capability");

        Assert.Equal(
            expected,
            CapabilityRegistry.Get("stalker2", "edit_stacks").Maturity.ToString().ToLowerInvariant());
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "s2-stacks-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()!;

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
