using System.Security.Cryptography;
using System.Text;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Patching;

namespace StalkerSaveEditor.Core.Content;

/// <summary>One logical file of the game's data tree, relative to <c>$game_data$</c>.</summary>
internal sealed class GameFile(string relativePath, string origin, Func<byte[]> read)
{
    public string RelativePath { get; } = relativePath;

    /// <summary>Archive path or "gamedata" for loose files.</summary>
    public string Origin { get; } = origin;

    public byte[] Read() => read();
}

/// <summary>
/// The view of the game's data the engine itself would see for a set of path prefixes:
/// archives from <c>fsgame.ltx</c> in LocatorAPI order (later archives override earlier ones),
/// then loose files in <c>$game_data$</c> on top.
/// </summary>
internal sealed class GameFileTree
{
    private GameFileTree(
        IReadOnlyDictionary<string, GameFile> files,
        string fingerprint,
        bool hasLooseOverlay,
        string configPrefix,
        string? dataDirectory,
        IReadOnlyList<string> issues)
    {
        Files = files;
        DataDirectory = dataDirectory;
        ConfigPrefix = configPrefix;
        Fingerprint = fingerprint;
        HasLooseOverlay = hasLooseOverlay;
        Issues = issues;
    }

    public IReadOnlyDictionary<string, GameFile> Files { get; }

    /// <summary>SHA-256 over the install path, archive names/sizes/times, the checksums of the used archive entries, and loose-file names/sizes/times.</summary>
    public string Fingerprint { get; }

    /// <summary>True when loose files in gamedata override at least one wanted path (a mod or unpacked data).</summary>
    public bool HasLooseOverlay { get; }

    public IReadOnlyList<string> Issues { get; }

    /// <summary>Config directory relative to <c>$game_data$</c> with a trailing slash ("config/" in SoC).</summary>
    public string ConfigPrefix { get; }

    /// <summary>Absolute <c>$game_data$</c> directory, when fsgame defines it.</summary>
    public string? DataDirectory { get; }

    public static GameFileTree Load(
        CompanionGame game,
        string gameDirectory,
        Func<string, bool> wanted,
        IGameFileSystem? fileSystem = null,
        IReadOnlyList<string>? fsgameFileNames = null)
    {
        fileSystem ??= new PhysicalGameFileSystem();
        var search = CompanionArchiveLocator.Discover(fileSystem, gameDirectory, fsgameFileNames ?? ["fsgame.ltx"], game);
        var issues = new List<string>(search.Issues);
        var files = new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase);
        var stamp = new StringBuilder();
        // The install itself is part of the identity: two installs with equal names, sizes and times are still two
        // installs, and a cache built for one must not be served for the other.
        stamp.Append(CultureInvariant($"R|{Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}\n"));

        foreach (var archivePath in search.ArchivePaths)
        {
            try
            {
                var info = new FileInfo(archivePath);
                stamp.Append(CultureInvariant($"A|{Path.GetFileName(archivePath)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n"));
                using var stream = fileSystem.OpenRead(archivePath);
                using var archive = XRayArchiveReader.Open(stream);
                foreach (var entry in archive.Entries)
                {
                    var relative = Normalize(entry.Name);
                    if (relative.Length == 0 || relative.EndsWith('/') || !wanted(relative)) continue;
                    // The archive's own checksum of every file that is used: a repacked archive with the same size
                    // and time still changes the fingerprint, without hashing gigabytes.
                    stamp.Append(CultureInvariant($"E|{relative}|{entry.UncompressedSize}|{entry.CompressedSize}|{entry.Crc32}|{entry.Offset}\n"));
                    var bytes = archive.ReadFile(entry.Name);
                    files[relative] = new GameFile(relative, archivePath, () => bytes);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XRayFormatException or InvalidDataException)
            {
                issues.Add($"Could not read archive {Path.GetFileName(archivePath)}: {exception.Message}");
            }
        }

        var overlay = false;
        var dataRoot = search.GameDataDirectory;
        if (dataRoot is not null && fileSystem.DirectoryExists(dataRoot))
        {
            foreach (var path in fileSystem.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Normalize(Path.GetRelativePath(dataRoot, path));
                if (!wanted(relative)) continue;
                var info = new FileInfo(path);
                stamp.Append(CultureInvariant($"L|{relative}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n"));
                // Files the companion installer patched or added are not a mod overlay.
                if (!relative.Contains("save_editor", StringComparison.OrdinalIgnoreCase)) overlay = true;
                var captured = path;
                files[relative] = new GameFile(relative, "gamedata", () => fileSystem.ReadAllBytes(captured));
            }
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp.ToString()))).ToLowerInvariant();
        var configPrefix = game == CompanionGame.ShadowOfChernobyl ? "config/" : "configs/";
        if (search.GameConfigDirectory is not null && dataRoot is not null)
        {
            var relativeConfig = Normalize(Path.GetRelativePath(dataRoot, search.GameConfigDirectory)).TrimEnd('/');
            if (relativeConfig.Length > 0 && !relativeConfig.StartsWith("..", StringComparison.Ordinal))
            {
                configPrefix = relativeConfig + "/";
            }
        }

        return new GameFileTree(files, fingerprint, overlay, configPrefix, dataRoot, issues.AsReadOnly());
    }

    private static string Normalize(string name)
    {
        var value = name.Replace('\\', '/').TrimStart('/');
        return value.StartsWith("gamedata/", StringComparison.OrdinalIgnoreCase) ? value["gamedata/".Length..] : value;
    }

    private static string CultureInvariant(FormattableString value) => FormattableString.Invariant(value);
}
