using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Storage;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class GameFixesViewModelTests
{
    [Fact]
    public void Shows_the_researched_clear_sky_fix_and_keeps_actions_disabled_without_an_installation()
    {
        var viewModel = new GameFixesViewModel();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");

        var fix = Assert.Single(viewModel.Fixes, entry => entry.Id == "cs.quest.dead-wild-napr");
        Assert.Equal("cs.quest.dead-wild-napr", fix.Id);
        Assert.Equal(GameFixMaturity.Validated, fix.Definition.Maturity);
        Assert.Equal(GameFixCategory.Essential, fix.Definition.Category);
        Assert.Equal(GameFixState.NotInstalled, fix.State);
        Assert.NotEmpty(fix.MaturityName);
        Assert.False(viewModel.InstallCommand.CanExecute(null));
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void Shows_recommended_preset_growth_and_catalogue_versions()
    {
        var viewModel = new GameFixesViewModel();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");

        Assert.Contains("23 → 47", viewModel.PresetChangeStatus, StringComparison.Ordinal);
        Assert.Contains(GameFixCatalog.PreviousDatasetVersion, viewModel.PresetChangeStatus, StringComparison.Ordinal);
        Assert.Contains(GameFixCatalog.DatasetVersion, viewModel.PresetChangeStatus, StringComparison.Ordinal);

        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cop");
        Assert.Contains("11 → 16", viewModel.PresetChangeStatus, StringComparison.Ordinal);

        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "soc");
        Assert.NotEmpty(viewModel.PresetChangeStatus);
    }

    [Fact]
    public async Task Enables_install_only_after_matching_build_was_checked()
    {
        using var install = new SteamInstallFixture("11450472");
        var viewModel = CreateClearSkyViewModel(install.GameDirectory);

        Assert.False(viewModel.InstallCommand.CanExecute(null));
        await viewModel.CheckInstallationAsync();

        Assert.Contains("11450472", viewModel.CompatibilityStatus, StringComparison.Ordinal);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Blocks_install_for_a_nonmatching_steam_build()
    {
        using var install = new SteamInstallFixture("19000000");
        var viewModel = CreateClearSkyViewModel(install.GameDirectory);

        await viewModel.CheckInstallationAsync();

        Assert.Contains("19000000", viewModel.CompatibilityStatus, StringComparison.Ordinal);
        Assert.False(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Recommended_preset_requires_a_checked_installation_and_records_a_safety_snapshot()
    {
        using var install = new SteamInstallFixture("11450472");
        var snapshotRoot = Path.Combine(Path.GetTempPath(), "sse-fix-vm-snapshots-" + Guid.NewGuid().ToString("N"));
        var configStateRoot = Path.Combine(snapshotRoot, "config");
        var snapshots = new ToolkitSnapshotService(
            snapshotRoot,
            new GameFixEngine(),
            GameFixCatalog.All.ToDictionary(fix => fix.Id, StringComparer.Ordinal),
            configStateDirectory: configStateRoot);
        var viewModel = CreateClearSkyViewModel(install.GameDirectory, snapshots);

        try
        {
            Assert.False(viewModel.ApplyRecommendedPresetCommand.CanExecute(null));
            await viewModel.CheckInstallationAsync();

            Assert.True(viewModel.ApplyRecommendedPresetCommand.CanExecute(null));
            await viewModel.ApplyRecommendedPresetAsync();

            Assert.NotEmpty(viewModel.Status);
            Assert.Contains(snapshots.List(), snapshot =>
                string.Equals(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(snapshot.GameDirectory)),
                    SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(install.GameDirectory)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.False(Directory.Exists(Path.Combine(install.GameDirectory, ".save-editor-game-fixes")));
        }
        finally
        {
            if (Directory.Exists(snapshotRoot)) Directory.Delete(snapshotRoot, recursive: true);
        }
    }

    [Fact]
    public void Marks_an_installed_fix_as_outdated_when_catalogue_version_advances()
    {
        var definition = Assert.Single(GameFixCatalog.All, candidate => candidate.Id == "cs.quest.dead-wild-napr");

        var outdated = new GameFixEntry(definition, GameFixState.Installed, "0.9.0");
        var current = new GameFixEntry(definition, GameFixState.Installed, definition.Version);

        Assert.True(outdated.UpdateAvailable);
        Assert.False(current.UpdateAvailable);
    }

    private static GameFixesViewModel CreateClearSkyViewModel(string gameDirectory, ToolkitSnapshotService? snapshots = null)
    {
        var viewModel = new GameFixesViewModel(snapshots);
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets, target => target.Id == "cs");
        viewModel.GameDirectory = gameDirectory;
        return viewModel;
    }

    private sealed class SteamInstallFixture : IDisposable
    {
        private readonly string _root;

        public SteamInstallFixture(string buildId)
        {
            _root = Path.Combine(Path.GetTempPath(), "sse-fix-vm-" + Guid.NewGuid().ToString("N"));
            Library = Path.Combine(_root, "steam-library");
            GameDirectory = Path.Combine(Library, "steamapps", "common", "STALKER Clear Sky");
            Directory.CreateDirectory(GameDirectory);
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
            File.WriteAllText(Path.Combine(Library, "steamapps", "appmanifest_20510.acf"), $$"""
                "AppState"
                {
                    "appid" "20510"
                    "buildid" "{{buildId}}"
                    "installdir" "STALKER Clear Sky"
                }
                """);
        }

        public string Library { get; }
        public string GameDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
