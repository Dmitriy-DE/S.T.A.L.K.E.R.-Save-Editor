using System.Text.Json;
using System.Security.Cryptography;
using System.Buffers.Binary;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayPlacementTests
{
    [Theory]
    [InlineData("stalker-soc", false)]
    [InlineData("stalker-cs", false)]
    [InlineData("stalker-cop", false)]
    [InlineData("stalker-soc-ee", true)]
    [InlineData("stalker-cs-ee", true)]
    [InlineData("stalker-cop-ee", true)]
    public void Reader_matches_python_placement_vectors(string releaseId, bool enhanced)
    {
        var vector = ReadVector(releaseId);
        var source = ReadFixture(GetString(vector, "source"));
        var parsed = enhanced
            ? XRayEnhancedReader.FromBytes(source)
            : XRayTrilogyReader.FromBytes(source);
        var item = Assert.Single(parsed.Inventory, candidate => candidate.Handle ==
            vector.GetProperty("handle").GetUInt16());
        var target = vector.GetProperty("sourcePlacement");

        Assert.Equal(target.GetProperty("type").GetString(), item.PlacementType);
        Assert.Equal((int?)target.GetProperty("slot").GetInt32(), item.PlacementSlot);
        Assert.Equal((int?)target.GetProperty("baseSlot").GetInt32(), item.PlacementBaseSlot);
        Assert.Equal(target.GetProperty("storage").GetString(), item.PlacementStorage);
        Assert.True(item.PlacementEditable);
    }

    [Theory]
    [InlineData(0x0C01)]
    [InlineData(0x3821)]
    public void Reader_leaves_invalid_equipment_slots_read_only(ushort invalidPlacement)
    {
        var vector = ReadVector("stalker-cop");
        var source = ReadFixture(GetString(vector, "source"));
        var parsed = XRayTrilogyReader.FromBytes(source);
        var sourceItem = Assert.Single(parsed.Inventory);
        var placementOffset = Assert.IsType<int>(sourceItem.PlacementOffset);
        var raw = parsed.Container.Raw.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(placementOffset), invalidPlacement);
        var malformed = parsed.Container.Build(raw);

        var reread = XRayTrilogyReader.FromBytes(malformed);
        var malformedItem = Assert.Single(reread.Inventory);

        Assert.False(malformedItem.PlacementEditable);
        Assert.Null(malformedItem.PlacementType);
        Assert.Null(malformedItem.PlacementSlot);
    }

    [Theory]
    [InlineData("stalker-soc", "")]
    [InlineData("stalker-cs", "")]
    [InlineData("stalker-cop", "")]
    [InlineData("stalker-soc", "belt")]
    [InlineData("stalker-cs", "belt")]
    [InlineData("stalker-cop", "belt")]
    [InlineData("stalker-soc", "ruck")]
    [InlineData("stalker-cs", "ruck")]
    [InlineData("stalker-cop", "ruck")]
    public void Writer_matches_python_container_and_raw_bytes(string releaseId, string variant)
    {
        var vector = ReadVector(releaseId, variant.Length == 0 ? null : variant);
        var source = ReadFixture(GetString(vector, "source"));
        var expected = ReadFixture(GetString(vector, "expected"));
        var expectedRaw = ReadFixture(GetString(vector, "expectedRaw"));
        var target = vector.GetProperty("targetPlacement");
        var targetType = GetString(target, "type");
        var plan = new EditPlan(
            Sha256(source),
            placements:
            [
                new XRayPlacementChange(
                    vector.GetProperty("handle").GetUInt32(),
                    targetType,
                    targetType == "slot" ? target.GetProperty("slot").GetInt32() : null),
            ]);

        var prepared = XRayEditWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(GetString(vector, "expectedSha256"), Sha256(prepared.Data.Span));
        var actualRaw = XRayContainer.FromBytes(prepared.Data.Span).Raw.ToArray();
        Assert.Equal(expectedRaw, actualRaw);
        var changedOffsets = GetSourceRaw(source)
            .Select((value, index) => (value, index))
            .Where(pair => pair.value != actualRaw[pair.index])
            .Select(pair => pair.index)
            .ToArray();
        Assert.Equal(vector.GetProperty("changedRawOffsets").EnumerateArray()
            .Select(offset => offset.GetInt32()).ToArray(), changedOffsets);
        var parsed = XRayTrilogyReader.FromBytes(prepared.Data.Span);
        var item = Assert.Single(parsed.Inventory, candidate => candidate.Handle ==
            vector.GetProperty("handle").GetUInt16());
        Assert.Equal(targetType, item.PlacementType);
        Assert.Equal(target.GetProperty("slot").ValueKind == JsonValueKind.Null
            ? null
            : target.GetProperty("slot").GetInt32(), item.PlacementSlot);
        Assert.Equal(target.GetProperty("baseSlot").GetInt32(), item.PlacementBaseSlot);
        Assert.Equal(GetString(target, "storage"), item.PlacementStorage);
    }

    [Theory]
    [InlineData("stalker-soc", "experimental")]
    [InlineData("stalker-cs", "experimental")]
    [InlineData("stalker-cop", "experimental")]
    [InlineData("stalker-soc-ee", "unsupported")]
    [InlineData("stalker-cs-ee", "unsupported")]
    [InlineData("stalker-cop-ee", "unsupported")]
    public void Placement_capability_matches_python_registry(string releaseId, string expected)
    {
        var vector = ReadVector(releaseId);
        Assert.Equal(expected, vector.GetProperty("capabilities").GetProperty("edit_placement").GetString());
        Assert.Equal(expected, CapabilityRegistry.Get(releaseId, "edit_placement")
            .Maturity.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData("stalker-soc-ee")]
    [InlineData("stalker-cs-ee")]
    [InlineData("stalker-cop-ee")]
    public void Writer_rejects_unsupported_enhanced_placement(string releaseId)
    {
        var vector = ReadVector(releaseId);
        var source = ReadFixture(GetString(vector, "source"));
        var plan = PlacementPlan(source, vector.GetProperty("handle").GetUInt32(), "slot", 3);

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(source, plan));
    }

    [Fact]
    public void Writer_rejects_unknown_handles_and_stale_sources()
    {
        var vector = ReadVector("stalker-cop");
        var source = ReadFixture(GetString(vector, "source"));

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            PlacementPlan(source, 0x9999, "slot", 3)));
        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            new EditPlan(new string('0', 64), placements: [new XRayPlacementChange(
                vector.GetProperty("handle").GetUInt32(), "slot", 3)])));
    }

    [Fact]
    public void Writer_rejects_slots_the_game_would_not_use_and_belt_for_weapons()
    {
        var vector = ReadVector("stalker-cop");
        var source = ReadFixture(GetString(vector, "source"));
        var handle = vector.GetProperty("handle").GetUInt32();

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            PlacementPlan(source, handle, "slot", 12)));
        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            PlacementPlan(source, handle, "belt", null)));
    }

    [Fact]
    public void Plan_rejects_invalid_placement_types_slots_and_duplicate_handles()
    {
        Assert.Throws<ArgumentException>(() => new XRayPlacementChange(0x3456, "unknown", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XRayPlacementChange(0x3456, "slot", 99));
        Assert.Throws<ArgumentException>(() => new XRayPlacementChange(0x3456, "ruck", 3));
        Assert.Throws<ArgumentException>(() => new EditPlan(
            new string('0', 64),
            placements:
            [
                new XRayPlacementChange(0x3456, "ruck", null),
                new XRayPlacementChange(0x3456, "slot", 3),
            ]));
    }

    [Fact]
    public void Plan_copies_placement_changes()
    {
        var changes = new List<XRayPlacementChange> { new(0x3456, "ruck", null) };
        var plan = new EditPlan(new string('0', 64), placements: changes);
        changes.Clear();

        Assert.Single(plan.Placements);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<XRayPlacementChange>)plan.Placements).Add(new(0x4567, "ruck", null)));
    }

    [Fact]
    public void Other_format_writers_reject_placement_fields()
    {
        var xraySource = ReadFixture(GetString(ReadVector("stalker-cop"), "source"));
        var xrayPlan = new EditPlan(
            Sha256(xraySource),
            money: 500,
            placements: [new XRayPlacementChange(0x3456, "ruck", null)]);
        Assert.Throws<XRayFormatException>(() => XRayMoneyWriter.Prepare(xraySource, xrayPlan));

        var stalker2Source = ReadFixture("synthetic-s2.sav");
        var stalker2Plan = new EditPlan(
            Sha256(stalker2Source),
            money: 500,
            placements: [new XRayPlacementChange(0x3456, "ruck", null)]);
        Assert.Throws<Stalker2FormatException>(() => Stalker2MoneyWriter.Prepare(stalker2Source, stalker2Plan));
    }

    [Fact]
    public void Writer_rejects_items_without_an_exact_client_data_place_anchor()
    {
        var source = ReadFixture("xray-call-of-pripyat-base-item.sav");
        var parsed = XRayTrilogyReader.FromBytes(source);
        var item = Assert.Single(parsed.Inventory);
        Assert.False(item.PlacementEditable);

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(
            source,
            PlacementPlan(source, item.Handle, "ruck", null)));
    }

    private static byte[] GetSourceRaw(byte[] source) => XRayContainer.FromBytes(source).Raw.ToArray();

    private static EditPlan PlacementPlan(byte[] source, uint handle, string type, int? slot) =>
        new(Sha256(source), placements: [new XRayPlacementChange(handle, type, slot)]);

    private static JsonElement ReadVector(string releaseId, string? variant = null)
    {
        using var manifest = ReadManifest();
        return Assert.Single(
            manifest.RootElement.GetProperty("vectors").EnumerateArray(),
            vector => GetString(vector, "releaseId") == releaseId &&
                (variant is null
                    ? !vector.TryGetProperty("variant", out _)
                    : vector.TryGetProperty("variant", out var value) && value.GetString() == variant)).Clone();
    }

    private static JsonDocument ReadManifest() => JsonDocument.Parse(
        ReadFixture("xray-placement-vectors.json"));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        name.StartsWith("xray-call-of-pripyat-base-item", StringComparison.Ordinal) ||
        name == "synthetic-s2.sav"
            ? name
            : Path.Combine("writer-placement", name)));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidDataException($"Fixture property '{property}' is missing.");

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
