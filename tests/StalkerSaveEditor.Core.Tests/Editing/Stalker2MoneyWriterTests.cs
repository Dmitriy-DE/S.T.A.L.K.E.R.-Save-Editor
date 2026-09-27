using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class Stalker2MoneyWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-money");

    [Fact]
    public void Money_write_matches_python_container_and_unpacked_bytes()
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement;
        var source = ReadFixture(GetString(vector, "source"));
        var expected = ReadFixture(GetString(vector, "expected"));
        var expectedRaw = ReadFixture(GetString(vector, "expectedRaw"));
        var plan = new EditPlan(GetString(vector, "sourceSha256"), vector.GetProperty("money").GetUInt32());

        var prepared = Stalker2MoneyWriter.Prepare(source, plan);

        Assert.Equal(expected, prepared.Data.ToArray());
        Assert.Equal(expectedRaw, Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.ToArray());
        Assert.Equal(GetString(vector, "sourceSha256"), prepared.SourceSha256);
        Assert.Equal(
            GetString(vector, "expectedSha256"),
            Convert.ToHexString(SHA256.HashData(prepared.Data.Span)).ToLowerInvariant());
        Assert.Equal(GetString(vector, "expectedRawSha256"), Sha256(expectedRaw));

        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.Equal(vector.GetProperty("money").GetUInt32(), parsed.Money);
        Assert.True(parsed.CrcOk);
        Assert.Equal((byte)99, Assert.Single(parsed.Orphans, item => item.Handle == 0x30000004).KindCode);

        var sourceRaw = Stalker2SaveReader.FromBytes(source).Raw.Span;
        var actualRaw = parsed.Raw.Span;
        var moneyOffset = vector.GetProperty("moneyOffset").GetInt32();
        for (var index = 0; index < sourceRaw.Length; index++)
        {
            if (index < moneyOffset || index >= moneyOffset + sizeof(uint))
            {
                Assert.Equal(sourceRaw[index], actualRaw[index]);
            }
        }
    }

    [Fact]
    public void Writing_the_existing_money_preserves_the_source_bytes()
    {
        var source = ReadFixture("s2-money-source.sav");
        var parsed = Stalker2SaveReader.FromBytes(source);

        var prepared = Stalker2MoneyWriter.Prepare(
            source,
            new EditPlan(Sha256(source), parsed.Money));

        Assert.Equal(source, prepared.Data.ToArray());
    }

    [Fact]
    public void Rejects_a_source_whose_sha_does_not_match_the_edit_plan()
    {
        var source = ReadFixture("s2-money-source.sav");

        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(source, new EditPlan(new string('0', 64), 1234)));
    }

    [Fact]
    public void Rejects_money_above_the_python_writer_limit()
    {
        var source = ReadFixture("s2-money-source.sav");

        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(source, new EditPlan(Sha256(source), 2_000_000_001)));
    }

    [Fact]
    public void Rejects_plans_for_other_operations_and_plans_without_money()
    {
        var source = ReadFixture("s2-money-source.sav");
        var sourceSha = Sha256(source);

        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(source, new EditPlan(sourceSha)));
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(
                source,
                new EditPlan(sourceSha, 1234, stackCounts: new Dictionary<uint, uint> { [1] = 2 })));
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(
                source,
                new EditPlan(sourceSha, 1234, adds: [new ItemAddRequest("bandage")])));
    }

    [Fact]
    public void Rejects_foreign_formats_and_corrupt_containers()
    {
        var originalXray = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "xray-soc.sav"));
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(
                originalXray,
                new EditPlan(Sha256(originalXray), 1234)));

        var source = ReadFixture("s2-money-source.sav");
        source[^1] ^= 1;
        Assert.Throws<Stalker2FormatException>(() =>
            Stalker2MoneyWriter.Prepare(source, new EditPlan(Sha256(source), 1234)));
    }

    [Fact]
    public void S2_money_capability_matches_the_python_registry_snapshot()
    {
        using var manifest = ReadManifest();
        var expected = GetString(manifest.RootElement, "capability");

        Assert.Equal(
            expected,
            CapabilityRegistry.Get("stalker2", "edit_money").Maturity.ToString().ToLowerInvariant());
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "s2-money-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()!;

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
