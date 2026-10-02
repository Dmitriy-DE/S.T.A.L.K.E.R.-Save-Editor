using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class InventoryPilesTests
{
    private static InventoryLineViewModel Line(string type, uint handle, uint? count = null, float? condition = null, string placement = "ruck") =>
        new(type, type, handle, "other", count, canEditCount: count is not null, condition, canEditCondition: condition is not null,
            placement, canEditPlacement: true, upgrades: null, canEditUpgrades: false);

    [Fact]
    public void Identical_single_objects_are_one_row_with_a_count()
    {
        var first = Line("grenade_rgd5", 1);
        var second = Line("grenade_rgd5", 2);
        var other = Line("grenade_f1", 3);

        var shown = InventoryLineViewModel.GroupPiles([first, other, second], selected: null);

        Assert.Equal([first, other], shown);
        Assert.Equal(2, first.GroupSize);
        Assert.Equal("× 2", first.CountDisplay);
        Assert.Equal("2", first.QuantityDisplay);
        Assert.Equal(1, other.GroupSize);
        Assert.Equal(string.Empty, other.CountDisplay);
    }

    [Fact]
    public void Real_stacks_and_objects_that_differ_stay_apart()
    {
        var ammo = Line("ammo_9x18_fmj", 1, count: 50);
        var moreAmmo = Line("ammo_9x18_fmj", 2, count: 50);
        var worn = Line("wpn_pm", 3, condition: 0.5f);
        var fresh = Line("wpn_pm", 4, condition: 1f);
        var onBelt = Line("af_medusa", 5, placement: "belt");
        var inRuck = Line("af_medusa", 6);

        var shown = InventoryLineViewModel.GroupPiles([ammo, moreAmmo, worn, fresh, onBelt, inRuck], selected: null);

        Assert.Equal(6, shown.Count);
        Assert.All(shown, line => Assert.Equal(1, line.GroupSize));
    }

    [Fact]
    public void The_selected_object_is_the_visible_one_of_its_pile_and_an_edit_takes_it_out()
    {
        var first = Line("medkit", 1, condition: 1f);
        var second = Line("medkit", 2, condition: 1f);
        var third = Line("medkit", 3, condition: 1f);

        var shown = InventoryLineViewModel.GroupPiles([first, second, third], selected: second);
        Assert.Same(second, Assert.Single(shown));
        Assert.Equal(3, second.GroupSize);
        Assert.Equal(1, first.GroupSize);

        second.ConditionPercent = 40;
        shown = InventoryLineViewModel.GroupPiles([first, second, third], selected: second);
        Assert.Equal([first, second], shown);
        Assert.Equal(2, first.GroupSize);
        Assert.Equal(1, second.GroupSize);

        first.IsDeleted = true;
        shown = InventoryLineViewModel.GroupPiles([second, third], selected: second);
        Assert.Equal(1, third.GroupSize);
    }
}
