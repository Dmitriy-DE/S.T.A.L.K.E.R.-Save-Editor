using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class InventoryUpgradeOrderTests
{
    // Real CoP saves keep upgrades in install order, which differs from the upgrade tree order.
    private static InventoryLineViewModel Weapon() => new(
        "Винторез", "wpn_vintorez", 7, "weapon", null, false, 1f, false, "ruck", false,
        ["up_thirda_vintorez", "up_firsta_vintorez", "up_secona_vintorez"], canEditUpgrades: true);

    [Fact]
    public void Unchanged_upgrades_in_another_order_are_not_a_change()
    {
        var weapon = Weapon();
        foreach (var upgrade in weapon.UpgradeItems.OrderBy(u => u.Key, StringComparer.Ordinal).ToArray())
        {
            upgrade.IsInstalled = false;
            upgrade.IsInstalled = true;
        }

        Assert.False(weapon.UpgradesChanged);
    }

    [Fact]
    public void Writes_the_saved_order_and_appends_new_upgrades()
    {
        var weapon = Weapon();
        weapon.UpgradeItems.Single(u => u.Key == "up_firsta_vintorez").IsInstalled = false;

        Assert.True(weapon.UpgradesChanged);
        Assert.Equal(["up_thirda_vintorez", "up_secona_vintorez"], weapon.UpgradesToWrite());
    }
}
