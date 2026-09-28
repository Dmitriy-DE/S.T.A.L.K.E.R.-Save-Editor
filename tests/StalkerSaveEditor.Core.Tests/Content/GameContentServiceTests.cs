using System.Buffers.Binary;
using System.Text;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Content;

public sealed class GameContentServiceTests
{
    [Fact]
    public void Parses_sections_inheritance_comments_and_bare_entries()
    {
        var sections = LtxDocument.Parse(
            "[base]\ninv_weight = 1.5 ; comment\nclass = II_ANTIR\n[medkit]:base\ninv_name = st_medkit\ninv_weight=0,5\n[list]\nfoo\nbar ; x\n",
            "configs/misc/items.ltx");

        var resolved = LtxDocument.Resolve(sections).ToDictionary(entry => entry.Section.Name, entry => entry.Values);
        Assert.Equal("0,5", resolved["medkit"]["inv_weight"]);
        Assert.Equal("II_ANTIR", resolved["medkit"]["class"]);
        Assert.Equal(["foo", "bar"], sections["list"].Entries);
    }

    [Fact]
    public void Include_graph_ignores_ltx_files_the_engine_does_not_include()
    {
        var files = Files(
            ("configs/system.ltx", "#include \"misc\\items.ltx\"\n#include \"weapons\\*.ltx\"\n"),
            ("configs/misc/items.ltx", "[medkit]\ninv_name = st_medkit\n"),
            ("configs/weapons/w_pm.ltx", "[wpn_pm]\nclass = WP_PM\ninv_name = st_pm\n"),
            ("configs/misc/notepad.ltx", "[medkit]\nnote = not an item\n"));

        var sections = LtxDocument.ParseIncludeGraph("configs/system.ltx", files);

        Assert.NotNull(sections);
        Assert.Equal("st_medkit", sections["medkit"].Values["inv_name"]);
        Assert.Contains("wpn_pm", sections.Keys);
    }

    [Fact]
    public void String_tables_use_only_the_preferred_language_folder()
    {
        var files = Files(
            ("configs/text/eng/items.xml", "<string_table><string id=\"st_medkit\"><text>Medkit</text></string></string_table>"),
            ("configs/text/rus/items.xml", "<?xml version=\"1.0\" encoding=\"windows-1251\"?><string_table><string id=\"st_medkit\"><text>Аптечка</text></string></string_table>"));

        Assert.Equal("Аптечка", XRayStringTables.Read(files.Values.ToArray(), "ru")["st_medkit"]);
        Assert.Equal("Medkit", XRayStringTables.Read(files.Values.ToArray(), "en")["st_medkit"]);
    }

    [Theory]
    [InlineData("wpn_ak74", "WP_AK74", "weapon", "weapon_wgl")]
    [InlineData("wpn_bm16", "WP_BM16", "weapon", "weapon_shotgun")]
    [InlineData("wpn_pm", "WP_PM", "weapon", "weapon_magazined")]
    [InlineData("ammo_9x18_fmj", "AMMO", "ammo", "ammo")]
    [InlineData("device_torch", "TORCH_S", "device", "torch")]
    [InlineData("stalker_outfit", "E_STLK", "item", "outfit")]
    [InlineData("medkit", "II_MEDKI", "consumable", "base")]
    public void Maps_categories_and_serializer_families_like_the_oracle(string name, string className, string category, string family)
    {
        var values = new Dictionary<string, string> { ["class"] = className, ["inv_name"] = "x" };
        Assert.Equal(category, InstalledGameCatalogBuilder.Category(name, values));
        Assert.Equal(family, InstalledGameCatalogBuilder.SerializationFamily(name, values, category));
    }

    [Fact]
    public void Builds_and_caches_a_catalog_with_items_upgrades_factions_and_icons()
    {
        using var game = new TemporaryDirectory();
        using var cache = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(game.Path, "fsgame.ltx"),
            "$game_data$ = false| true| $fs_root$| gamedata\\\n$game_config$ = true| false| $game_data$| configs\\\n" +
            "$arch_dir_resources$ = false| false| $fs_root$| resources\\\n");
        Write(game.Path, "gamedata/configs/system.ltx",
            "#include \"misc\\items.ltx\"\n#include \"creatures\\game_relations.ltx\"\n#include \"misc\\inventory_upgrades.ltx\"\n");
        Write(game.Path, "gamedata/configs/misc/items.ltx",
            "[medkit]\nclass = II_MEDKI\ninv_name = st_medkit\ninv_weight = 0.5\ninv_grid_width = 1\ninv_grid_height = 1\ninv_grid_x = 1\ninv_grid_y = 0\n" +
            "[wpn_pm]\nclass = WP_PM\ninv_name = st_pm\ninv_grid_width = 2\ninv_grid_height = 1\ninv_grid_x = 0\ninv_grid_y = 1\n" +
            "[wpn_pm_hud]\nclass = WP_PM\ninv_name = st_pm\n");
        Write(game.Path, "gamedata/configs/misc/inventory_upgrades.ltx", "#include \"..\\weapons\\upgrades\\w_pm_up.ltx\"\n[upgraded_inventory]\nwpn_pm\n");
        Write(game.Path, "gamedata/configs/weapons/upgrades/w_pm_up.ltx", "[up_a_pm]\nsection = up_sect_a_pm\nname = st_up_a_pm\n");
        Write(game.Path, "gamedata/configs/creatures/game_relations.ltx",
            "[game_relations]\ncommunities = stalker, 0, bandit, 1\n[communities_relations]\nstalker = 0, -1000\nbandit = -1000, 0\n" +
            "[action_points]\ncommunity_goodwill_limits = -5000, 5000\n");
        Write(game.Path, "gamedata/configs/text/rus/st_items.xml",
            "<string_table><string id=\"st_medkit\"><text>Аптечка</text></string><string id=\"st_pm\"><text>ПМ</text></string>" +
            "<string id=\"st_up_a_pm\"><text>Ствол</text></string><string id=\"stalker\"><text>Одиночки</text></string></string_table>");
        Directory.CreateDirectory(Path.Combine(game.Path, "gamedata/textures/ui"));
        File.WriteAllBytes(Path.Combine(game.Path, "gamedata/textures/ui/ui_icon_equipment.dds"), SolidDxt1(100, 100));

        var first = GameContentService.Load(CompanionGame.CallOfPripyat, game.Path, cache.Path);

        Assert.NotNull(first);
        Assert.False(first.Status.FromCache);
        Assert.Equal(2, first.Status.ItemCount);
        Assert.Null(first.Bundle.Items.Resolve("wpn_pm_hud"));
        Assert.Equal("Аптечка", first.Bundle.Items.Resolve("medkit")!.DisplayName);
        Assert.Equal("weapon_magazined", first.Bundle.Items.Resolve("wpn_pm")!.SerializationFamily);
        Assert.Equal("wpn_pm", first.Bundle.Upgrades!.Resolve("up_a_pm")!.ItemKey);
        Assert.Equal(-1000, first.Bundle.Factions!.DefaultRelation("stalker", "bandit"));
        Assert.Equal(5000, first.Bundle.Factions.GoodwillMax);

        var png = first.IconPng("wpn_pm");
        Assert.NotNull(png);
        Assert.Equal((byte)0x89, png[0]);
        Assert.Equal(100, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(50, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

        var second = GameContentService.Load(CompanionGame.CallOfPripyat, game.Path, cache.Path);
        Assert.NotNull(second);
        Assert.True(second.Status.FromCache);
        Assert.Equal("ПМ", second.Bundle.Items.Resolve("wpn_pm")!.DisplayName);
        Assert.Equal(2, second.Bundle.Items.Resolve("wpn_pm")!.Width);
    }

    [Fact]
    public void Changing_a_game_file_invalidates_the_cache()
    {
        using var game = new TemporaryDirectory();
        using var cache = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(game.Path, "fsgame.ltx"),
            "$game_data$ = false| true| $fs_root$| gamedata\\\n$game_config$ = true| false| $game_data$| configs\\\n$arch_dir$ = false| false| $fs_root$\n");
        Write(game.Path, "gamedata/configs/system.ltx", "[medkit]\nclass = II_MEDKI\ninv_name = a\n");
        Assert.False(GameContentService.Load(CompanionGame.CallOfPripyat, game.Path, cache.Path)!.Status.FromCache);
        Assert.True(GameContentService.Load(CompanionGame.CallOfPripyat, game.Path, cache.Path)!.Status.FromCache);

        Write(game.Path, "gamedata/configs/system.ltx", "[medkit]\nclass = II_MEDKI\ninv_name = b\n[bandage]\nclass = II_BANDG\ninv_name = c\n");
        File.SetLastWriteTimeUtc(Path.Combine(game.Path, "gamedata/configs/system.ltx"), DateTime.UtcNow.AddMinutes(1));
        var rebuilt = GameContentService.Load(CompanionGame.CallOfPripyat, game.Path, cache.Path)!;
        Assert.False(rebuilt.Status.FromCache);
        Assert.Equal(2, rebuilt.Status.ItemCount);
    }

    [Fact]
    public void Detects_a_known_community_mod_by_its_files()
    {
        using var game = new TemporaryDirectory();
        using var cache = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(game.Path, "fsgame.ltx"),
            "$game_data$ = false| true| $fs_root$| gamedata\\\n$game_config$ = true| false| $game_data$| configs\\\n$arch_dir$ = false| false| $fs_root$\n");
        Write(game.Path, "gamedata/configs/system.ltx", "[medkit]\nclass = II_MEDKI\ninv_name = a\n");
        Write(game.Path, "gamedata/OGSM_CS_info.ltx", "; mod");

        Assert.Equal("OGSM", GameContentService.Load(CompanionGame.ClearSky, game.Path, cache.Path)!.Status.ModName);
    }

    [Fact]
    public void Decodes_dxt1_including_transparency()
    {
        var image = DdsImage.Decode(SolidDxt1(4, 4, transparent: true));
        Assert.Equal(0, image.Pixels[3]);
        image = DdsImage.Decode(SolidDxt1(4, 4));
        Assert.Equal(255, image.Pixels[3]);
        Assert.Equal(255, image.Pixels[0]);
    }

    [Fact]
    public void Rejects_truncated_dds()
    {
        var data = SolidDxt1(8, 8);
        Assert.Throws<InvalidDataException>(() => DdsImage.Decode(data.AsSpan(0, data.Length - 4)));
        Assert.Throws<InvalidDataException>(() => DdsImage.Decode("NOTADDS"u8));
    }

    private static Dictionary<string, GameFile> Files(params (string Path, string Text)[] entries) =>
        entries.ToDictionary(
            entry => entry.Path,
            entry =>
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var bytes = entry.Text.Contains("windows-1251", StringComparison.Ordinal)
                    ? Encoding.GetEncoding(1251).GetBytes(entry.Text)
                    : Encoding.UTF8.GetBytes(entry.Text);
                return new GameFile(entry.Path, "test", () => bytes);
            },
            StringComparer.OrdinalIgnoreCase);

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>DXT1 image filled with red, or with the transparent colour index when requested.</summary>
    private static byte[] SolidDxt1(int width, int height, bool transparent = false)
    {
        var blocks = ((width + 3) / 4) * ((height + 3) / 4);
        var data = new byte[128 + blocks * 8];
        "DDS "u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), 0x4);
        "DXT1"u8.CopyTo(data.AsSpan(84));
        for (var block = 0; block < blocks; block++)
        {
            var offset = 128 + block * 8;
            if (transparent)
            {
                // c0 <= c1 selects the 3-colour mode; index 3 is transparent black.
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), 0x0000);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), 0xF800);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4), 0xFFFFFFFF);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), 0xF800);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), 0x0000);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4), 0x00000000);
            }
        }

        return data;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "se-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
