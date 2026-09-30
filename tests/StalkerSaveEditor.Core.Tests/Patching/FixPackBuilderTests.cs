using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class FixPackBuilderTests
{
    [Fact]
    public void Ignores_programs_and_docs_and_never_treats_loose_mod_files_as_the_original()
    {
        var root = Directory.CreateTempSubdirectory("pack-builder-");
        try
        {
            var game = Directory.CreateDirectory(Path.Combine(root.FullName, "game"));
            File.WriteAllText(Path.Combine(game.FullName, "fsgame.ltx"),
                "$fs_root$ = false| false| $fs_root$\\\n$game_data$ = false| true| $fs_root$| gamedata\\\n");
            // A loose file from another mod: must not become "the original".
            Directory.CreateDirectory(Path.Combine(game.FullName, "gamedata", "scripts"));
            File.WriteAllText(Path.Combine(game.FullName, "gamedata", "scripts", "task.script"), "other mod\n");

            var pack = Directory.CreateDirectory(Path.Combine(root.FullName, "pack"));
            Directory.CreateDirectory(Path.Combine(pack.FullName, "scripts"));
            Directory.CreateDirectory(Path.Combine(pack.FullName, "docs"));
            File.WriteAllText(Path.Combine(pack.FullName, "scripts", "task.script"), "fixed\n");
            File.WriteAllText(Path.Combine(pack.FullName, "docs", "readme.txt"), "doc\n");
            File.WriteAllText(Path.Combine(pack.FullName, "Modifier.exe"), "MZ");

            var build = FixPackBuilder.Build(GameTarget.ClearSky, game.FullName, pack.FullName);

            var file = Assert.Single(build.Files);
            Assert.Equal("gamedata/scripts/task.script", file.RelativePath);
            Assert.Null(file.OriginalSha256); // not in the archives -> a new file, whatever is loose on disk
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
