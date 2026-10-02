using System.Collections.ObjectModel;
using System.Text;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Core.Companion;

public sealed partial class CompanionInstaller
{
    private Dictionary<string, byte[]> ReadModPayloads(CompanionGameDefinition definition, string gameDirectory)
    {
        var payloads = ReadShippedPayloads(definition);
        if (GameCatalogScript(definition.Game, gameDirectory) is { } catalog)
        {
            payloads[CompanionCatalogScript.RelativePath] = catalog;
        }

        return payloads;
    }

    /// <summary>The spawn list from the game's own configs (cp1251), or null to keep the shipped one.</summary>
    private static byte[]? GameCatalogScript(CompanionGame game, string gameDirectory)
    {
        try
        {
            var content = Content.GameContentService.Load(game, gameDirectory, Diagnostics.AppPaths.ContentCache);
            if (content is null || content.Bundle.Items.Items.Count(CompanionCatalogScript.IsSpawnable) < 20) return null;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetBytes(CompanionCatalogScript.Render(content.Bundle.Items));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private Dictionary<string, byte[]> ReadShippedPayloads(CompanionGameDefinition definition)
    {
        if (!_fileSystem.DirectoryExists(_modSourceRoot))
        {
            throw new CompanionInstallerException($"Companion source directory does not exist: {_modSourceRoot}", _modSourceRoot);
        }

        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var sourceRoot in new[]
        {
            Path.Combine(_modSourceRoot, "gamedata"),
            Path.Combine(_modSourceRoot, definition.ModDirectory, "gamedata"),
        })
        {
            if (!_fileSystem.DirectoryExists(sourceRoot))
            {
                continue;
            }

            foreach (var source in _fileSystem.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(sourceRoot, source));
                var extension = Path.GetExtension(source).ToLowerInvariant();
                var bytes = _fileSystem.ReadAllBytes(source);
                payloads[relative] = extension switch
                {
                    ".script" or ".xml" or ".ltx" => ConvertUtf8ToWindows1251(bytes, source),
                    ".dds" => bytes,
                    _ => throw new CompanionInstallerException($"Unsupported companion asset type: {relative}", source),
                };
            }
        }

        if (payloads.Count == 0)
        {
            throw new CompanionInstallerException($"No companion assets found for {definition.Id}.", _modSourceRoot);
        }

        return payloads;
    }

    private IReadOnlyList<string> GetArchiveStatusIssues(
        string gameDirectory,
        CompanionGameDefinition definition)
    {
        var search = DiscoverArchives(gameDirectory, definition);
        var issues = new List<string>(search.Issues);
        if (search.Issues.Count > 0 || search.ArchivePaths.Count == 0)
        {
            return issues;
        }

        IReadOnlyList<HookFileTarget> hookTargets;
        try
        {
            hookTargets = GetHookTargets(gameDirectory, search);
        }
        catch (CompanionInstallerException exception)
        {
            issues.Add(exception.Message);
            return issues.AsReadOnly();
        }

        foreach (var hook in hookTargets)
        {
            if (_fileSystem.FileExists(ResolveGamePath(gameDirectory, hook.GameRelativePath)))
            {
                continue;
            }

            try
            {
                _ = ReadFromArchives(gameDirectory, hook.ArchiveRelativePath, definition, search);
            }
            catch (CompanionInstallerException exception)
            {
                issues.Add(exception.Message);
            }
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private byte[] ReadFromArchives(
        string gameDirectory,
        string relativePath,
        CompanionGameDefinition definition,
        CompanionArchiveSearchResult? archiveSearch = null)
    {
        var search = archiveSearch ?? DiscoverArchives(gameDirectory, definition);
        if (search.Issues.Count > 0)
        {
            throw new CompanionInstallerException(
                $"Could not resolve X-Ray archive paths from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}: {string.Join(" ", search.Issues)}",
                search.FsgamePath);
        }

        foreach (var archivePath in search.ArchivePaths.Reverse())
        {
            try
            {
                using var stream = _fileSystem.OpenRead(archivePath);
                using var archive = XRayArchiveReader.Open(stream);
                var entries = archive.Entries
                    .Where(entry => EntryMatches(entry.Name, relativePath))
                    .ToArray();
                if (entries.Length > 1)
                {
                    throw new CompanionInstallerException(
                        $"Archive contains multiple entries for {relativePath}: {archivePath}",
                        archivePath);
                }

                if (entries.Length == 1)
                {
                    return archive.ReadFile(entries[0].Name);
                }
            }
            catch (CompanionInstallerException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XRayFormatException)
            {
                throw new CompanionInstallerException(
                    $"Could not read configured X-Ray archive {archivePath}: {exception.Message}",
                    archivePath,
                    exception);
            }
        }

        var details = search.ArchivePaths.Count == 0
            ? "No archives were found in the configured fsgame paths."
            : "No configured archive contains the requested file.";
        throw new CompanionInstallerException(
            $"Could not find vanilla {relativePath}: {details} Check {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            relativePath);
    }

    private CompanionArchiveSearchResult DiscoverArchives(
        string gameDirectory,
        CompanionGameDefinition definition) =>
        CompanionArchiveLocator.Discover(
            _fileSystem,
            gameDirectory,
            definition.FsgameFileNames,
            definition.Game);

    private static ReadOnlyCollection<HookFileTarget> GetHookTargets(
        string gameDirectory,
        CompanionArchiveSearchResult search)
    {
        var resolvedGameDataDirectory = search.GameDataDirectory ?? throw new CompanionInstallerException(
            $"Could not resolve fsgame alias $game_data$ from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            search.FsgamePath);
        var gameDataDirectory = Path.TrimEndingDirectorySeparator(resolvedGameDataDirectory);
        var expectedGameDataDirectory = Path.GetFullPath(Path.Combine(gameDirectory, "gamedata"));
        if (!string.Equals(gameDataDirectory, expectedGameDataDirectory, PathComparison))
        {
            throw new CompanionInstallerException(
                $"The fsgame $game_data$ alias resolves outside the supported gamedata directory: {gameDataDirectory}",
                search.FsgamePath);
        }

        var configRelativePath = GetConfigRelativePath(search);
        var questItemsPath = JoinRelative(configRelativePath, "misc/quest_items.ltx");
        return Array.AsReadOnly<HookFileTarget>(
        [
            new HookFileTarget(HookFileKind.BindStalker, "gamedata/scripts/bind_stalker.script", "scripts/bind_stalker.script"),
            new HookFileTarget(HookFileKind.MainMenu, "gamedata/scripts/ui_main_menu.script", "scripts/ui_main_menu.script"),
            new HookFileTarget(HookFileKind.QuestItems, JoinRelative("gamedata", questItemsPath), questItemsPath),
        ]);
    }

    private static string GetConfigRelativePath(CompanionArchiveSearchResult search)
    {
        var gameDataDirectory = search.GameDataDirectory ?? throw new CompanionInstallerException(
            $"Could not resolve fsgame alias $game_data$ from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            search.FsgamePath);
        var gameConfigDirectory = search.GameConfigDirectory ?? throw new CompanionInstallerException(
            $"Could not resolve fsgame alias $game_config$ from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            search.FsgamePath);
        var relative = Path.GetRelativePath(gameDataDirectory, gameConfigDirectory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison))
        {
            throw new CompanionInstallerException(
                "The fsgame $game_config$ alias must stay under $game_data$.",
                search.FsgamePath);
        }

        return relative == "." ? string.Empty : NormalizeRelative(relative).TrimEnd('/');
    }

    private static string MapPayloadRelativePath(string relativePath, string configRelativePath)
    {
        const string sourceConfigPrefix = "configs/";
        var normalized = NormalizeRelative(relativePath);
        return normalized.StartsWith(sourceConfigPrefix, StringComparison.Ordinal)
            ? JoinRelative(configRelativePath, normalized[sourceConfigPrefix.Length..])
            : normalized;
    }

    private static string JoinRelative(params string[] paths) =>
        string.Join('/', paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeRelative(path).Trim('/')));

    private static bool EntryMatches(string entryName, string relativePath)
    {
        var name = entryName.Replace('\\', '/').TrimStart('/');
        var suffix = relativePath.Replace('\\', '/').TrimStart('/');
        return name.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ConvertUtf8ToWindows1251(byte[] bytes, string source)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text[1..];
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(text);
        }
        catch (Exception exception) when (exception is DecoderFallbackException or EncoderFallbackException)
        {
            throw new CompanionInstallerException(
                $"Companion text asset is not valid UTF-8 or cannot be represented as Windows-1251: {source}",
                source,
                exception);
        }
    }
}
