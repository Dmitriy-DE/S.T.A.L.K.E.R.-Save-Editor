using System.Text;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

public sealed partial class GameFixEngine
{
    private List<PreparedFileChange> PrepareChanges(
        GameTarget game,
        string root,
        IReadOnlyList<(TextPatchOperation Operation, string RelativePath)> operations)
    {
        var comparer = PathComparer;
        var grouped = new Dictionary<string, (string Path, byte[] Before, string Text, Encoding Encoding, bool TargetExistedBefore, string? SourceFingerprint)>(comparer);
        foreach (var (operation, rawRelativePath) in operations)
        {
            // One key per file: "a\\b" and "a/b" must share the cumulative text, not overwrite each other.
            var relativePath = NormalizeRelativePath(rawRelativePath);
            var path = ResolveGamePath(root, relativePath);
            var encoding = GetTextEncoding(operation.CodePage);
            if (!grouped.TryGetValue(relativePath, out var entry))
            {
                var source = ReadTargetSource(game, root, relativePath, path);
                entry = (path, source.Bytes, encoding.GetString(source.Bytes), encoding,
                    source.TargetExistedBefore, source.SourceFingerprint);
            }
            else if (entry.Encoding.CodePage != encoding.CodePage)
            {
                throw new ArgumentException("All text patches for one file must use the same code page.", nameof(operations));
            }

            if (entry.TargetExistedBefore && !_allowSyntheticDefinitions && operation.ExpectedFileSha256 is null)
                throw new InvalidDataException($"An existing loose game-data file has no verified source hash; refusing to patch a user override: {relativePath}.");

            if (operation.ExpectedFileSha256 is { } expectedFileSha256 &&
                !string.Equals(Hash(entry.Before), expectedFileSha256, StringComparison.Ordinal))
                throw new InvalidDataException($"The source file hash does not match the verified build for {relativePath}.");

            var first = entry.Text.IndexOf(operation.ExpectedText, StringComparison.Ordinal);
            if (first < 0 || entry.Text.IndexOf(operation.ExpectedText, first + operation.ExpectedText.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException($"Expected exactly one text anchor in {relativePath}; found zero or multiple matches.");
            var patched = entry.Text[..first] + operation.ReplacementText + entry.Text[(first + operation.ExpectedText.Length)..];
            grouped[relativePath] = (entry.Path, entry.Before, patched, entry.Encoding, entry.TargetExistedBefore, entry.SourceFingerprint);
        }

        return grouped.Select(pair =>
        {
            var afterBytes = pair.Value.Encoding.GetBytes(pair.Value.Text);
            if (afterBytes.AsSpan().SequenceEqual(pair.Value.Before))
                throw new InvalidDataException("Text patch produced no change: " + pair.Key);
            return new PreparedFileChange(
                pair.Key,
                pair.Value.Path,
                pair.Value.Before,
                afterBytes,
                Hash(pair.Value.Before),
                Hash(afterBytes),
                pair.Value.TargetExistedBefore,
                pair.Value.SourceFingerprint);
        }).ToList();
    }

    private PreparedFileChange PrepareSpawnEdits(GameTarget game, string root, string relativePath, IReadOnlyList<SpawnEditOperation> edits)
    {
        var path = ResolveGamePath(root, relativePath);
        var (before, existed, fingerprint) = ReadTargetSource(game, root, relativePath, path);
        foreach (var edit in edits)
        {
            if (edit.ExpectedFileSha256 is { } expected && !string.Equals(Hash(before), expected, StringComparison.Ordinal))
                throw new InvalidDataException($"The source file hash does not match the verified build for {relativePath}.");
            if (edit.ExpectedFileSha256 is null && !_allowSyntheticDefinitions)
                throw new InvalidDataException($"An all.spawn edit has no verified source hash: {relativePath}.");
        }
        var after = AllSpawnEditor.Apply(before, edits);
        return new PreparedFileChange(relativePath, path, before, after, Hash(before), Hash(after), existed, fingerprint);
    }

    private PreparedFileChange PrepareOverlay(GameTarget game, string root, FileOverlayOperation overlay, string relativePath)
    {
        var path = ResolveGamePath(root, relativePath);
        var content = _overlayContent(overlay.ContentSha256)
            ?? throw new FileNotFoundException("The fix pack file is not in the local content store; download the pack first.", relativePath);
        if (!string.Equals(Hash(content), overlay.ContentSha256, StringComparison.Ordinal))
            throw new InvalidDataException("A fix pack file does not match its recorded SHA-256: " + relativePath);

        byte[] before;
        bool existed;
        string? fingerprint;
        if (overlay.ExpectedFileSha256 is { } expected)
        {
            (before, existed, fingerprint) = ReadTargetSource(game, root, relativePath, path);
            if (!string.Equals(Hash(before), expected, StringComparison.Ordinal))
                throw new InvalidDataException($"The source file hash does not match the file this pack was made for: {relativePath}.");
        }
        else
        {
            // A new file: it must not exist loose or in the archives, so removing it later restores the game exactly.
            if (_fileSystem.FileExists(path) || SourceExistsInArchives(game, root, relativePath, path))
                throw new InvalidDataException("The pack adds a file that already exists in the game: " + relativePath);
            (before, existed, fingerprint) = ([], false, AbsentSourceFingerprint);
        }

        if (content.AsSpan().SequenceEqual(before))
            throw new InvalidDataException("A fix pack file is identical to the game's file: " + relativePath);
        return new PreparedFileChange(relativePath, path, before, content, Hash(before), Hash(content), existed, fingerprint);
    }

    private bool SourceExistsInArchives(GameTarget game, string root, string relativePath, string path)
    {
        try
        {
            _ = ReadTargetSource(game, root, relativePath, path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private (byte[] Bytes, bool TargetExistedBefore, string? SourceFingerprint) ReadTargetSource(
        GameTarget game,
        string root,
        string relativePath,
        string absolutePath)
    {
        if (_fileSystem.FileExists(absolutePath))
            return (_fileSystem.ReadAllBytes(absolutePath), TargetExistedBefore: true, SourceFingerprint: null);

        var contentRelativePath = relativePath.StartsWith("gamedata/", StringComparison.OrdinalIgnoreCase)
            ? relativePath["gamedata/".Length..]
            : relativePath;
        var (companionGame, fsgame) = game switch
        {
            GameTarget.ShadowOfChernobyl => (CompanionGame.ShadowOfChernobyl, "fsgame.ltx"),
            GameTarget.ClearSky => (CompanionGame.ClearSky, "fsgame.ltx"),
            GameTarget.CallOfPripyat => (CompanionGame.CallOfPripyat, "fsgame.ltx"),
            GameTarget.ShadowOfChernobylEnhancedEdition => (CompanionGame.ShadowOfChernobyl, "fsgame_soc.ltx"),
            GameTarget.ClearSkyEnhancedEdition => (CompanionGame.ClearSky, "fsgame_cs.ltx"),
            GameTarget.CallOfPripyatEnhancedEdition => (CompanionGame.CallOfPripyat, "fsgame_cop.ltx"),
            _ => throw new FileNotFoundException("The target is not a loose file and this game edition has no verified archive reader.", absolutePath),
        };
        var tree = GameFileTree.Load(companionGame, root,
            candidate => string.Equals(candidate, contentRelativePath, StringComparison.OrdinalIgnoreCase), _fileSystem, [fsgame]);
        if (!tree.Files.TryGetValue(contentRelativePath, out var gameFile))
            throw new FileNotFoundException("The target file is absent from the game archives and the loose game-data directory.", absolutePath);
        return (gameFile.Read(), TargetExistedBefore: false, tree.Fingerprint);
    }

    private bool MatchesPreflightSource(string root, GameTarget game, PreparedFileChange change)
    {
        CheckExistingPathForLinks(root, change.AbsolutePath);
        if (change.TargetExistedBefore)
            return MatchesHash(change.AbsolutePath, change.BeforeSha256);

        if (_fileSystem.FileExists(change.AbsolutePath)) return false;
        if (string.Equals(change.SourceFingerprint, AbsentSourceFingerprint, StringComparison.Ordinal))
            return !SourceExistsInArchives(game, root, change.RelativePath, change.AbsolutePath);
        try
        {
            var current = ReadTargetSource(game, root, change.RelativePath, change.AbsolutePath);
            return !current.TargetExistedBefore &&
                string.Equals(current.SourceFingerprint, change.SourceFingerprint, StringComparison.Ordinal) &&
                string.Equals(Hash(current.Bytes), change.BeforeSha256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }
}
