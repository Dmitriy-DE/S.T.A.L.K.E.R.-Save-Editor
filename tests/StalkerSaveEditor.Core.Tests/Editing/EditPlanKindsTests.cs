using StalkerSaveEditor.Core.Editing;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class EditPlanKindsTests
{
    [Fact]
    public void Reports_each_present_edit_kind_as_a_flag()
    {
        var plan = new EditPlan(
            new string('a', 64),
            money: 100,
            stackCounts: new Dictionary<uint, uint> { [1] = 5 },
            detachHandles: [2],
            adds: [new ItemAddRequest("bandage")],
            stashTakes: [3],
            stashPuts: [new StashPutRequest(4, 5)],
            upgrades: new Dictionary<ushort, IReadOnlyList<string>> { [6] = ["upgrade_a"] });

        Assert.Equal(
            EditKind.Money |
            EditKind.StackCounts |
            EditKind.Delete |
            EditKind.Add |
            EditKind.XRayStashTransfer |
            EditKind.Upgrades,
            plan.EditKinds);
    }

    [Fact]
    public void Reports_no_edits_for_an_empty_plan()
    {
        var plan = new EditPlan(new string('b', 64));

        Assert.Equal(EditKind.None, plan.EditKinds);
    }
}
