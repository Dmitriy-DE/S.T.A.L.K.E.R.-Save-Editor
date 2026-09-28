using StalkerSaveEditor.Core.Companion;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class Stalker2CompanionInstallerTests : IDisposable
{
    private readonly string _game = Directory.CreateTempSubdirectory("s2-game-").FullName;

    private static string ModsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "mods", "companion"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "mods", "companion");
    }

    public void Dispose() => Directory.Delete(_game, recursive: true);

    private string AddUe4ss()
    {
        var ue4ss = Directory.CreateDirectory(Path.Combine(_game, "Stalker2", "Binaries", "Win64", "ue4ss")).FullName;
        File.WriteAllBytes(Path.Combine(ue4ss, "UE4SS.dll"), [0]);
        return Path.Combine(ue4ss, "Mods");
    }

    [Fact]
    public void Needs_ue4ss_and_does_not_write_without_it()
    {
        var installer = new Stalker2CompanionInstaller(ModsRoot());

        var status = installer.Install(_game);

        Assert.True(status.GameFound);
        Assert.False(status.LoaderFound);
        Assert.False(Directory.Exists(Path.Combine(_game, "Stalker2", "Binaries", "Win64", "Mods")));
    }

    [Fact]
    public void Installs_reports_the_build_and_uninstalls_its_own_folder()
    {
        var mods = AddUe4ss();
        var installer = new Stalker2CompanionInstaller(ModsRoot());

        var installed = installer.Install(_game);

        Assert.True(installed.ModInstalled);
        Assert.Equal(installer.BundledBuild(), installed.ModBuild);
        Assert.Contains("experimental", installed.ModBuild, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(mods, "SaveEditorCompanion", "Scripts", "main.lua")));
        Assert.True(File.Exists(Path.Combine(mods, "SaveEditorCompanion", "enabled.txt")));

        Assert.False(Stalker2CompanionInstaller.Uninstall(_game).ModInstalled);
        Assert.False(Directory.Exists(Path.Combine(mods, "SaveEditorCompanion")));
    }

    [Fact]
    public void Refuses_to_replace_a_folder_it_did_not_create()
    {
        var mods = AddUe4ss();
        Directory.CreateDirectory(Path.Combine(mods, "SaveEditorCompanion"));
        File.WriteAllText(Path.Combine(mods, "SaveEditorCompanion", "keep.txt"), "user file");

        Assert.Throws<CompanionInstallerException>(() => new Stalker2CompanionInstaller(ModsRoot()).Install(_game));
        Assert.True(File.Exists(Path.Combine(mods, "SaveEditorCompanion", "keep.txt")));
    }
}
