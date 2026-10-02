using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class GameDoctorTests
{
    [Fact]
    public void Reports_unclassified_loose_files_without_calling_them_vanilla_or_a_conflict()
    {
        using var fixture = new GameFixture();
        fixture.CreateXRayRoot();
        var loose = Path.Combine(fixture.Root, "gamedata", "scripts", "local.script");
        Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
        File.WriteAllText(loose, "function local_script() end");

        var report = GameDoctor.Analyze(GameTarget.ClearSky, fixture.Root);

        Assert.Equal(GameTarget.ClearSky, report.Target);
        Assert.Contains(report.Checks, check => check.Id == "installation" && check.Status == GameDoctorStatus.Ok);
        Assert.Contains(report.Checks, check => check.Id == "loose-files" && check.Status == GameDoctorStatus.Warning);
        Assert.Equal(["gamedata/scripts/local.script"], report.LooseFiles);
        Assert.Null(report.SteamBuildId);
    }

    [Fact]
    public void Reads_the_steam_build_id_for_the_selected_target()
    {
        using var fixture = new GameFixture();
        var common = Path.Combine(fixture.BaseRoot, "steam-library", "steamapps", "common");
        fixture.Root = Path.Combine(common, "STALKER Clear Sky");
        fixture.CreateXRayRoot();
        File.WriteAllText(Path.Combine(fixture.BaseRoot, "steam-library", "steamapps", "appmanifest_20510.acf"), """
            "AppState"
            {
                "appid" "20510"
                "buildid" "19000000"
                "installdir" "STALKER Clear Sky"
            }
            """);

        var report = GameDoctor.Analyze(GameTarget.ClearSky, fixture.Root);

        Assert.Equal("19000000", report.SteamBuildId);
    }

    [Fact]
    public void Enhanced_edition_target_is_kept_separate_from_original_target()
    {
        using var fixture = new GameFixture();
        fixture.CreateXRayRoot();

        var report = GameDoctor.Analyze(GameTarget.ShadowOfChernobylEnhancedEdition, fixture.Root);

        Assert.Equal(GameTarget.ShadowOfChernobylEnhancedEdition, report.Target);
        Assert.DoesNotContain(report.Checks, check => check.Id == "companion");
    }

    [Fact]
    public void Missing_installation_is_reported_without_scanning_or_mutation()
    {
        var missing = Path.Combine(Path.GetTempPath(), "doctor-missing-" + Guid.NewGuid().ToString("N"));

        var report = GameDoctor.Analyze(GameTarget.Stalker2, missing);

        Assert.Contains(report.Checks, check => check.Id == "installation" && check.Status == GameDoctorStatus.Error);
        Assert.Empty(report.LooseFiles);
    }

    [Fact]
    public void Reports_archive_verified_recommendations_without_claiming_they_are_installed()
    {
        using var fixture = new GameFixture();
        var common = Path.Combine(fixture.BaseRoot, "steam-library", "steamapps", "common");
        fixture.Root = Path.Combine(common, "STALKER Clear Sky");
        fixture.CreateXRayRoot();
        File.WriteAllText(Path.Combine(fixture.BaseRoot, "steam-library", "steamapps", "appmanifest_20510.acf"), """
            "AppState"
            {
                "appid" "20510"
                "buildid" "11450472"
                "installdir" "STALKER Clear Sky"
            }
            """);

        var report = GameDoctor.Analyze(GameTarget.ClearSky, fixture.Root);

        var check = Assert.Single(report.Checks, candidate => candidate.Id == "game-fixes");
        Assert.Equal(GameDoctorStatus.Warning, check.Status);
        Assert.Contains("53 of 53 safe recommendation(s) are not installed", check.Detail, StringComparison.Ordinal);
        Assert.Contains("55 fix(es) are catalogued", check.Detail, StringComparison.Ordinal);
        Assert.Equal(53, GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended).Count);
    }

    [Fact]
    public void Does_not_recommend_soc_1_0006_fixes_for_an_unsupported_build()
    {
        using var fixture = new GameFixture();
        var common = Path.Combine(fixture.BaseRoot, "steam-library", "steamapps", "common");
        fixture.Root = Path.Combine(common, "Shadow of Chernobyl");
        fixture.CreateXRayRoot();
        File.WriteAllText(Path.Combine(fixture.BaseRoot, "steam-library", "steamapps", "appmanifest_4500.acf"), """
            "AppState"
            {
                "appid" "4500"
                "buildid" "19000000"
                "installdir" "Shadow of Chernobyl"
            }
            """);

        var report = GameDoctor.Analyze(GameTarget.ShadowOfChernobyl, fixture.Root);

        var check = Assert.Single(report.Checks, candidate => candidate.Id == "game-fixes");
        Assert.Equal(GameDoctorStatus.Unknown, check.Status);
        Assert.Equal("No compatible Game Fix recommendation is available.", check.Summary);
        Assert.Contains("19000000", check.Detail, StringComparison.Ordinal);
        Assert.Contains("not supported", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Discovers_structural_steam_installs_by_app_id_and_keeps_enhanced_and_s2_targets_distinct()
    {
        using var fixture = new GameFixture();
        var steamRoot = Path.Combine(fixture.BaseRoot, "steam");
        CreateSteamInstall(steamRoot, 20_510, "Clear Sky", buildId: "11450472", xray: true);
        CreateSteamInstall(steamRoot, 2_427_410, "SoC Enhanced", buildId: "22000000", xray: true);
        CreateSteamInstall(steamRoot, 1_643_320, "S2", buildId: "33000000", xray: false);
        var unmanifested = Path.Combine(steamRoot, "steamapps", "common", "Clear Sky copy");
        Directory.CreateDirectory(unmanifested);
        File.WriteAllText(Path.Combine(unmanifested, "fsgame.ltx"), "marker");
        var maliciousSteamRoot = Path.Combine(fixture.BaseRoot, "malicious-steam");
        var outside = Path.Combine(fixture.BaseRoot, "outside-game");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "fsgame.ltx"), "marker");
        Directory.CreateDirectory(Path.Combine(maliciousSteamRoot, "steamapps"));
        File.WriteAllText(Path.Combine(maliciousSteamRoot, "steamapps", "appmanifest_20510.acf"), """
            "AppState"
            {
                "appid" "20510"
                "buildid" "99999999"
                "installdir" "../../../outside-game"
            }
            """);

        var installations = GameDoctor.DiscoverInstallations([steamRoot, maliciousSteamRoot]);

        Assert.Equal(3, installations.Count);
        Assert.Contains(installations, item => item.Target == GameTarget.ClearSky && item.BuildId == "11450472");
        Assert.Contains(installations, item => item.Target == GameTarget.ShadowOfChernobylEnhancedEdition && item.BuildId == "22000000");
        Assert.Contains(installations, item => item.Target == GameTarget.Stalker2 && item.BuildId == "33000000");
        Assert.All(installations, item => Assert.Equal(GameInstallSource.Steam, item.Source));
        Assert.DoesNotContain(installations, item => item.Directory == unmanifested);
        Assert.DoesNotContain(installations, item => item.Directory == outside);
    }

    [Fact]
    public void Discovery_deduplicates_a_steam_install_reached_through_a_symlinked_root()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new GameFixture();
        var steamRoot = Path.Combine(fixture.BaseRoot, "steam");
        CreateSteamInstall(steamRoot, 20_510, "Clear Sky", buildId: "11450472", xray: true);
        var alias = Path.Combine(fixture.BaseRoot, "steam-link");
        Directory.CreateSymbolicLink(alias, steamRoot);

        var installations = GameDoctor.DiscoverInstallations([steamRoot, alias]);

        Assert.Single(installations);
        Assert.Equal(GameTarget.ClearSky, installations[0].Target);
    }

    [Theory]
    [InlineData(GameTarget.ShadowOfChernobylEnhancedEdition, 2_427_410, "SoC EE", "fsgame_soc.ltx", "24067120")]
    [InlineData(GameTarget.ClearSkyEnhancedEdition, 2_427_420, "Clear Sky EE", "fsgame_cs.ltx", "24067129")]
    [InlineData(GameTarget.CallOfPripyatEnhancedEdition, 2_427_430, "CoP EE", "fsgame_cop.ltx", "24067133")]
    public void Discovery_accepts_target_specific_enhanced_xray_markers(
        GameTarget target,
        int appId,
        string installDirectory,
        string markerName,
        string buildId)
    {
        using var fixture = new GameFixture();
        var steamRoot = Path.Combine(fixture.BaseRoot, "steam");
        CreateSteamInstall(steamRoot, appId, installDirectory, buildId, xray: true, markerName: markerName);

        var installations = GameDoctor.DiscoverInstallations([steamRoot]);

        var installation = Assert.Single(installations);
        Assert.Equal(target, installation.Target);
        Assert.Equal(buildId, installation.BuildId);
        Assert.Equal(
            StalkerSaveEditor.Core.Storage.SaveSlotDiscovery.ResolveLinks(Path.Combine(steamRoot, "steamapps", "common", installDirectory)),
            installation.Directory);
    }

    [Theory]
    [InlineData(GameTarget.ClearSkyEnhancedEdition, 2_427_420, "Clear Sky EE", "fsgame_cop.ltx")]
    [InlineData(GameTarget.CallOfPripyatEnhancedEdition, 2_427_430, "CoP EE", "fsgame_cs.ltx")]
    public void Discovery_rejects_another_enhanced_games_xray_marker(
        GameTarget target,
        int appId,
        string installDirectory,
        string markerName)
    {
        using var fixture = new GameFixture();
        var steamRoot = Path.Combine(fixture.BaseRoot, "steam");
        CreateSteamInstall(steamRoot, appId, installDirectory, buildId: "24000000", xray: true, markerName: markerName);

        var installations = GameDoctor.DiscoverInstallations([steamRoot]);

        Assert.Empty(installations);
        var report = GameDoctor.Analyze(target, Path.Combine(steamRoot, "steamapps", "common", installDirectory));
        Assert.Contains(report.Checks, check => check.Id == "installation" && check.Status == GameDoctorStatus.Error);
    }

    [Fact]
    public void Discovery_keeps_distinct_manifest_targets_even_when_they_share_a_directory()
    {
        using var fixture = new GameFixture();
        var steamRoot = Path.Combine(fixture.BaseRoot, "steam");
        CreateSteamInstall(steamRoot, 20_510, "Shared", buildId: "11450472", xray: true);
        CreateSteamInstall(steamRoot, 2_427_410, "Shared", buildId: "22000000", xray: true);

        var installations = GameDoctor.DiscoverInstallations([steamRoot]);

        Assert.Equal(2, installations.Count);
        Assert.All(installations, item => Assert.Equal(StalkerSaveEditor.Core.Storage.SaveSlotDiscovery.ResolveLinks(Path.Combine(steamRoot, "steamapps", "common", "Shared")), item.Directory));
        Assert.Contains(installations, item => item.Target == GameTarget.ClearSky && item.BuildId == "11450472");
        Assert.Contains(installations, item => item.Target == GameTarget.ShadowOfChernobylEnhancedEdition && item.BuildId == "22000000");
    }

    [Fact]
    public void Discovery_rejects_a_manifest_install_symlink_that_escapes_the_steam_library()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new GameFixture();
        var steamRoot = Path.Combine(fixture.BaseRoot, "steam");
        var common = Path.Combine(steamRoot, "steamapps", "common");
        var outside = Path.Combine(fixture.BaseRoot, "outside-game");
        Directory.CreateDirectory(common);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "fsgame.ltx"), "marker");
        Directory.CreateSymbolicLink(Path.Combine(common, "escaped"), outside);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "appmanifest_20510.acf"), """
            "AppState"
            {
                "appid" "20510"
                "buildid" "11450472"
                "installdir" "escaped"
            }
            """);

        var installations = GameDoctor.DiscoverInstallations([steamRoot]);

        Assert.Empty(installations);
    }

    private static void CreateSteamInstall(
        string steamRoot,
        int appId,
        string installDirectory,
        string buildId,
        bool xray,
        string markerName = "fsgame.ltx")
    {
        var gameDirectory = Path.Combine(steamRoot, "steamapps", "common", installDirectory);
        if (xray)
        {
            Directory.CreateDirectory(gameDirectory);
            File.WriteAllText(Path.Combine(gameDirectory, markerName), "marker");
        }
        else
        {
            var paks = Path.Combine(gameDirectory, "Stalker2", "Content", "Paks");
            Directory.CreateDirectory(paks);
        }

        var manifestPath = Path.Combine(steamRoot, "steamapps", $"appmanifest_{appId}.acf");
        File.WriteAllText(manifestPath, $$"""
            "AppState"
            {
                "appid" "{{appId}}"
                "buildid" "{{buildId}}"
                "installdir" "{{installDirectory}}"
            }
            """);
    }

    private sealed class GameFixture : IDisposable
    {
        public GameFixture()
        {
            BaseRoot = Path.Combine(Path.GetTempPath(), "doctor-" + Guid.NewGuid().ToString("N"));
            Root = Path.Combine(BaseRoot, "game");
        }

        public string BaseRoot { get; }
        public string Root { get; set; }

        public void CreateXRayRoot()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(BaseRoot)) Directory.Delete(BaseRoot, recursive: true);
        }
    }
}
