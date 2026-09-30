using System.Security.Cryptography;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

/// <summary>One file of a pack: its content hash and the original it replaces (null = the pack adds it).</summary>
public sealed record FixPackFile(string RelativePath, string ContentSha256, string? OriginalSha256, string SourcePath);

public sealed record FixPackBuild(IReadOnlyList<FixPackFile> Files, IReadOnlyList<string> Unchanged, IReadOnlyList<string> Issues)
{
    public IReadOnlyList<FileOverlayOperation> Overlays => Files
        .Select(file => new FileOverlayOperation(file.RelativePath, file.ContentSha256) { ExpectedFileSha256 = file.OriginalSha256 })
        .ToArray();
}

/// <summary>
/// Turns a fix pack's <c>gamedata</c> folder into whole-file overlay operations against a vanilla install: every
/// file is compared with the game's own copy from its archives (loose files ignored, so another installed mod never
/// becomes "the original"); identical files are dropped.
/// </summary>
public static class FixPackBuilder
{
    public static FixPackBuild Build(GameTarget game, string gameDirectory, string packGameData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packGameData);
        var root = Path.GetFullPath(packGameData);
        var relatives = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(IsGameData)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var wanted = relatives.Select(relative => relative.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var (companionGame, fsgame) = Edition(game);
        var tree = GameFileTree.Load(companionGame, gameDirectory,
            candidate => wanted.Contains(candidate.Replace('\\', '/').ToLowerInvariant()), fsgameFileNames: [fsgame], includeLooseFiles: false);
        var vanilla = tree.Files.ToDictionary(pair => pair.Key.Replace('\\', '/').ToLowerInvariant(), pair => pair.Value, StringComparer.Ordinal);

        var files = new List<FixPackFile>();
        var unchanged = new List<string>();
        foreach (var relative in relatives)
        {
            var source = Path.Combine(root, relative);
            var content = File.ReadAllBytes(source);
            var contentSha = Sha(content);
            string? originalSha = null;
            if (vanilla.TryGetValue(relative.ToLowerInvariant(), out var original))
            {
                originalSha = Sha(original.Read());
                if (originalSha == contentSha)
                {
                    unchanged.Add(relative);
                    continue;
                }
            }

            files.Add(new FixPackFile("gamedata/" + relative, contentSha, originalSha, source));
        }

        return new FixPackBuild(files, unchanged, tree.Issues);
    }

    /// <summary>Programs and the pack's own documentation/config tools are never installed into the game.</summary>
    private static bool IsGameData(string relative)
    {
        var extension = Path.GetExtension(relative).ToLowerInvariant();
        if (extension is ".exe" or ".dll" or ".bat" or ".cmd" or ".ps1") return false;
        var top = relative.Split('/')[0].ToLowerInvariant();
        return top is not ("docs" or "modcfg");
    }

    private static (CompanionGame Game, string Fsgame) Edition(GameTarget game) => game switch
    {
        GameTarget.ShadowOfChernobyl => (CompanionGame.ShadowOfChernobyl, "fsgame.ltx"),
        GameTarget.ClearSky => (CompanionGame.ClearSky, "fsgame.ltx"),
        GameTarget.CallOfPripyat => (CompanionGame.CallOfPripyat, "fsgame.ltx"),
        GameTarget.ShadowOfChernobylEnhancedEdition => (CompanionGame.ShadowOfChernobyl, "fsgame_soc.ltx"),
        GameTarget.ClearSkyEnhancedEdition => (CompanionGame.ClearSky, "fsgame_cs.ltx"),
        GameTarget.CallOfPripyatEnhancedEdition => (CompanionGame.CallOfPripyat, "fsgame_cop.ltx"),
        _ => throw new NotSupportedException($"Fix packs are not supported for {game}."),
    };

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
