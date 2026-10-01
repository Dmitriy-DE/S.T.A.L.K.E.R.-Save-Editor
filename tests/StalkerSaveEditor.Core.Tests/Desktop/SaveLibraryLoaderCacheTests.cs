using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class SaveLibraryLoaderCacheTests
{
    [Fact]
    public void Different_bytes_with_the_same_size_and_times_are_read_again()
    {
        var directory = Directory.CreateTempSubdirectory("se-library-cache-").FullName;
        try
        {
            var path = Path.Combine(directory, "quick.scop");
            File.WriteAllBytes(path, Enumerable.Repeat((byte)1, 10_000).ToArray());
            var stamp = File.GetLastWriteTimeUtc(path);

            var first = SaveLibraryLoader.LoadLibrary([directory], new Dictionary<string, SaveLibraryLoader.CachedSave>()).Cache;
            var second = SaveLibraryLoader.LoadLibrary([directory], first).Cache;
            Assert.Same(first[path], second[path]);

            // Same length, same modification time, other content: what a sync client or a copy tool can leave.
            File.WriteAllBytes(path, Enumerable.Repeat((byte)2, 10_000).ToArray());
            File.SetLastWriteTimeUtc(path, stamp);
            var third = SaveLibraryLoader.LoadLibrary([directory], second).Cache;

            Assert.NotSame(second[path], third[path]);
            Assert.NotEqual(second[path].Probe, third[path].Probe);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_probe_covers_the_start_and_the_end_of_the_file()
    {
        var directory = Directory.CreateTempSubdirectory("se-library-probe-").FullName;
        try
        {
            var path = Path.Combine(directory, "a.sav");
            var bytes = new byte[100_000];
            File.WriteAllBytes(path, bytes);
            var original = SaveLibraryLoader.ContentProbe(path);
            Assert.NotEqual(0ul, original);

            bytes[10] = 1;
            File.WriteAllBytes(path, bytes);
            Assert.NotEqual(original, SaveLibraryLoader.ContentProbe(path));

            bytes[10] = 0;
            bytes[^10] = 1;
            File.WriteAllBytes(path, bytes);
            Assert.NotEqual(original, SaveLibraryLoader.ContentProbe(path));

            Assert.Equal(0ul, SaveLibraryLoader.ContentProbe(Path.Combine(directory, "missing.sav")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
