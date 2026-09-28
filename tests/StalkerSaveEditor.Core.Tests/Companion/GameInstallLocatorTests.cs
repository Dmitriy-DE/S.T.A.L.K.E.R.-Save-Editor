using StalkerSaveEditor.Core.Companion;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class GameInstallLocatorTests
{
    [Theory]
    [InlineData("S.T.A.L.K.E.R.: Shadow of Chernobyl", CompanionGame.ShadowOfChernobyl, true)]
    [InlineData("STALKER Clear Sky", CompanionGame.ClearSky, true)]
    [InlineData("S.T.A.L.K.E.R. Call of Pripyat", CompanionGame.CallOfPripyat, true)]
    [InlineData("STALKER Call of Prypiat - EE", CompanionGame.CallOfPripyat, false)]
    [InlineData("S.T.A.L.K.E.R.: Shadow of Chornobyl Enhanced Edition", CompanionGame.ShadowOfChernobyl, false)]
    [InlineData("STALKER Clear Sky", CompanionGame.CallOfPripyat, false)]
    public void Matches_titles_and_leaves_enhanced_editions_out(string title, CompanionGame game, bool expected)
    {
        Assert.Equal(expected, GameInstallLocator.TitleMatches(title, game));
    }

    [Fact]
    public void Reads_heroic_gog_library_and_keeps_only_the_requested_game()
    {
        var home = Path.Combine(Path.GetTempPath(), "se-heroic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = Path.Combine(home, ".config", "heroic", "gog_store");
            Directory.CreateDirectory(config);
            var cop = Path.Combine(home, "Games", "Heroic", "STALKER Call of Pripyat");
            var cs = Path.Combine(home, "Games", "Heroic", "STALKER Clear Sky");
            File.WriteAllText(Path.Combine(config, "installed.json"),
                $$"""{"installed":[{"appName":"1","install_path":"{{cop.Replace("\\", "\\\\")}}"},{"appName":"2","install_path":"{{cs.Replace("\\", "\\\\")}}"}]}""");

            Assert.Equal([cop], GameInstallLocator.HeroicDirectories(CompanionGame.CallOfPripyat, home));
            Assert.Empty(GameInstallLocator.HeroicDirectories(CompanionGame.ShadowOfChernobyl, home));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void Broken_heroic_file_is_ignored()
    {
        var home = Path.Combine(Path.GetTempPath(), "se-heroic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = Path.Combine(home, ".config", "heroic", "gog_store");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "installed.json"), "{ not json");
            Assert.Empty(GameInstallLocator.HeroicDirectories(CompanionGame.CallOfPripyat, home));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void A_folder_without_fsgame_is_not_an_installation()
    {
        if (OperatingSystem.IsWindows()) return; // Windows discovery reads the registry.
        var home = Path.Combine(Path.GetTempPath(), "se-games-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fake = Path.Combine(home, "Games", "STALKER Call of Pripyat");
            Directory.CreateDirectory(fake);
            Assert.Empty(GameInstallLocator.FindNonSteam(CompanionGame.CallOfPripyat, home));
            File.WriteAllText(Path.Combine(fake, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
            var found = Assert.Single(GameInstallLocator.FindNonSteam(CompanionGame.CallOfPripyat, home));
            Assert.Equal(GameInstallSource.Gog, found.Source);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
