using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Storage;

public sealed class SaveDirectoryLocatorTests
{
    [Fact]
    public void Finds_windows_game_save_paths_from_steam_manifests_and_fsgame_override()
    {
        using var directory = new TemporaryDirectory();
        var home = Directory.CreateDirectory(Path.Combine(directory.Path, "User")).FullName;
        var steamRoot = Directory.CreateDirectory(Path.Combine(directory.Path, "Steam")).FullName;
        var library = Directory.CreateDirectory(Path.Combine(directory.Path, "Library")).FullName;
        WriteLibraryFolders(steamRoot, library);
        var install = Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Custom COP Install")).FullName;
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_41700.acf"), "\"AppState\" { \"appid\" \"41700\" \"installdir\" \"Custom COP Install\" }\n");
        File.WriteAllText(Path.Combine(install, "fsgame.ltx"), "$app_data_root$ = true| false| $fs_root$| custom-user-data\\\n$game_saves$ = true| false| $app_data_root$| saves\\\n");

        var candidates = SaveDirectoryLocator.FindCandidateDirectories(new SaveDirectoryDiscoveryOptions
        {
            Platform = SaveDiscoveryPlatform.Windows,
            HomeDirectory = home,
            UserProfileDirectory = home,
            PublicDirectory = Path.Combine(directory.Path, "Public"),
            LocalAppDataDirectory = Path.Combine(home, "AppData", "Local"),
            SteamRoots = [steamRoot],
        });

        Assert.Contains(candidates, candidate =>
            candidate.ReleaseId == "stalker-cop" && candidate.DirectoryPath == Path.Combine(install, "custom-user-data", "saves"));
        Assert.Contains(candidates, candidate =>
            candidate.ReleaseId == "stalker-cop" && candidate.DirectoryPath == Path.Combine(install, "_appdata_", "savedgames"));
        Assert.Contains(candidates, candidate => candidate.DirectoryPath.Contains("Documents", StringComparison.Ordinal));
    }

    [Fact]
    public void Finds_proton_save_paths_in_a_secondary_library_even_without_a_game_manifest()
    {
        using var directory = new TemporaryDirectory();
        var home = Directory.CreateDirectory(Path.Combine(directory.Path, "home")).FullName;
        var steamRoot = Directory.CreateDirectory(Path.Combine(directory.Path, "Steam")).FullName;
        var library = Directory.CreateDirectory(Path.Combine(directory.Path, "Secondary Library")).FullName;
        WriteLibraryFolders(steamRoot, library);
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "compatdata", "41700", "pfx", "drive_c", "users", "ProtonProfile"));
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "compatdata", "1643320", "pfx", "drive_c", "users", "ProtonProfile"));

        var candidates = SaveDirectoryLocator.FindCandidateDirectories(new SaveDirectoryDiscoveryOptions
        {
            Platform = SaveDiscoveryPlatform.Linux,
            HomeDirectory = home,
            SteamRoots = [steamRoot],
        });

        Assert.Contains(candidates, candidate =>
            candidate.ReleaseId == "stalker-cop" && candidate.DirectoryPath.Contains(
                Path.Combine("compatdata", "41700", "pfx", "drive_c", "users", "ProtonProfile", "Documents", "Stalker-COP", "savedgames"),
                StringComparison.Ordinal));
        Assert.Contains(candidates, candidate =>
            candidate.ReleaseId == "stalker2" && candidate.DirectoryPath.Contains(
                Path.Combine("compatdata", "1643320", "pfx", "drive_c", "users", "ProtonProfile", "Local Settings", "Application Data", "Stalker2", "Saved", "STEAM", "SaveGames"),
                StringComparison.Ordinal));
    }

    private static void WriteLibraryFolders(string steamRoot, string library)
    {
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Escape(steamRoot)}\" }} \"1\" {{ \"path\" \"{Escape(library)}\" }} }}\n");
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"save-locations-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
