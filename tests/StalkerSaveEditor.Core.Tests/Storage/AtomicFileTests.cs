using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Storage;

public sealed class AtomicFileTests
{
    [Fact]
    public void Concurrent_writers_never_collide_and_leave_no_temp_files()
    {
        var directory = Directory.CreateTempSubdirectory("atomic-");
        try
        {
            var path = Path.Combine(directory.FullName, "sub", "cache.json");
            Parallel.For(0, 32, index =>
            {
                try
                {
                    AtomicFile.WriteAllText(path, new string((char)('a' + index % 26), 4096));
                }
                catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
                {
                    // Windows may refuse a replace while another one is in flight; the file must still be whole.
                }
            });

            var text = File.ReadAllText(path);
            Assert.Equal(4096, text.Length);
            Assert.All(text, character => Assert.Equal(text[0], character));
            Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
