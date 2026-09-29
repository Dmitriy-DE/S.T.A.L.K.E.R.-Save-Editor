using System.Text;
using StalkerSaveEditor.Core.Companion;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class CompanionEnhancedEditionTests
{
    [Fact]
    public void Enhanced_edition_data_root_is_the_proton_saved_games_folder()
    {
        var library = Directory.CreateTempSubdirectory("ee-lib-");
        try
        {
            var game = Directory.CreateDirectory(Path.Combine(library.FullName, "steamapps", "common", "STALKER Call of Prypiat - EE"));
            File.WriteAllText(Path.Combine(game.FullName, "fsgame_cop.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");

            var root = CompanionAppDataRootResolver.EnhancedEditionRoot(game.FullName);

            Assert.Equal(Path.Combine(library.FullName, "steamapps", "compatdata", "2427430", "pfx", "drive_c", "users", "steamuser",
                "Saved Games", "STALKER Call of Prypiat - EE", "STEAM"), root);
            Assert.Equal(Path.Combine("C:\\Users\\me\\Saved Games", "STALKER Call of Prypiat - EE", "STEAM"),
                CompanionAppDataRootResolver.EnhancedEditionRoot(game.FullName, "C:\\Users\\me\\Saved Games"));
        }
        finally
        {
            library.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_retail_install_is_not_treated_as_enhanced_edition()
    {
        var game = Directory.CreateTempSubdirectory("retail-");
        try
        {
            File.WriteAllText(Path.Combine(game.FullName, "fsgame.ltx"), "$app_data_root$ = false| false| $fs_root$| _appdata_\\\n");
            Assert.Null(CompanionAppDataRootResolver.EnhancedEditionRoot(game.FullName));
        }
        finally
        {
            game.Delete(recursive: true);
        }
    }

    [Fact]
    public void Shadow_of_chornobyl_ee_menu_hook_goes_after_the_key_press_line()
    {
        const string menu = "function main_menu:OnKeyboard(dik, keyboard_action)  --virtual function\r\n\r\n\tif keyboard_action == ui_events.WINDOW_KEY_PRESSED then\r\n\r\n\t\tif not self.mm_is_controller then\r\n\t\t\tif dik == DIK_keys.DIK_ESCAPE then\r\n\t\t\t\treturn true\r\n\t\t\telseif dik == DIK_keys.DIK_Q then\r\n\t\t\t\tself:OnMessageQuitWin()\r\n\t\t\t\treturn true\r\n\t\t\tend\r\n\t\tend\r\n\tend\r\n\treturn CUIScriptWnd.OnKeyboard(self, dik, keyboard_action)\r\nend\r\n";

        var patched = Encoding.Latin1.GetString(CompanionHookPatcher.PatchMainMenu(Encoding.Latin1.GetBytes(menu)));

        Assert.Contains("WINDOW_KEY_PRESSED then\r\n", patched, StringComparison.Ordinal);
        Assert.Contains("save_editor_companion_ui.on_menu_key(dik, self)", patched, StringComparison.Ordinal);
        Assert.Equal(menu, Encoding.Latin1.GetString(CompanionHookPatcher.RemoveMainMenuHook(Encoding.Latin1.GetBytes(patched))));
    }
}
