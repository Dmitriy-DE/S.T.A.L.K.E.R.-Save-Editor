using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class CompanionCatalogScriptTests
{
    private static ItemDefinition Item(string key, string category, string? name = "Name") =>
        new(key, name, category, null, null, null, null, [], "test", null, null, null, null);

    [Theory]
    [InlineData("ammo_base")]
    [InlineData("wpn_ak74_hud")]
    [InlineData("mp_ammo_9x19_fmj")]
    [InlineData("stationary_mgun")]
    [InlineData("helicopter_missile")]
    [InlineData("wpn_fake_missile")]
    [InlineData("explosive_barrel")]
    [InlineData("weapon_probability")]
    [InlineData("ammo_12x76_zhekan_heli")]
    public void Leaves_out_sections_that_are_not_inventory_items(string key)
    {
        Assert.False(CompanionCatalogScript.IsSpawnable(Item(key, "item")));
    }

    [Fact]
    public void Writes_mod_items_by_category_and_skips_nameless_sections()
    {
        var script = CompanionCatalogScript.Render(new ItemCatalog("stalker-cs",
        [
            Item("wpn_abakan_ogsm_burst_1", "weapon"),
            Item("cs_heavy_outfit", "outfit"),
            Item("detector_elite", "device"),
            Item("secret_section", "item", name: null),
        ]));

        Assert.Contains("weapon = { \"wpn_abakan_ogsm_burst_1\" }", script, StringComparison.Ordinal);
        Assert.Contains("outfit = { \"cs_heavy_outfit\" }", script, StringComparison.Ordinal);
        Assert.Contains("item = { \"detector_elite\" }", script, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_section", script, StringComparison.Ordinal);
    }
}
