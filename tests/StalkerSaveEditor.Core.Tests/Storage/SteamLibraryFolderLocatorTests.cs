using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Storage;

public sealed class SteamLibraryFolderLocatorTests
{
    [Fact]
    public void Reads_root_legacy_and_modern_libraryfolders_entries_and_deduplicates_paths()
    {
        using var directory = new TemporaryDirectory();
        var steamRoot = Directory.CreateDirectory(Path.Combine(directory.Path, "Steam")).FullName;
        var secondary = Directory.CreateDirectory(Path.Combine(directory.Path, "Games Library")).FullName;
        var legacy = Directory.CreateDirectory(Path.Combine(directory.Path, "Legacy Library")).FullName;
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
              "0" { "path" "{{steamRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}}" }
              "1" { "path" "{{secondary.Replace("\\", "\\\\", StringComparison.Ordinal)}}" "apps" { "41700" "1" } }
              "2" "{{legacy.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
            }
            """);

        var result = SteamLibraryFolderLocator.GetLibraries([steamRoot]);

        Assert.Equal([steamRoot, secondary, legacy], result);
    }

    [Fact]
    public void Skips_malformed_libraryfolders_without_aborting_other_steam_roots()
    {
        using var directory = new TemporaryDirectory();
        var malformed = Directory.CreateDirectory(Path.Combine(directory.Path, "bad-steam")).FullName;
        var valid = Directory.CreateDirectory(Path.Combine(directory.Path, "other-steam")).FullName;
        Directory.CreateDirectory(Path.Combine(malformed, "steamapps"));
        Directory.CreateDirectory(Path.Combine(valid, "steamapps"));
        File.WriteAllText(Path.Combine(malformed, "steamapps", "libraryfolders.vdf"), "{ broken");
        File.WriteAllText(Path.Combine(valid, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { }\n");

        var result = SteamLibraryFolderLocator.GetLibraries([malformed, valid]);

        Assert.Equal([malformed, valid], result);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"libraries-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
