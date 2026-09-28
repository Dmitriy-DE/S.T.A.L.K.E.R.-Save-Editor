using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class AtomicGameFileWriterTests
{
    [Fact]
    public void Replaces_a_game_file_through_a_temporary_file_and_leaves_no_temporary_artifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-atomic-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "gamedata", "scripts", "example.script");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old");
        var fileSystem = new PhysicalGameFileSystem();

        try
        {
            AtomicGameFileWriter.Write(fileSystem, target, "new"u8.ToArray(), overwrite: true);

            Assert.Equal("new", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Does_not_overwrite_when_create_only_was_requested()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-atomic-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "owned.json");
        File.WriteAllText(target, "prior");

        try
        {
            Assert.Throws<IOException>(() => AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), target, "new"u8.ToArray(), overwrite: false));
            Assert.Equal("prior", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
