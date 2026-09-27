using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayDurabilityWriterTests
{
    [Theory]
    [InlineData("stalker-soc", false)]
    [InlineData("stalker-cs", false)]
    [InlineData("stalker-cop", false)]
    [InlineData("stalker-soc-ee", true)]
    [InlineData("stalker-cs-ee", true)]
    [InlineData("stalker-cop-ee", true)]
    public void Durability_write_matches_python_container_and_unpacked_bytes(
        string releaseId,
        bool enhanced)
    {
        var vector = ReadVector(releaseId);
        var source = ReadFixture(GetString(vector, "source"));
        var expected = ReadFixture(GetString(vector, "expected"));
        var expectedRaw = ReadFixture(GetString(vector, "expectedRaw"));
        var sourceSha256 = Sha256(source);
        Assert.Equal(GetString(vector, "sourceSha256"), sourceSha256);

        var plan = new EditPlan(
            sourceSha256,
            durability: new Dictionary<uint, double>
            {
                [vector.GetProperty("handle").GetUInt32()] = vector.GetProperty("targetCondition").GetDouble(),
            });
        var prepared = XRayEditWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(GetString(vector, "expectedSha256"), Sha256(prepared.Data.Span));
        Assert.Equal(expectedRaw, XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(plan, prepared.Plan);

        var parsed = enhanced
            ? XRayEnhancedReader.FromBytes(prepared.Data.Span)
            : XRayTrilogyReader.FromBytes(prepared.Data.Span);
        var item = Assert.Single(
            parsed.Inventory,
            candidate => candidate.Handle == vector.GetProperty("handle").GetUInt32());
        Assert.Equal((float)vector.GetProperty("targetCondition").GetDouble(), item.Condition);
    }

    [Fact]
    public void Q8_rounding_matches_python_before_f32_state_rounding()
    {
        var vector = ReadVector("stalker-cop", "q8-rounding");
        var source = ReadFixture(GetString(vector, "source"));
        var plan = new EditPlan(
            Sha256(source),
            durability: new Dictionary<uint, double>
            {
                [vector.GetProperty("handle").GetUInt32()] = vector.GetProperty("targetCondition").GetDouble(),
            });

        var prepared = XRayEditWriter.Prepare(source, plan);

        Assert.Equal(ReadFixture(GetString(vector, "expected")), prepared.Data.ToArray());
        Assert.Equal(
            ReadFixture(GetString(vector, "expectedRaw")),
            XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray());
    }

    [Fact]
    public void Rejects_non_finite_and_out_of_range_conditions_before_writing()
    {
        var source = ReadFixture("xray-durability-cop-source.sav");
        var invalidValues = new[] { double.NaN, double.PositiveInfinity, -0.01d, 1.01d };

        foreach (var condition in invalidValues)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EditPlan(
                Sha256(source),
                durability: new Dictionary<uint, double> { [0x3456] = condition }));
        }
    }

    [Fact]
    public void Edit_plan_copies_durability_targets()
    {
        var targets = new Dictionary<uint, double> { [0x3456] = 0.25d };
        var plan = new EditPlan(Sha256("synthetic"u8), durability: targets);
        targets[0x3456] = 0.75d;

        Assert.Equal(0.25d, plan.Durability[0x3456]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<uint, double>)plan.Durability).Add(0x4567, 0.5d));
    }

    [Fact]
    public void Rejects_unknown_item_handle()
    {
        var vector = ReadVector("stalker-cop");
        var source = ReadFixture(GetString(vector, "source"));
        using var manifest = ReadManifest();
        var invalidHandle = manifest.RootElement.GetProperty("negativeCases")
            .GetProperty("unknownItemHandle")
            .GetProperty("invalidHandle")
            .GetUInt32();
        var plan = new EditPlan(
            Sha256(source),
            durability: new Dictionary<uint, double> { [invalidHandle] = 0.8d });

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_durability_for_a_known_non_equipment_item()
    {
        var source = ReadFixture("xray-call-of-pripyat-base-item.sav");
        var parsed = XRayTrilogyReader.FromBytes(source);
        var item = Assert.Single(parsed.Inventory);
        Assert.False(item.ConditionEditable);
        var plan = new EditPlan(
            Sha256(source),
            durability: new Dictionary<uint, double> { [item.Handle] = 0.5d });

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_a_stale_source_before_writing_durability()
    {
        var source = ReadFixture("xray-durability-cop-source.sav");
        var plan = new EditPlan(
            new string('0', 64),
            durability: new Dictionary<uint, double> { [0x3456] = 0.75d });

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(source, plan));
    }

    [Fact]
    public void Rejects_an_s2_stash_transfer_in_an_xray_durability_plan()
    {
        var source = ReadFixture("xray-durability-cop-source.sav");
        var plan = new EditPlan(
            Sha256(source),
            stalker2StashTakeHandle: 0x30000010,
            durability: new Dictionary<uint, double> { [0x3456] = 0.75d });

        Assert.Throws<XRayFormatException>(() => XRayDurabilityWriter.Prepare(source, plan));
    }

    private static JsonElement ReadVector(string releaseId, string? variant = null)
    {
        using var manifest = ReadManifest();
        return Assert.Single(
            manifest.RootElement.GetProperty("vectors").EnumerateArray(),
            vector => GetString(vector, "releaseId") == releaseId &&
                (variant is null
                    ? !vector.TryGetProperty("variant", out _)
                    : vector.TryGetProperty("variant", out var itemVariant) && itemVariant.GetString() == variant)).Clone();
    }

    private static JsonDocument ReadManifest() => JsonDocument.Parse(ReadFixture("xray-durability-vectors.json"));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        name.StartsWith("xray-call-of-pripyat-base-item", StringComparison.Ordinal)
            ? name
            : Path.Combine("writer-durability", name)));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Fixture property '{property}' is missing.");

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
