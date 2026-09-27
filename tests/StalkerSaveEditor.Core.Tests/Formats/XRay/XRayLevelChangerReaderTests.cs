using System.Text.Json;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayLevelChangerReaderTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "xray-level-changer");

    [Theory]
    [InlineData("soc")]
    [InlineData("cs")]
    [InlineData("cop")]
    [InlineData("legacy")]
    [InlineData("cp1251")]
    public void Reads_python_level_changer_vectors(string caseName)
    {
        using var golden = ReadGolden();
        var vector = Assert.Single(
            golden.RootElement.GetProperty("vectors").EnumerateArray(),
            candidate => GetString(candidate, "case") == caseName);
        var packet = ReadFixture(GetString(vector, "source"));

        var result = XRayLevelChangerReader.ParseStateSuffix(
            packet,
            vector.GetProperty("objectVersion").GetInt32());

        Assert.Equal(vector.GetProperty("objectVersion").GetInt32(), result.ObjectVersion);
        AssertNullableUInt16(vector.GetProperty("destGameVertexId"), result.DestGameVertexId);
        AssertNullableUInt32(vector.GetProperty("destLevelVertexId"), result.DestLevelVertexId);
        AssertVector(vector.GetProperty("destPosition"), result.DestPosition);
        AssertVector(vector.GetProperty("destDirection"), result.DestDirection);
        Assert.Equal(GetString(vector, "destLevelName"), result.DestLevelName);
        Assert.Equal(GetString(vector, "destLevelPointName"), result.DestLevelPointName);
        AssertNullableBoolean(vector.GetProperty("silent"), result.Silent);
        Assert.Equal(vector.GetProperty("consumedBytes").GetInt32(), result.ConsumedBytes);
        Assert.Equal(packet.Length, result.ConsumedBytes);
    }

    [Fact]
    public void Rejects_truncated_suffixes_and_invalid_versions()
    {
        var fullPacket = ReadFixture("synthetic-level-changer-soc.bin");
        Assert.Throws<XRayFormatException>(() => XRayLevelChangerReader.ParseStateSuffix(
            fullPacket.AsSpan(0, fullPacket.Length - 1),
            118));
        Assert.Throws<XRayFormatException>(() => XRayLevelChangerReader.ParseStateSuffix(fullPacket, -1));
        Assert.Throws<XRayFormatException>(() => XRayLevelChangerReader.ParseStateSuffix(fullPacket, 0x1_0000));
    }

    private static JsonDocument ReadGolden() => JsonDocument.Parse(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "golden", "xray-level-changer-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private static string GetString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()!;

    private static void AssertNullableUInt16(JsonElement expected, ushort? actual) =>
        Assert.Equal(
            expected.ValueKind == JsonValueKind.Null ? null : expected.GetUInt16(),
            actual);

    private static void AssertNullableUInt32(JsonElement expected, uint? actual) =>
        Assert.Equal(
            expected.ValueKind == JsonValueKind.Null ? null : expected.GetUInt32(),
            actual);

    private static void AssertNullableBoolean(JsonElement expected, bool? actual) =>
        Assert.Equal(
            expected.ValueKind == JsonValueKind.Null ? null : expected.GetBoolean(),
            actual);

    private static void AssertVector(JsonElement expected, XRayVector3? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(actual);
            return;
        }

        var values = expected.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        Assert.Equal(3, values.Length);
        Assert.Equal(new XRayVector3(values[0], values[1], values[2]), actual);
    }
}
