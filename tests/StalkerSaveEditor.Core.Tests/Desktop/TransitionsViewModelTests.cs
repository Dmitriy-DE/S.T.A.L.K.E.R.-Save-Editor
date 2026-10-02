using Xunit;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class TransitionsViewModelTests
{
    [Fact]
    public void TransitionViewModel_RegistryProperties_DisplayCorrectly()
    {
        var vm = new TransitionViewModel(
            handle: 0xF001,
            name: "level_changer",
            nameReplace: "l01_escape_level_changer_0001",
            parentId: 0,
            objectVersion: 118);

        Assert.Equal((ushort)0xF001, vm.Handle);
        Assert.Equal("level_changer", vm.Name);
        Assert.Equal("l01_escape_level_changer_0001", vm.NameReplace);
        Assert.Equal((ushort)0, vm.ParentId);
        Assert.Equal(118, vm.ObjectVersion);

        Assert.Equal("l01_escape_level_changer_0001", vm.DisplayName);
        Assert.Equal("0xF001 (61441)", vm.HandleDisplay);
        Assert.Equal("level_changer", vm.TypeDisplay);
        Assert.Equal("0 (мир)", vm.ParentDisplay);
        Assert.Equal("v118", vm.VersionDisplay);
        Assert.Equal("Только чтение", vm.StatusDisplay);
    }

    [Fact]
    public void TransitionViewModel_WhenNameReplaceIsEmpty_FallsBackToNameOrHandle()
    {
        var vm = new TransitionViewModel(
            handle: 0x0042,
            name: "level_changer",
            nameReplace: string.Empty,
            parentId: 0x0010,
            objectVersion: 53);

        Assert.Equal("level_changer", vm.DisplayName);
        Assert.Equal("0x0042 (66)", vm.HandleDisplay);
        Assert.Equal("0x0010 (16)", vm.ParentDisplay);
        Assert.Equal("v53", vm.VersionDisplay);
    }

    [Fact]
    public void SaveFileSummary_TransitionsProperties_ReportAccurately()
    {
        var emptySummary = new SaveFileSummary(
            filePath: "/fake/save.sav",
            releaseName: "S.T.A.L.K.E.R.: Shadow of Chernobyl",
            releaseId: "stalker-soc",
            sourceSha256: "fake-sha",
            money: 5000,
            canEditMoney: true,
            inventory: [],
            fileSizeBytes: 1024,
            lastModified: DateTime.UtcNow,
            transitions: []);

        Assert.Empty(emptySummary.Transitions);
        Assert.False(emptySummary.HasTransitions);
        Assert.True(emptySummary.HasNoTransitions);

        var transition = new TransitionViewModel(0x100, "level_changer", "exit_to_garbage");
        var populatedSummary = new SaveFileSummary(
            filePath: "/fake/save2.sav",
            releaseName: "S.T.A.L.K.E.R.: Call of Pripyat",
            releaseId: "stalker-cop",
            sourceSha256: "fake-sha-2",
            money: 10000,
            canEditMoney: true,
            inventory: [],
            fileSizeBytes: 2048,
            lastModified: DateTime.UtcNow,
            transitions: [transition]);

        Assert.Single(populatedSummary.Transitions);
        Assert.True(populatedSummary.HasTransitions);
        Assert.False(populatedSummary.HasNoTransitions);
        Assert.Equal("exit_to_garbage", populatedSummary.Transitions[0].DisplayName);
    }
}
