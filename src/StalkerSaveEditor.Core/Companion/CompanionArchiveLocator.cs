using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Patching;

namespace StalkerSaveEditor.Core.Companion;

internal sealed record CompanionArchiveSearchResult(
    IReadOnlyList<string> ArchivePaths,
    IReadOnlyList<string> Issues,
    string? FsgamePath,
    string? GameDataDirectory = null,
    string? GameConfigDirectory = null);

/// <summary>
/// Resolves archive roots from X-Ray's fsgame aliases. The root list is kept in
/// declaration order because LocatorAPI processes fsgame entries in that order.
/// </summary>
internal static class CompanionArchiveLocator
{
    private sealed record AliasDefinition(
        string Name,
        string Parent,
        string RelativePath,
        bool Recursive,
        int Order);

    public static CompanionArchiveSearchResult Discover(
        IGameFileSystem fileSystem,
        string gameDirectory,
        IReadOnlyList<string> fsgameFileNames,
        CompanionGame game)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(fsgameFileNames);

        var fsgamePath = fsgameFileNames
            .Select(fileName => Path.Combine(gameDirectory, fileName))
            .FirstOrDefault(fileSystem.FileExists);
        if (fsgamePath is null)
        {
            return new CompanionArchiveSearchResult(
                Array.Empty<string>(),
                [$"Could not find fsgame.ltx for archive discovery in {gameDirectory}."],
                null);
        }

        string contents;
        try
        {
            contents = fileSystem.ReadAllText(fsgamePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CompanionArchiveSearchResult(
                Array.Empty<string>(),
                [$"Could not read {Path.GetFileName(fsgamePath)}: {exception.Message}"],
                fsgamePath);
        }

        var definitions = ParseAliases(contents, fsgamePath, out var parseIssues);
        var gameDataDirectory = ResolveAlias("$game_data$", definitions, gameDirectory, []);
        var gameConfigDirectory = ResolveAlias("$game_config$", definitions, gameDirectory, []);
        var archiveAliases = definitions
            .Where(alias => alias.Name.Contains("arch", StringComparison.OrdinalIgnoreCase))
            .OrderBy(alias => alias.Order)
            .ToArray();
        var issues = new List<string>(parseIssues);
        if (gameDataDirectory is null)
        {
            issues.Add($"Could not resolve fsgame alias $game_data$ from {Path.GetFileName(fsgamePath)}.");
        }

        if (gameConfigDirectory is null)
        {
            issues.Add($"Could not resolve fsgame alias $game_config$ from {Path.GetFileName(fsgamePath)}.");
        }

        var archivePaths = new List<string>();
        if (archiveAliases.Length == 0 && game != CompanionGame.ShadowOfChernobyl)
        {
            issues.Add($"No X-Ray archive aliases were found in {Path.GetFileName(fsgamePath)}.");
        }

        foreach (var alias in archiveAliases)
        {
            var directory = ResolveAlias(alias.Name, definitions, gameDirectory, []);
            if (directory is null)
            {
                issues.Add($"Could not resolve archive alias {alias.Name} from {Path.GetFileName(fsgamePath)}.");
                continue;
            }

            if (!fileSystem.DirectoryExists(directory))
            {
                continue;
            }

            try
            {
                var searchOption = alias.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = fileSystem.EnumerateFiles(directory, "*", searchOption)
                    .Where(IsXRayArchive)
                    .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal)
                    .ToArray();
                archivePaths.AddRange(files);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Could not enumerate archive alias {alias.Name}: {exception.Message}");
            }
        }

        if (game == CompanionGame.ShadowOfChernobyl)
        {
            try
            {
                // SoC fsgame relies on LocatorAPI scanning the $fs_root$ entry;
                // it does not declare a separate $arch_dir$ alias. LocatorAPI's
                // _initialize/Recurse/ProcessOne path discovers these root DBs.
                var rootArchives = fileSystem.EnumerateFiles(gameDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsShadowOfChernobylRootArchive)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToArray();
                archivePaths.AddRange(rootArchives);
                if (archiveAliases.Length == 0 && rootArchives.Length == 0)
                {
                    issues.Add($"No X-Ray archive aliases or gamedata.db0–gamedata.dbd root archives were found in {Path.GetFileName(fsgamePath)}.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Could not enumerate Shadow of Chernobyl root archives: {exception.Message}");
            }
        }

        var orderedArchives = archivePaths
            .Distinct(StringComparerForPaths())
            .ToArray();
        if (orderedArchives.Length == 0 && archiveAliases.Length > 0 && issues.Count == parseIssues.Count)
        {
            issues.Add($"No X-Ray archives were found in the paths configured by {Path.GetFileName(fsgamePath)}.");
        }

        return new CompanionArchiveSearchResult(
            orderedArchives,
            issues.AsReadOnly(),
            fsgamePath,
            gameDataDirectory,
            gameConfigDirectory);
    }

    private static ReadOnlyCollection<AliasDefinition> ParseAliases(
        string contents,
        string fsgamePath,
        out IReadOnlyList<string> issues)
    {
        var definitions = new List<AliasDefinition>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var lineNumber = 0;
        foreach (var rawLine in contents.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = rawLine.Split(';', 2)[0].Trim();
            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var name = line[..equals].Trim();
            if (!name.StartsWith('$') || !name.EndsWith('$'))
            {
                continue;
            }

            var fields = line[(equals + 1)..]
                .Split('|')
                .Select(field => field.Trim().Trim('"'))
                .ToArray();
            if (fields.Length < 3 ||
                !bool.TryParse(fields[0], out var recursive) ||
                !bool.TryParse(fields[1], out _))
            {
                if (name.Contains("arch", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Invalid archive alias {name} on line {lineNumber} of {Path.GetFileName(fsgamePath)}.");
                }

                continue;
            }

            if (!known.Add(name))
            {
                if (name.Contains("arch", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Duplicate archive alias {name} in {Path.GetFileName(fsgamePath)}.");
                }

                continue;
            }

            definitions.Add(new AliasDefinition(
                name,
                fields[2],
                fields.Length > 3 ? fields[3] : string.Empty,
                recursive,
                lineNumber));
        }

        issues = errors.AsReadOnly();
        return definitions.AsReadOnly();
    }

    private static string? ResolveAlias(
        string aliasName,
        IReadOnlyList<AliasDefinition> definitions,
        string gameDirectory,
        HashSet<string> resolving)
    {
        if (aliasName.Equals("$fs_root$", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(gameDirectory);
        }

        var alias = definitions.FirstOrDefault(
            definition => definition.Name.Equals(aliasName, StringComparison.OrdinalIgnoreCase));
        if (alias is null || !resolving.Add(alias.Name))
        {
            return null;
        }

        try
        {
            string? parent;
            if (alias.Parent.StartsWith('$') && alias.Parent.EndsWith('$'))
            {
                parent = ResolveAlias(alias.Parent, definitions, gameDirectory, resolving);
            }
            else if (Path.IsPathRooted(NormalizePathSeparators(alias.Parent)))
            {
                parent = Path.GetFullPath(NormalizePathSeparators(alias.Parent));
            }
            else
            {
                parent = Path.GetFullPath(Path.Combine(gameDirectory, NormalizePathSeparators(alias.Parent)));
            }

            if (parent is null)
            {
                return null;
            }

            var relativePath = NormalizePathSeparators(alias.RelativePath);
            return relativePath.Length == 0
                ? parent
                : Path.GetFullPath(Path.Combine(parent, relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        finally
        {
            resolving.Remove(alias.Name);
        }
    }

    private static bool IsXRayArchive(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xdb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xrp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var marker = name.LastIndexOf(".db", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 && IsArchiveSuffix(name.AsSpan(marker + 3));
    }

    private static bool IsShadowOfChernobylRootArchive(string path)
    {
        const string archivePrefix = "gamedata.db";
        var name = Path.GetFileName(path);
        return name.StartsWith(archivePrefix, StringComparison.OrdinalIgnoreCase) &&
            IsArchiveSuffix(name.AsSpan(archivePrefix.Length));
    }

    private static bool IsArchiveSuffix(ReadOnlySpan<char> suffix)
    {
        if (suffix.Length == 0 || suffix.IndexOfAnyExceptInRange('0', '9') < 0)
        {
            return true;
        }

        return suffix.Length == 1 && char.ToLowerInvariant(suffix[0]) is >= 'a' and <= 'd';
    }

    private static string NormalizePathSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim();

    private static StringComparer StringComparerForPaths() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
