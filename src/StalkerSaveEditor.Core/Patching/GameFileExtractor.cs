using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

/// <summary>
/// Research helper: copies the game's own files (from its archives, with loose files on top, exactly as the game
/// resolves them) into a folder, so a fix can be written and checked against the real originals. Reads only.
/// </summary>
public static class GameFileExtractor
{
    /// <param name="prefixes">Paths relative to gamedata ("scripts/", "config/misc/"); empty = everything.</param>
    /// <param name="archivesOnly">The game as shipped: loose files (mods, the companion, installed fixes) are ignored.</param>
    /// <returns>The number of files written and the problems met while reading archives.</returns>
    public static (int Files, IReadOnlyList<string> Issues) Extract(
        GameTarget target, string gameDirectory, string outputDirectory, IReadOnlyList<string> prefixes, bool archivesOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(prefixes);
        var (game, fsgame) = target switch
        {
            GameTarget.ShadowOfChernobyl => (CompanionGame.ShadowOfChernobyl, "fsgame.ltx"),
            GameTarget.ClearSky => (CompanionGame.ClearSky, "fsgame.ltx"),
            GameTarget.CallOfPripyat => (CompanionGame.CallOfPripyat, "fsgame.ltx"),
            GameTarget.ShadowOfChernobylEnhancedEdition => (CompanionGame.ShadowOfChernobyl, "fsgame_soc.ltx"),
            GameTarget.ClearSkyEnhancedEdition => (CompanionGame.ClearSky, "fsgame_cs.ltx"),
            GameTarget.CallOfPripyatEnhancedEdition => (CompanionGame.CallOfPripyat, "fsgame_cop.ltx"),
            _ => throw new NotSupportedException("This game has no X-Ray archives."),
        };
        var wanted = prefixes.Select(prefix => prefix.Replace('\\', '/').TrimStart('/')).ToArray();
        var tree = GameFileTree.Load(game, Path.GetFullPath(gameDirectory),
            path => wanted.Length == 0 || wanted.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
            fsgameFileNames: [fsgame], deferArchiveContent: true, archivesOnly: archivesOnly);
        var root = Path.GetFullPath(outputDirectory);
        var issues = new List<string>(tree.Issues);
        var written = 0;
        foreach (var (relative, file) in tree.Files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                issues.Add("skipped a path outside the output folder: " + relative);
                continue;
            }

            try
            {
                var bytes = file.Read();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
                written++;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                issues.Add(relative + ": " + exception.Message);
            }
        }

        return (written, issues);
    }
}
