using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class Stalker2DurabilityWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-equipment");

    [Theory]
    [InlineData("armor")]
    [InlineData("weapon")]
    public void Reads_s2_equipment_state_from_python_vectors(string caseName)
    {
        using var manifest = ReadManifest();
        var vector = ReadVector(manifest.RootElement, caseName);
        var source = ReadFixture(GetString(vector, "source"));
        var parsed = Stalker2SaveReader.FromBytes(source);
        var item = Assert.Single(parsed.Inventory, value => value.Handle == vector.GetProperty("handle").GetUInt32());

        Assert.Equal(vector.GetProperty("sourceCondition").GetSingle(), item.Condition);
        Assert.Equal(vector.GetProperty("conditionOffset").GetInt32(), item.ConditionOffset);
        Assert.Equal(vector.GetProperty("displayName").GetString(), item.DisplayName);
        Assert.True(item.ConditionEditable);
        AssertStringArray(vector, "modules", item.Modules);
        AssertStringArray(vector, "upgrades", item.Upgrades);
    }

    [Theory]
    [InlineData("armor")]
    [InlineData("weapon")]
    public void Condition_write_matches_python_container_and_unpacked_bytes(string caseName)
    {
        using var manifest = ReadManifest();
        var vector = ReadVector(manifest.RootElement, caseName);
        var source = ReadFixture(GetString(vector, "source"));
        var target = vector.GetProperty("targetCondition").GetDouble();
        var handle = vector.GetProperty("handle").GetUInt32();
        var plan = new EditPlan(
            GetString(vector, "sourceSha256"),
            durability: new Dictionary<uint, double> { [handle] = target });

        var prepared = Stalker2DurabilityWriter.Prepare(source, plan);

        Assert.Equal(ReadFixture(GetString(vector, "expected")), prepared.Data.ToArray());
        Assert.Equal(ReadFixture(GetString(vector, "expectedRaw")),
            Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(GetString(vector, "expectedSha256"), Sha256(prepared.Data.Span));
        Assert.Equal(GetString(vector, "expectedRawSha256"),
            Sha256(Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.Span));
        Assert.Equal(vector.GetProperty("changedRawOffsets").EnumerateArray().Select(value => value.GetInt32()),
            ChangedOffsets(
                Stalker2SaveReader.FromBytes(source).Raw.Span,
                Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.Span));
        var edited = Assert.Single(
            Stalker2SaveReader.FromBytes(prepared.Data.Span).Inventory,
            value => value.Handle == handle);
        Assert.Equal((float)target, edited.Condition);
        Assert.True(Stalker2SaveReader.FromBytes(prepared.Data.Span).CrcOk);
    }

    [Fact]
    public void Durability_writer_matches_python_when_combined_with_money()
    {
        using var manifest = ReadManifest();
        var vector = ReadVector(manifest.RootElement, "armor-money");
        var source = ReadFixture(GetString(vector, "source"));
        var handle = vector.GetProperty("handle").GetUInt32();
        var plan = new EditPlan(
            GetString(vector, "sourceSha256"),
            money: vector.GetProperty("money").GetUInt32(),
            durability: new Dictionary<uint, double>
            {
                [handle] = vector.GetProperty("targetCondition").GetDouble(),
            });

        var prepared = Stalker2DurabilityWriter.Prepare(source, plan);

        Assert.Equal(ReadFixture(GetString(vector, "expected")), prepared.Data.ToArray());
        Assert.Equal(ReadFixture(GetString(vector, "expectedRaw")),
            Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.ToArray());
        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.Equal(vector.GetProperty("money").GetUInt32(), parsed.Money);
        Assert.Equal((float)vector.GetProperty("targetCondition").GetDouble(),
            Assert.Single(parsed.Inventory, item => item.Handle == handle).Condition);
    }

    [Fact]
    public void Rejects_unknown_handles_stale_hashes_and_other_edit_kinds()
    {
        using var manifest = ReadManifest();
        var vector = ReadVector(manifest.RootElement, "armor");
        var source = ReadFixture(GetString(vector, "source"));
        var sourceSha = Sha256(source);
        var value = vector.GetProperty("targetCondition").GetDouble();

        Assert.Throws<Stalker2FormatException>(() => Stalker2DurabilityWriter.Prepare(
            source,
            new EditPlan(sourceSha, durability: new Dictionary<uint, double> { [0x3000_FFFF] = value })));
        Assert.Throws<Stalker2FormatException>(() => Stalker2DurabilityWriter.Prepare(
            source,
            new EditPlan(new string('0', 64), durability: new Dictionary<uint, double>
            {
                [vector.GetProperty("handle").GetUInt32()] = value,
            })));
        Assert.Throws<Stalker2FormatException>(() => Stalker2DurabilityWriter.Prepare(
            source,
            new EditPlan(sourceSha, durability: new Dictionary<uint, double>
            {
                [vector.GetProperty("handle").GetUInt32()] = value,
            }, stashTakes: [1])));
    }

    [Fact]
    public void Rejects_an_armor_with_a_non_armor_name()
    {
        using var manifest = ReadManifest();
        var vector = ReadVector(manifest.RootElement, "armor");
        var source = ReadFixture(GetString(vector, "source"));
        var parsed = Stalker2SaveReader.FromBytes(source);
        var item = Assert.Single(parsed.Inventory, value => value.Handle == vector.GetProperty("handle").GetUInt32());
        var raw = parsed.Raw.ToArray();
        raw[item.RecordOffset + 9] = 0;
        raw[item.RecordOffset + 10] = 0;
        var invalidSource = Stalker2ContainerWriter.Build(raw, KrakenCodec.Compress(raw));
        var invalidParsed = Stalker2SaveReader.FromBytes(invalidSource);
        var invalidItem = Assert.Single(invalidParsed.Inventory, value => value.Handle == item.Handle);
        Assert.NotNull(invalidItem.Condition);
        Assert.False(invalidItem.ConditionEditable);

        Assert.Throws<Stalker2FormatException>(() => Stalker2DurabilityWriter.Prepare(
            invalidSource,
            new EditPlan(Sha256(invalidSource), durability: new Dictionary<uint, double>
            {
                [item.Handle] = 0.5,
            })));
    }

    [Fact]
    public void Rejects_malformed_nested_armor_and_weapon_vectors()
    {
        using var manifest = ReadManifest();
        var armorVector = ReadVector(manifest.RootElement, "armor");
        var armorSource = ReadFixture(GetString(armorVector, "source"));
        var armor = Stalker2SaveReader.FromBytes(armorSource);
        var armorItem = Assert.Single(armor.Inventory, value => value.Handle == armorVector.GetProperty("handle").GetUInt32());
        var badArmorRaw = armor.Raw.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(badArmorRaw.AsSpan(armorItem.RecordOffset + 0x23), 0x3000_FFFF);
        Assert.Null(Stalker2ItemState.ReadArmorCondition(
            badArmorRaw,
            armorItem.Handle,
            armorItem.RecordOffset,
            armorItem.KindCode));

        var weaponVector = ReadVector(manifest.RootElement, "weapon");
        var weaponSource = ReadFixture(GetString(weaponVector, "source"));
        var weapon = Stalker2SaveReader.FromBytes(weaponSource);
        var weaponItem = Assert.Single(weapon.Inventory, value => value.Handle == weaponVector.GetProperty("handle").GetUInt32());
        var badWeaponRaw = weapon.Raw.ToArray();
        badWeaponRaw[weaponItem.ConditionOffset!.Value + 8] = 0xFF;
        Assert.Null(Stalker2ItemState.ReadWeaponCondition(
            badWeaponRaw,
            weaponItem.Handle,
            weaponItem.RecordOffset,
            weaponItem.RecordEndGuess,
            weaponItem.KindCode,
            weapon.NameTables!));
    }

    [Fact]
    public void Rejects_invalid_condition_values_and_matches_python_capability()
    {
        using var manifest = ReadManifest();
        var vector = ReadVector(manifest.RootElement, "armor");
        var source = ReadFixture(GetString(vector, "source"));
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -0.01, 1.01 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EditPlan(
                Sha256(source),
                durability: new Dictionary<uint, double>
                {
                    [vector.GetProperty("handle").GetUInt32()] = invalid,
                }));
        }

        Assert.Equal(
            GetString(vector, "capability"),
            CapabilityRegistry.Get("stalker2", "edit_durability").Maturity.ToString().ToLowerInvariant());
        Assert.Throws<KeyNotFoundException>(() => CapabilityRegistry.Get("stalker2", "edit_upgrades"));
    }

    private static JsonDocument ReadManifest() => JsonDocument.Parse(
        ReadFixture("s2-equipment-vectors.json"));

    private static JsonElement ReadVector(JsonElement manifest, string caseName) =>
        Assert.Single(manifest.GetProperty("vectors").EnumerateArray(),
            vector => GetString(vector, "case") == caseName).Clone();

    private static string[]? ReadStringArray(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()!;

    private static int[] ChangedOffsets(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        Assert.Equal(before.Length, after.Length);
        var changed = new List<int>();
        for (var index = 0; index < before.Length; index++)
        {
            if (before[index] != after[index]) changed.Add(index);
        }

        return changed.ToArray();
    }

    private static void AssertStringArray(JsonElement element, string property, IReadOnlyList<string>? actual)
    {
        var expected = ReadStringArray(element, property);
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected, actual!.ToArray());
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
