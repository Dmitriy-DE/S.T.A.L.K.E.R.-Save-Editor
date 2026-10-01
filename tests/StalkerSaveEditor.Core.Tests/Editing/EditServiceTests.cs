using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class EditServiceTests
{
    private static readonly byte[] XRayCoPSource = File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-stacks",
        "xray-stack-cop-source.sav"));

    private static readonly byte[] S2Source = File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-s2-money",
        "s2-money-source.sav"));

    [Fact]
    public void A_plan_that_edits_and_removes_the_same_item_is_refused_before_any_output()
    {
        var plan = new EditPlan(Sha256(XRayCoPSource), stackCounts: new Dictionary<uint, uint> { [0x1234] = 100 }, detachHandles: [0x1234]);

        var error = Assert.Throws<InvalidOperationException>(() => EditService.PrepareEdit(XRayCoPSource, plan, "stalker-cop"));

        Assert.Contains("0x1234", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_xray_save_refuses_a_stalker2_operation_instead_of_dropping_it()
    {
        var plan = new EditPlan(Sha256(XRayCoPSource), money: 1, stalker2StashTakeHandle: 123);

        Assert.Throws<NotSupportedException>(() => EditService.PrepareEdit(XRayCoPSource, plan, "stalker-cop"));
    }

    [Fact]
    public void Prepare_and_read_back_agree_on_the_release_including_aliases()
    {
        var plan = new EditPlan(Sha256(XRayCoPSource), money: 4_242);

        // A wrong explicit release is refused before anything is prepared.
        Assert.Throws<InvalidOperationException>(() => EditService.PrepareEdit(XRayCoPSource, plan, "stalker-soc"));

        // An alias is accepted by both sides.
        var prepared = EditService.PrepareEdit(XRayCoPSource, plan, "cop");
        EditService.VerifyReadBack(prepared.Data.Span, "cop", plan);
        EditService.VerifyReadBack(prepared.Data.Span, "stalker-cop", plan);
        Assert.Throws<InvalidDataException>(() => EditService.VerifyReadBack(prepared.Data.Span, "stalker-soc", plan));
    }

    [Theory]
    [InlineData("stalker-soc", true)]
    [InlineData("stalker-soc-ee", true)]
    [InlineData("stalker-cs", true)]
    [InlineData("stalker-cs-ee", true)]
    [InlineData("stalker-cop", true)]
    [InlineData("stalker-cop-ee", true)]
    [InlineData("soc", true)]
    [InlineData("clear_sky", true)]
    [InlineData("cop", true)]
    [InlineData("stalker2", false)]
    [InlineData("s2", false)]
    [InlineData("doom", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsXRayRelease_classifies_supported_releases(string? releaseId, bool expected)
    {
        Assert.Equal(expected, EditService.IsXRayRelease(releaseId));
    }

    [Theory]
    [InlineData("stalker2", true)]
    [InlineData("s2", true)]
    [InlineData("stalker-cop", false)]
    [InlineData("stalker-soc", false)]
    [InlineData("doom", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStalker2Release_classifies_s2_releases(string? releaseId, bool expected)
    {
        Assert.Equal(expected, EditService.IsStalker2Release(releaseId));
    }

    [Fact]
    public void CanEdit_uses_registered_s2_capabilities()
    {
        Assert.True(EditService.CanEdit("stalker2"));
        Assert.True(EditService.CanEdit("stalker2", EditKind.Money));
        Assert.True(EditService.CanEdit("s2", EditKind.StackCounts));
        Assert.True(EditService.CanEdit("stalker2", EditKind.Durability));
        Assert.False(EditService.CanEdit("stalker2", EditKind.Add));
        Assert.False(EditService.CanEdit("stalker2", EditKind.Stalker2StashTransfer));
        Assert.False(EditService.CanEdit("stalker-soc", EditKind.Upgrades));
        Assert.False(EditService.CanEdit("not-a-release", EditKind.Money));
    }

    [Theory]
    [InlineData("stalker-cop", true)]
    [InlineData("stalker-cs", true)]
    [InlineData("stalker-soc", true)]
    [InlineData("stalker-cop-ee", true)]
    [InlineData("unknown-game", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanEdit_checks_xray_releases(string? releaseId, bool expected)
    {
        Assert.Equal(expected, EditService.CanEdit(releaseId));
    }

    [Fact]
    public void CanEdit_verifies_specific_edit_kinds()
    {
        Assert.True(EditService.CanEdit("stalker-cop", EditKind.Money | EditKind.StackCounts));
        Assert.True(EditService.CanEdit("stalker-cs", EditKind.Upgrades));
        Assert.False(EditService.CanEdit("stalker-soc", EditKind.Upgrades)); // SoC has no upgrade mechanic
    }

    [Fact]
    public void DetectFormat_identifies_xray_and_s2_saves()
    {
        Assert.Equal("stalker-cop", EditService.DetectFormat(XRayCoPSource));
        Assert.Equal("stalker2", EditService.DetectFormat(S2Source));
        Assert.Null(EditService.DetectFormat(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Fact]
    public void PrepareEdit_dispatches_to_xray_writer()
    {
        var plan = new EditPlan(Sha256(XRayCoPSource), money: 55_000);
        var prepared = EditService.PrepareEdit(XRayCoPSource, plan, "stalker-cop");

        var parsed = XRayTrilogyReader.FromBytes(prepared.Data.Span);
        Assert.Equal(55_000u, parsed.Money);
        Assert.Equal(Sha256(XRayCoPSource), prepared.SourceSha256);
        Assert.Equal(Sha256(prepared.Data.Span), prepared.OutputSha256);
    }

    [Fact]
    public void PrepareEdit_dispatches_to_s2_writer()
    {
        var plan = new EditPlan(Sha256(S2Source), money: 999_888);
        var prepared = EditService.PrepareEdit(S2Source, plan, "stalker2");

        var parsed = Stalker2SaveReader.FromBytes(prepared.Data.Span);
        Assert.True(parsed.CrcOk);
        Assert.Equal(999_888u, parsed.Money);
        Assert.Equal(Sha256(S2Source), prepared.SourceSha256);
        Assert.Equal(Sha256(prepared.Data.Span), prepared.OutputSha256);
    }

    [Fact]
    public void PrepareEdit_auto_detects_format_when_release_is_omitted()
    {
        var xrayPlan = new EditPlan(Sha256(XRayCoPSource), money: 12_345);
        var preparedXray = EditService.PrepareEdit(XRayCoPSource, xrayPlan);
        Assert.Equal(12_345u, XRayTrilogyReader.FromBytes(preparedXray.Data.Span).Money);

        var s2Plan = new EditPlan(Sha256(S2Source), money: 54_321);
        var preparedS2 = EditService.PrepareEdit(S2Source, s2Plan);
        Assert.Equal(54_321u, Stalker2SaveReader.FromBytes(preparedS2.Data.Span).Money);
    }

    [Fact]
    public void VerifyReadBack_succeeds_on_matching_xray_save()
    {
        var plan = new EditPlan(Sha256(XRayCoPSource), money: 44_000);
        var prepared = EditService.PrepareEdit(XRayCoPSource, plan, "stalker-cop");

        // Should not throw
        EditService.VerifyReadBack(prepared.Data.Span, "stalker-cop", plan);
    }

    [Fact]
    public void VerifyReadBack_throws_on_mismatched_xray_money()
    {
        var plan = new EditPlan(Sha256(XRayCoPSource), money: 44_000);
        var differentPlan = new EditPlan(Sha256(XRayCoPSource), money: 99_999);
        var prepared = EditService.PrepareEdit(XRayCoPSource, plan, "stalker-cop");

        Assert.Throws<InvalidDataException>(() =>
            EditService.VerifyReadBack(prepared.Data.Span, "stalker-cop", differentPlan));
    }

    [Fact]
    public void VerifyReadBack_succeeds_on_matching_s2_save()
    {
        var plan = new EditPlan(Sha256(S2Source), money: 777_111);
        var prepared = EditService.PrepareEdit(S2Source, plan, "stalker2");

        // Should not throw
        EditService.VerifyReadBack(prepared.Data.Span, "stalker2", plan);
    }

    [Fact]
    public void VerifyReadBack_throws_on_mismatched_s2_money()
    {
        var plan = new EditPlan(Sha256(S2Source), money: 777_111);
        var differentPlan = new EditPlan(Sha256(S2Source), money: 111_222);
        var prepared = EditService.PrepareEdit(S2Source, plan, "stalker2");

        Assert.Throws<InvalidDataException>(() =>
            EditService.VerifyReadBack(prepared.Data.Span, "stalker2", differentPlan));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
