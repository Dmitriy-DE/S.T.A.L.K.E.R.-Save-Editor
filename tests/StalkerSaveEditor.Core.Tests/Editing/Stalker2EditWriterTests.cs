using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class Stalker2EditWriterTests
{
    private static readonly string MoneyFixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-money");

    private static readonly string StackFixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-stacks");

    private static readonly string EquipmentFixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-equipment");

    [Fact]
    public void Dispatches_single_money_edit_via_fast_path()
    {
        var manifest = ReadManifest(MoneyFixtureDirectory, "s2-money-vectors.json");
        var vector = manifest.RootElement;
        var source = ReadFixture(MoneyFixtureDirectory, vector.GetProperty("source").GetString()!);
        var expected = ReadFixture(MoneyFixtureDirectory, vector.GetProperty("expected").GetString()!);
        var plan = new EditPlan(
            vector.GetProperty("sourceSha256").GetString()!,
            money: vector.GetProperty("money").GetUInt32());

        var prepared = Stalker2EditWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(plan.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Sha256(expected), prepared.OutputSha256);

        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.Equal(vector.GetProperty("money").GetUInt32(), parsed.Money);
        Assert.True(parsed.CrcOk);
    }

    [Fact]
    public void Dispatches_single_stack_edit_via_fast_path()
    {
        var manifest = ReadManifest(StackFixtureDirectory, "s2-stacks-vectors.json");
        var vector = manifest.RootElement;
        var source = ReadFixture(StackFixtureDirectory, vector.GetProperty("source").GetString()!);
        var expected = ReadFixture(StackFixtureDirectory, vector.GetProperty("expected").GetString()!);
        var handle = vector.GetProperty("handle").GetUInt32();
        var count = vector.GetProperty("count").GetUInt32();
        var plan = new EditPlan(
            vector.GetProperty("sourceSha256").GetString()!,
            stackCounts: new Dictionary<uint, uint> { [handle] = count });

        var prepared = Stalker2EditWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(plan.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Sha256(expected), prepared.OutputSha256);

        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.True(parsed.CrcOk);
        Assert.Equal(count, Assert.Single(parsed.Inventory, item => item.Handle == handle).Count);
    }

    [Fact]
    public void Dispatches_single_durability_edit_via_fast_path()
    {
        var manifest = ReadManifest(EquipmentFixtureDirectory, "s2-equipment-vectors.json");
        var vector = ReadVector(manifest.RootElement, "weapon");
        var source = ReadFixture(EquipmentFixtureDirectory, vector.GetProperty("source").GetString()!);
        var expected = ReadFixture(EquipmentFixtureDirectory, vector.GetProperty("expected").GetString()!);
        var handle = vector.GetProperty("handle").GetUInt32();
        var targetCondition = vector.GetProperty("targetCondition").GetDouble();
        var plan = new EditPlan(
            vector.GetProperty("sourceSha256").GetString()!,
            durability: new Dictionary<uint, double> { [handle] = targetCondition });

        var prepared = Stalker2EditWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(plan.SourceSha256, prepared.SourceSha256);
        Assert.Equal(Sha256(expected), prepared.OutputSha256);

        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.True(parsed.CrcOk);
        Assert.Equal((float)targetCondition, Assert.Single(parsed.Inventory, item => item.Handle == handle).Condition);
    }

    [Fact]
    public void Combines_money_and_stack_edits_as_sequential_verified_pipeline()
    {
        var source = ReadFixture(MoneyFixtureDirectory, "s2-money-source.sav");
        const uint targetMoney = 777_888;
        const uint stackHandle = 805306369; // 0x30000001
        const uint targetStackCount = 15;

        var plan = new EditPlan(
            Sha256(source),
            money: targetMoney,
            stackCounts: new Dictionary<uint, uint> { [stackHandle] = targetStackCount });

        var prepared = Stalker2EditWriter.Prepare(source, plan);
        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);

        Assert.True(parsed.CrcOk);
        Assert.Equal(targetMoney, parsed.Money);
        Assert.Equal(targetStackCount, Assert.Single(parsed.Inventory, item => item.Handle == stackHandle).Count);
        Assert.Equal(Sha256(source), prepared.SourceSha256);
        Assert.Equal(Sha256(prepared.Data.Span), prepared.OutputSha256);
    }

    [Fact]
    public void Combines_durability_money_and_stacks_in_single_durability_writer_pass()
    {
        var manifest = ReadManifest(EquipmentFixtureDirectory, "s2-equipment-vectors.json");
        var vector = ReadVector(manifest.RootElement, "weapon");
        var source = ReadFixture(EquipmentFixtureDirectory, vector.GetProperty("source").GetString()!);
        var handle = vector.GetProperty("handle").GetUInt32();
        var targetCondition = vector.GetProperty("targetCondition").GetDouble();
        const uint targetMoney = 555_444;

        var plan = new EditPlan(
            vector.GetProperty("sourceSha256").GetString()!,
            money: targetMoney,
            durability: new Dictionary<uint, double> { [handle] = targetCondition });

        var prepared = Stalker2EditWriter.Prepare(source, plan);
        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);

        Assert.True(parsed.CrcOk);
        Assert.Equal(targetMoney, parsed.Money);
        Assert.Equal((float)targetCondition, Assert.Single(parsed.Inventory, item => item.Handle == handle).Condition);
    }

    [Fact]
    public void Rejects_empty_edit_plan()
    {
        var source = ReadFixture(MoneyFixtureDirectory, "s2-money-source.sav");
        var plan = new EditPlan(Sha256(source));

        Assert.Throws<Stalker2FormatException>(() => Stalker2EditWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_unsupported_edit_kinds_for_s2()
    {
        var source = ReadFixture(MoneyFixtureDirectory, "s2-money-source.sav");
        var plan = new EditPlan(
            Sha256(source),
            upgrades: new Dictionary<ushort, IReadOnlyList<string>> { [1] = ["up_1"] });

        Assert.Throws<Stalker2FormatException>(() => Stalker2EditWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_mismatched_source_sha256()
    {
        var source = ReadFixture(MoneyFixtureDirectory, "s2-money-source.sav");
        var plan = new EditPlan(new string('a', 64), money: 12345);

        Assert.Throws<Stalker2FormatException>(() => Stalker2EditWriter.Prepare(source, plan));
    }

    private static JsonElement ReadVector(JsonElement manifest, string caseName) =>
        Assert.Single(manifest.GetProperty("vectors").EnumerateArray(),
            vector => vector.GetProperty("case").GetString() == caseName).Clone();

    private static JsonDocument ReadManifest(string directory, string filename) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, filename)));

    private static byte[] ReadFixture(string directory, string filename) =>
        File.ReadAllBytes(Path.Combine(directory, filename));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
