using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Companion;

public sealed partial class CompanionInstaller
{
    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private bool FileMatches(string gameDirectory, string relativePath, string expectedSha)
    {
        var path = ResolveGamePath(gameDirectory, relativePath);
        return _fileSystem.FileExists(path) &&
            string.Equals(Sha256(_fileSystem.ReadAllBytes(path)), expectedSha, StringComparison.Ordinal);
    }

    private bool FileExistsInState(string gameDirectory, string relativePath) =>
        _fileSystem.FileExists(ResolveStatePath(gameDirectory, relativePath));

    private (string? Directory, string? Issue) ResolveGameDirectory(
        CompanionGameDefinition definition,
        string? selectedGameDirectory,
        IReadOnlyList<string>? steamRoots)
    {
        if (!string.IsNullOrWhiteSpace(selectedGameDirectory))
        {
            string selected;
            try
            {
                selected = Path.GetFullPath(selectedGameDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return (null, $"Selected game path is invalid: {exception.Message}");
            }

            return IsGameRoot(selected, definition)
                ? (selected, null)
                : (null, $"Selected directory is not a recognized {definition.Id} installation: {selected}");
        }

        var roots = steamRoots ?? SaveDirectoryLocator.DefaultSteamRoots();
        foreach (var library in SteamLibraryFolderLocator.GetLibraries(roots))
        {
            var manifestDirectory = SteamLibraryFolderLocator.GetManifestInstallDirectory(library, definition.SteamAppId);
            if (manifestDirectory is not null && IsGameRoot(manifestDirectory, definition))
            {
                return (Path.GetFullPath(manifestDirectory), null);
            }

            var commonDirectory = Path.Combine(library, "steamapps", "common");
            foreach (var name in definition.InstallDirectoryNames)
            {
                var candidate = Path.Combine(commonDirectory, name);
                if (IsGameRoot(candidate, definition))
                {
                    return (Path.GetFullPath(candidate), null);
                }
            }
        }

        if (steamRoots is null)
        {
            // Default discovery also looks at GOG, the retail installer and Heroic; explicit Steam roots stay exact.
            var others = GameInstallLocator.FindNonSteam(definition.Game);
            if (others.Count > 0) return (others[0].Directory, null);
        }

        return (null, $"Could not find an installed {definition.Id} game in Steam, GOG or the retail installer; choose its folder.");
    }

    private string ResolveRequiredGameDirectory(
        CompanionGameDefinition definition,
        string? selectedGameDirectory,
        IReadOnlyList<string>? steamRoots)
    {
        var result = ResolveGameDirectory(definition, selectedGameDirectory, steamRoots);
        return result.Directory ?? throw new CompanionInstallerException(
            result.Issue ?? $"Could not find the {definition.Id} installation.");
    }

    private bool IsGameRoot(string path, CompanionGameDefinition definition) =>
        _fileSystem.DirectoryExists(path) && definition.FsgameFileNames.Any(file =>
            _fileSystem.FileExists(Path.Combine(path, file)));

    private static string GetManifestPath(string gameDirectory) =>
        Path.Combine(gameDirectory, ManifestDirectoryName, ManifestFileName);

    private static string ResolveGamePath(string gameDirectory, string relativePath) =>
        ResolveUnderRoot(gameDirectory, ResolveGameRelative(relativePath));

    private static string ResolveStatePath(string gameDirectory, string relativePath) =>
        ResolveUnderRoot(Path.Combine(gameDirectory, ManifestDirectoryName), ResolveStateRelative(relativePath));

    private static string ResolveGameRelative(string path) => ResolveRelative(path, "gamedata/");

    private static string ResolveStateRelative(string path) => ResolveRelative(path, "backups/");

    private static string ResolveRelative(string path, string expectedPrefix)
    {
        var normalized = NormalizeRelative(path);
        if (!normalized.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) || normalized.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new CompanionInstallerException($"Unsafe path in companion manifest: {path}", path);
        }

        return normalized;
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison) && !string.Equals(fullPath, normalizedRoot, PathComparison))
        {
            throw new CompanionInstallerException($"Path escapes its expected root: {relativePath}", relativePath);
        }

        return fullPath;
    }

    private void EnsureSafeWritePath(string gameDirectory, string relativePath)
    {
        var path = ResolveUnderRoot(gameDirectory, relativePath.Replace('\\', '/'));
        var relative = Path.GetRelativePath(gameDirectory, path);
        var current = Path.GetFullPath(gameDirectory);
        CheckNotLink(current);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => segment.Length > 0))
        {
            current = Path.Combine(current, segment);
            if (_fileSystem.FileExists(current) || _fileSystem.DirectoryExists(current))
            {
                CheckNotLink(current);
            }
        }
    }

    private void CheckNotLink(string path)
    {
        if ((_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new CompanionInstallerException($"Refusing to write through a link or reparse point: {path}", path);
        }
    }

    private void AtomicWrite(string path, byte[] bytes, bool overwrite)
    {
        AtomicGameFileWriter.Write(_fileSystem, path, bytes, overwrite);
    }

    private void DeleteEmptyDirectoryTree(string path)
    {
        if (!_fileSystem.DirectoryExists(path))
        {
            return;
        }

        CheckNotLink(path);

        foreach (var child in _fileSystem.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            DeleteEmptyDirectoryTree(child);
        }

        if (!_fileSystem.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Any() &&
            !_fileSystem.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).Any())
        {
            _fileSystem.DeleteDirectory(path, recursive: false);
        }
    }

    private void RemoveManifestAndBackups(string gameDirectory, InstallManifest manifest)
    {
        var manifestPath = GetManifestPath(gameDirectory);
        EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, ManifestFileName));
        if (_fileSystem.FileExists(manifestPath))
        {
            _fileSystem.DeleteFile(manifestPath);
        }

        foreach (var entry in manifest.Files.Where(entry => entry.BackupPath is not null))
        {
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, entry.BackupPath!));
            var backup = ResolveStatePath(gameDirectory, entry.BackupPath!);
            if (_fileSystem.FileExists(backup))
            {
                _fileSystem.DeleteFile(backup);
            }
        }

        DeleteEmptyDirectoryTree(Path.GetDirectoryName(manifestPath)!);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
