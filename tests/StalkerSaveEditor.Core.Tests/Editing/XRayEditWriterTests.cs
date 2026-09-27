using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class XRayEditWriterTests
{
    [Fact]
    public void Combines_money_and_stack_edits_as_sequential_verified_writes()
    {
        var source = ReadFixture();
        var plan = new EditPlan(
            Sha256(source),
            money: 9_876,
            stackCounts: new Dictionary<uint, uint> { [0x1234] = 44 });

        var prepared = XRayEditWriter.Prepare(source, plan);
        var parsed = XRayTrilogyReader.FromBytes(prepared.Data.Span);

        Assert.Equal(9_876u, parsed.Money);
        Assert.Equal((ushort?)44, Assert.Single(parsed.Inventory).Count);
        Assert.Equal(Sha256(source), prepared.SourceSha256);
        Assert.Equal(Sha256(prepared.Data.Span), prepared.OutputSha256);
        Assert.Equal(plan, prepared.Plan);
    }

    [Fact]
    public void Rejects_a_stale_source_before_editing()
    {
        var source = ReadFixture();
        var plan = new EditPlan(
            new string('0', 64),
            money: 9_876,
            stackCounts: new Dictionary<uint, uint> { [0x1234] = 44 });

        Assert.Throws<XRayFormatException>(() => XRayEditWriter.Prepare(source, plan));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ReadFixture() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-stacks",
        "xray-stack-cop-source.sav"));
}
