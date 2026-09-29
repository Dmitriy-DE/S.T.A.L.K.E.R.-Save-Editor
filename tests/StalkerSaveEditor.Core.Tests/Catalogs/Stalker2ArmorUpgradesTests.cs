using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Catalogs;

public sealed class Stalker2ArmorUpgradesTests
{
    [Fact]
    public void Save_upgrade_ids_resolve_to_effect_and_tier()
    {
        Assert.True(Stalker2ArmorUpgrades.Count > 600);
        Assert.Equal(new Stalker2ArmorUpgrade("psy", 1), Stalker2ArmorUpgrades.Find("Exoskeleton_Monolith_Armor_PSY_Left_2_2"));
        Assert.Null(Stalker2ArmorUpgrades.Find("wpn_upgrade_not_armor"));
    }

    [Fact]
    public void Known_upgrades_get_a_readable_name_and_unknown_keep_their_key()
    {
        Assert.Equal("Пси-защита · ур. 1", InventoryLineViewModel.UpgradeName("Exoskeleton_Monolith_Armor_PSY_Left_2_2"));
        Assert.Equal("some_weapon_upgrade", InventoryLineViewModel.UpgradeName("some_weapon_upgrade"));
        Assert.Equal("Приклад: скорость прицеливания", InventoryLineViewModel.UpgradeName("GunLavina_Upgrade_Stock_1"));
    }
}
