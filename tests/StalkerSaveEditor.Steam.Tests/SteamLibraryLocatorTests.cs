using StalkerSaveEditor.Steam;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamLibraryLocatorTests
{
    [Fact]
    public void Linux_prefers_the_Steam_runtime_library()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var expected = Path.Combine(root, "steamrt64", "libsteam_api.so");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllBytes(expected, []);

            Assert.Equal(expected, SteamLibraryLocator.FindLibraryPath([root], isWindows: false));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Windows_searches_secondary_Steam_libraries_for_the_game_DLL()
    {
        var root = CreateTemporaryDirectory();
        var library = Path.Combine(root, "library");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            File.WriteAllText(
                Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                $"\"libraryfolders\"\n{{\n  \"1\" {{ \"path\" \"{library}\" }}\n}}\n");
            var expected = Path.Combine(
                library,
                "steamapps",
                "common",
                "Game",
                "Binaries",
                "Win64",
                "steam_api64.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllBytes(expected, []);

            Assert.Equal(expected, SteamLibraryLocator.FindLibraryPath([root], isWindows: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"steam-native-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
