using StalkerSaveEditor.Desktop.Services;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class PlaceNamesTests
{
    [Theory]
    [InlineData("stalker-soc", "L02_Garbage", "Свалка")]              // Shadow of Chernobyl capitalises level names
    [InlineData("stalker-cop", "jupiter_underground", "Путепровод «Припять-1»")]
    [InlineData("stalker-cs", "marsh", "Болота")]
    [InlineData("stalker-cs", "agroprom_underground", "Подземелье НИИ Агропром")] // named only by the first game
    [InlineData("stalker-cop", "l01_escape", "Кордон")]               // a mod's level, named by another game
    [InlineData("stalker-cop", "some_mod_level", "some_mod_level")]
    public void A_level_is_named_as_the_games_name_it(string release, string level, string expected) =>
        Assert.Equal(expected, PlaceNames.Level(release, level));

    [Theory]
    [InlineData("stalker-cop", "zat_a2_actor_treasure", "Личный ящик на «Скадовске»")]
    [InlineData("stalker-cop", "jup_b202_actor_treasure", "Личный ящик на «Янове»")]
    [InlineData("stalker-cop", "mod_x_actor_treasure", "Личный ящик")]
    [InlineData("stalker-soc", "level_prefix_inventory_box", "Ящик")]
    [InlineData("stalker-soc", "level_prefix_inventory_box_0000", "Ящик № 1")]
    [InlineData("stalker-soc", "bar_inventory_box_0012", "Ящик № 13")]
    [InlineData("stalker-cs", "esc_smart_terrain_5_7_box", "Ящик лагеря 5-7")]
    [InlineData("stalker-cop", "mod_treasure_weapon", "Тайник")]
    [InlineData("stalker-cop", "zat_b12_container", "Контейнер «zat_b12_container»")]
    public void A_stash_without_an_official_name_is_described_by_its_kind(string release, string box, string expected) =>
        Assert.Equal(expected, PlaceNames.Stash(release, box, 7));

    [Fact]
    public void A_clear_sky_stash_has_the_name_of_its_treasure()
    {
        var name = PlaceNames.Stash("stalker-cs", "mar_treasure_1", 7);

        Assert.DoesNotContain("mar_treasure", name, StringComparison.Ordinal);
        Assert.DoesNotContain("Тайник №", name, StringComparison.Ordinal);
        Assert.Equal(name, PlaceNames.Stash("stalker-cs-ee", "MAR_TREASURE_1", 7));
    }

    [Fact]
    public void An_unnamed_box_is_named_by_its_handle() =>
        Assert.Equal("Тайник 0x002A", PlaceNames.Stash("stalker-cop", "", 42));

    [Theory]
    [InlineData("zat_b12_container", "Затон")]
    [InlineData("pripyat_level_changer_0000", "Припять")]
    [InlineData("pas_b400_level_changer", "Путепровод «Припять-1»")]
    [InlineData("exit_to_garbage_01", null)]
    public void The_level_of_an_object_follows_from_its_prefix(string name, string? expected) =>
        Assert.Equal(expected, PlaceNames.LevelOfObject("stalker-cop", name));
}
