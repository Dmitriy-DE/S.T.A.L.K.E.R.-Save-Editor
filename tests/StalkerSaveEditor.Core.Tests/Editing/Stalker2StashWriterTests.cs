using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class Stalker2StashWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-stash");

    [Fact]
    public void Stash_to_player_raw_bytes_match_python_oracle()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "sourceRaw"));
        var expected = ReadFixture(GetString(vector, "expectedRaw"));
        var handle = vector.GetProperty("handle").GetUInt32();
        var sourceRecordOffset = vector.GetProperty("sourceRecordOffset").GetInt32();
        var expectedRecordOffset = vector.GetProperty("expectedRecordOffset").GetInt32();

        var actual = Stalker2StashWriter.TransferToPlayerRaw(source, handle);

        Assert.Equal(expected, actual);
        Assert.Equal(GetString(vector, "expectedRawSha256"), Sha256(actual));
        Assert.Equal(
            (byte)vector.GetProperty("sourceStashFlag").GetInt32(),
            source[sourceRecordOffset + 15]);
        Assert.Equal(
            (byte)vector.GetProperty("expectedStashFlag").GetInt32(),
            actual[expectedRecordOffset + 15]);
        Assert.Equal(
            (byte)vector.GetProperty("expectedObjectFlags").GetInt32(),
            actual[expectedRecordOffset + 28]);
        Assert.Equal(
            (byte)(vector.GetProperty("sourceObjectFlags").GetInt32() & ~0x08),
            actual[expectedRecordOffset + 28]);
        var stash = Stalker2StashReader.Locate(actual);
        Assert.Equal(new[] { uint.MaxValue, uint.MaxValue }, stash.OwnedHandles);
        Assert.Empty(stash.GridCells);
        var player = Stalker2InventoryReader.LocateLayout(actual);
        Assert.Equal(handle, player.OwnedHandles[^1]);
        Assert.Equal(new[] { (1, 0), (1, 1) }, player.GridCells
            .Where(cell => cell.Handle == handle)
            .Select(cell => ((int)cell.X, (int)cell.Y)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("truncated")]
    public void Rejects_missing_ambiguous_or_truncated_stash_arrays(string fixtureKey)
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var file = GetString(vector.GetProperty("negativeFixtures"), fixtureKey);
        var save = ReadFixture(file);
        var raw = Stalker2SaveReader.FromBytes(save).Raw;
        var handle = vector.GetProperty("handle").GetUInt32();

        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2StashWriter.TransferToPlayerRaw(raw.Span, handle));
    }

    [Fact]
    public void Rejects_a_foreign_handle_and_an_unflagged_stash_record()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "sourceRaw"));
        var handle = vector.GetProperty("handle").GetUInt32();
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2StashWriter.TransferToPlayerRaw(source, 0x30000001));

        var unflagged = Stalker2SaveReader.FromBytes(
            ReadFixture(GetString(vector.GetProperty("negativeFixtures"), "unflagged"))).Raw;
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2StashWriter.TransferToPlayerRaw(unflagged.Span, handle));

        var missingFlagBit = Stalker2SaveReader.FromBytes(
            ReadFixture(GetString(vector.GetProperty("negativeFixtures"), "flagBitMissing"))).Raw;
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2StashWriter.TransferToPlayerRaw(missingFlagBit.Span, handle));
    }

    [Fact]
    public void Public_prepare_keeps_s2_item_moves_disabled_by_the_python_capability_snapshot()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var handle = vector.GetProperty("handle").GetUInt32();
        var support = CapabilityRegistry.Get("stalker2", "move_items");

        Assert.Equal(CapabilityMaturity.Unsupported, support.Maturity);
        Assert.Throws<Stalker2FormatException>(() => Stalker2StashWriter.Prepare(
            source,
            new EditPlan(Sha256(source), stalker2StashTakeHandle: handle)));
    }

    [Fact]
    public void Rejects_a_stale_source_and_mixed_stash_plan()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var handle = vector.GetProperty("handle").GetUInt32();

        Assert.Throws<Stalker2FormatException>(() => Stalker2StashWriter.Prepare(
            source,
            new EditPlan(new string('0', 64), stalker2StashTakeHandle: handle)));
        Assert.Throws<Stalker2FormatException>(() => Stalker2StashWriter.Prepare(
            source,
            new EditPlan(Sha256(source), money: 100, stalker2StashTakeHandle: handle)));
    }

    [Fact]
    public void Rejects_durability_when_mixed_with_an_s2_stash_transfer()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var handle = vector.GetProperty("handle").GetUInt32();
        var plan = new EditPlan(
            Sha256(source),
            stalker2StashTakeHandle: handle,
            durability: new Dictionary<uint, double> { [handle] = 0.75d });

        var error = Assert.Throws<Stalker2FormatException>(() =>
            Stalker2StashWriter.Prepare(source, plan));

        Assert.Contains("does not accept other edits", error.Message, StringComparison.Ordinal);
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "s2-stash-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()!;

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
