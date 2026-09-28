using StalkerSaveEditor.Core.Diagnostics;
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
    public void Reports_experimental_catalogue_entries_without_calling_them_safe_recommendations()
    {
        using var fixture = new GameFixture();
        fixture.CreateXRayRoot();

        var report = GameDoctor.Analyze(GameTarget.ClearSky, fixture.Root);

        var check = Assert.Single(report.Checks, candidate => candidate.Id == "game-fixes");
        Assert.Equal(GameDoctorStatus.Unknown, check.Status);
        Assert.Contains("1 fix(es) are catalogued", check.Detail, StringComparison.Ordinal);
        Assert.Contains("1 experimental fix(es)", check.Detail, StringComparison.Ordinal);
        Assert.Empty(GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended));
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
