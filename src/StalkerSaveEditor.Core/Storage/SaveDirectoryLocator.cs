using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StalkerSaveEditor.Core.Storage;

public enum SaveDiscoveryPlatform
{
    Current,
    Windows,
    Linux,
    MacOS,
}

public sealed record SaveDirectoryCandidate(string GameId, string ReleaseId, string DirectoryPath);

public sealed class SaveDirectoryDiscoveryOptions
{
    public SaveDiscoveryPlatform Platform { get; init; } = SaveDiscoveryPlatform.Current;

    public string? HomeDirectory { get; init; }

    public string? UserProfileDirectory { get; init; }

    public string? PublicDirectory { get; init; }

    public string? LocalAppDataDirectory { get; init; }

    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public IReadOnlyList<string>? SteamRoots { get; init; }
}

public static partial class SaveDirectoryLocator
{
    private static readonly ReleaseLocation[] Releases =
    [
        new("stalker2", "stalker2", "s2", 1_643_320,
            ["S.T.A.L.K.E.R. 2 Heart of Chornobyl", "STALKER 2 Heart of Chornobyl", "S.T.A.L.K.E.R. 2"]),
        new("soc", "stalker-soc", "original", 4_500,
            ["STALKER Shadow of Chernobyl", "STALKER Shadow of Chornobyl"]),
        new("clear_sky", "stalker-cs", "original", 20_510, ["STALKER Clear Sky"]),
        new("cop", "stalker-cop", "original", 41_700,
            ["Stalker Call of Pripyat", "STALKER Call of Pripyat"]),
        new("soc", "stalker-soc-ee", "enhanced", 2_427_410, ["STALKER Shadow of Chornobyl - Enhanced Edition"]),
        new("clear_sky", "stalker-cs-ee", "enhanced", 2_427_420, ["STALKER Clear Sky - Enhanced Edition"]),
        new("cop", "stalker-cop-ee", "enhanced", 2_427_430, ["STALKER Call of Prypiat - Enhanced Edition"]),
    ];

    private static readonly Dictionary<string, string[]> XRaySaveFolderNames =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["soc"] = ["stalker-shoc", "Stalker-SHOC"],
            ["clear_sky"] = ["Stalker-STCS"],
            ["cop"] = ["S.T.A.L.K.E.R. - Call of Pripyat", "Stalker-COP"],
        };

    private static readonly Dictionary<string, string[]> EnhancedSaveFolderNames =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["stalker-soc-ee"] = ["STALKER Shadow of Chornobyl - EE"],
            ["stalker-cs-ee"] = ["STALKER Clear Sky - EE"],
            ["stalker-cop-ee"] = ["STALKER Call of Prypiat - EE", "STALKER Call of Pripyat - EE"],
        };

    private static readonly string[] DocumentFolderNames =
    [
        "Documents", "My Documents", "Mes documents", "Documentos", "Dokumente", "Documenti",
        "Dokumenty", "Dokumenti", "Документы", "Документи", "文档",
    ];

    private static readonly string[] SavedGamesFolderNames =
    [
        "Saved Games", "My Saved Games", "Parties enregistrées", "Gespeicherte Spiele",
        "Partite salvate", "Сохраненные игры", "Сохранённые игры", "Збережені ігри",
    ];

    private static readonly string[] S2Profiles = ["SaveGames", "STEAM/SaveGames", "EOS/SaveGames", "GOG/SaveGames"];

    public static IReadOnlyList<SaveDirectoryCandidate> FindCandidateDirectories(
        SaveDirectoryDiscoveryOptions? options = null)
    {
        options ??= new SaveDirectoryDiscoveryOptions();
        var environment = options.Environment ?? ReadEnvironment();
        var platform = GetPlatform(options.Platform);
        var home = FullPath(options.HomeDirectory ?? GetEnvironment(environment, "HOME") ??
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var userProfile = FullPath(options.UserProfileDirectory ?? GetEnvironment(environment, "USERPROFILE") ?? home);
        var publicDirectory = options.PublicDirectory ?? GetEnvironment(environment, "PUBLIC");
        var localAppData = FullPath(options.LocalAppDataDirectory ?? GetEnvironment(environment, "LOCALAPPDATA") ??
            (platform == SaveDiscoveryPlatform.Windows
                ? Path.Combine(userProfile, "AppData", "Local")
                : Path.Combine(home, "AppData", "Local")));
        var steamRoots = options.SteamRoots ?? GetDefaultSteamRoots(platform, home, localAppData, environment);
        var libraries = SteamLibraryFolderLocator.GetLibraries(steamRoots);
        var candidates = new List<SaveDirectoryCandidate>();
        var seen = new HashSet<string>(PathComparer);

        var documentRoots = GetKnownRoots([home, userProfile, publicDirectory], DocumentFolderNames);
        var savedGameRoots = GetKnownRoots([home, userProfile], SavedGamesFolderNames);
        foreach (var release in Releases)
        {
            if (release.Edition == "original")
            {
                foreach (var root in documentRoots)
                {
                    foreach (var folder in XRaySaveFolderNames[release.Family])
                    {
                        Add(candidates, seen, release, Path.Combine(root, folder, "savedgames"));
                    }
                }
            }
            else if (release.Edition == "enhanced")
            {
                foreach (var root in savedGameRoots)
                {
                    foreach (var name in EnhancedSaveFolderNames[release.Id])
                    {
                        Add(candidates, seen, release, Path.Combine(root, name, "STEAM", "savedgames"));
                        Add(candidates, seen, release, Path.Combine(root, name, "gog", "savedgames"));
                    }
                }
            }
        }

        AddStalker2LocalCandidates(candidates, seen, Releases[0], localAppData);
        AddStalker2PackageCandidates(candidates, seen, Releases[0], localAppData);

        foreach (var library in libraries)
        {
            foreach (var release in Releases)
            {
                var installDirectory = FindInstallDirectory(library, release);
                if (installDirectory is not null)
                {
                    var fsgameDirectory = TryReadSaveDirectory(installDirectory, release.Family, environment);
                    if (fsgameDirectory is not null)
                    {
                        Add(candidates, seen, release, fsgameDirectory);
                    }

                    if (release.Edition == "original")
                    {
                        Add(candidates, seen, release, Path.Combine(installDirectory, "_appdata_", "savedgames"));
                    }
                }

                if (platform == SaveDiscoveryPlatform.Linux)
                {
                    AddProtonCandidates(candidates, seen, release, library);
                }
            }

            // Original Steam installs can retain their save folder after the app manifest is removed.
            foreach (var release in Releases.Where(release => release.Edition == "original"))
            {
                foreach (var installName in release.InstallDirectories)
                {
                    Add(candidates, seen, release,
                        Path.Combine(library, "steamapps", "common", installName, "_appdata_", "savedgames"));
                }
            }

            if (platform == SaveDiscoveryPlatform.Linux)
            {
                AddStalker2ProtonCandidates(candidates, seen, Releases[0], library);
            }
        }

        return candidates.AsReadOnly();
    }

    private static void AddStalker2LocalCandidates(
        List<SaveDirectoryCandidate> candidates,
        HashSet<string> seen,
        ReleaseLocation release,
        string localAppData)
    {
        var saved = Path.Combine(localAppData, "Stalker2", "Saved");
        foreach (var profile in S2Profiles)
        {
            var root = Path.Combine(saved, profile.Replace('/', Path.DirectorySeparatorChar));
            Add(candidates, seen, release, root);
            Add(candidates, seen, release, Path.Combine(root, "Data"));
        }
    }

    private static void AddStalker2PackageCandidates(
        List<SaveDirectoryCandidate> candidates,
        HashSet<string> seen,
        ReleaseLocation release,
        string localAppData)
    {
        var xgs = Path.Combine(localAppData, "Packages", "GSCGameWorld.S.T.A.L.K.E.R.2HeartofChornobyl_6fr1t1rwfarwt", "SystemAppData", "xgs");
        try
        {
            if (!Directory.Exists(xgs))
            {
                return;
            }

            foreach (var userDirectory in Directory.EnumerateDirectories(xgs))
            {
                Add(candidates, seen, release, Path.Combine(userDirectory, "SaveGames"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable optional package profile does not stop discovery elsewhere.
        }
    }

    private static void AddProtonCandidates(
        List<SaveDirectoryCandidate> candidates,
        HashSet<string> seen,
        ReleaseLocation release,
        string library)
    {
        var driveC = Path.Combine(library, "steamapps", "compatdata", release.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "pfx", "drive_c");
        if (release.Family == "stalker2")
        {
            AddStalker2ProtonCandidates(candidates, seen, release, library);
            return;
        }

        foreach (var user in GetProtonUserDirectories(driveC))
        {
            foreach (var folder in XRaySaveFolderNames[release.Family])
            {
                Add(candidates, seen, release, Path.Combine(user, "Documents", folder, "savedgames"));
                Add(candidates, seen, release, Path.Combine(driveC, "ProgramData", "Documents", folder, "savedgames"));
            }

            if (release.Edition == "enhanced")
            {
                foreach (var folder in EnhancedSaveFolderNames[release.Id])
                {
                    Add(candidates, seen, release, Path.Combine(user, "Saved Games", folder, "STEAM", "savedgames"));
                    Add(candidates, seen, release, Path.Combine(user, "Saved Games", folder, "gog", "savedgames"));
                }
            }
        }
    }

    private static void AddStalker2ProtonCandidates(
        List<SaveDirectoryCandidate> candidates,
        HashSet<string> seen,
        ReleaseLocation release,
        string library)
    {
        var driveC = Path.Combine(library, "steamapps", "compatdata", release.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "pfx", "drive_c");
        foreach (var user in GetProtonUserDirectories(driveC))
        {
            foreach (var local in new[]
            {
                Path.Combine(user, "AppData", "Local"),
                Path.Combine(user, "Local Settings", "Application Data"),
            })
            {
                var saved = Path.Combine(local, "Stalker2", "Saved");
                foreach (var profile in S2Profiles)
                {
                    var root = Path.Combine(saved, profile.Replace('/', Path.DirectorySeparatorChar));
                    Add(candidates, seen, release, root);
                    Add(candidates, seen, release, Path.Combine(root, "Data"));
                }
            }
        }
    }

    private static List<string> GetProtonUserDirectories(string driveC)
    {
        var usersRoot = Path.Combine(driveC, "users");
        var users = new List<string>();
        try
        {
            if (Directory.Exists(usersRoot))
            {
                users.AddRange(Directory.EnumerateDirectories(usersRoot));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The known Public profile remains a useful fallback if profile enumeration is unavailable.
        }

        var publicProfile = Path.Combine(usersRoot, "Public");
        if (!users.Contains(publicProfile, PathComparer))
        {
            users.Add(publicProfile);
        }

        return users;
    }

    private static string? FindInstallDirectory(string library, ReleaseLocation release)
    {
        var manifestPath = SteamLibraryFolderLocator.GetManifestInstallDirectory(library, release.AppId);
        if (manifestPath is not null && Directory.Exists(manifestPath))
        {
            return manifestPath;
        }

        var common = Path.Combine(library, "steamapps", "common");
        foreach (var directoryName in release.InstallDirectories)
        {
            var candidate = Path.Combine(common, directoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            var wanted = new HashSet<string>(release.InstallDirectories, StringComparer.OrdinalIgnoreCase);
            return Directory.EnumerateDirectories(common)
                .FirstOrDefault(path => wanted.Contains(Path.GetFileName(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryReadSaveDirectory(
        string installDirectory,
        string gameId,
        IReadOnlyDictionary<string, string> environment)
    {
        var filenames = gameId == "soc" ? new[] { "fsgame_soc.ltx", "fsgame.ltx" } : ["fsgame.ltx"];
        foreach (var filename in filenames)
        {
            var path = Path.Combine(installDirectory, filename);
            if (!File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var definitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Split(';', 2)[0].Trim();
                var match = FsgameLineRegex().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var values = match.Groups[2].Value.Split('|').Select(value => value.Trim().Trim('"')).ToArray();
                if (values.Length >= 4)
                {
                    definitions[match.Groups[1].Value] = values;
                }
            }

            return ResolveFsgameAlias("$game_saves$", definitions, installDirectory, environment, []);
        }

        return null;
    }

    private static string? ResolveFsgameAlias(
        string alias,
        IReadOnlyDictionary<string, string[]> definitions,
        string installDirectory,
        IReadOnlyDictionary<string, string> environment,
        HashSet<string> stack)
    {
        if (string.Equals(alias, "$fs_root$", StringComparison.OrdinalIgnoreCase))
        {
            return installDirectory;
        }

        if (!stack.Add(alias) || !definitions.TryGetValue(alias, out var values) || values.Length < 4)
        {
            return null;
        }

        try
        {
            var parentValue = ExpandEnvironment(values[2], environment);
            string? parent;
            if (parentValue.StartsWith('$') && parentValue.EndsWith('$'))
            {
                parent = ResolveFsgameAlias(parentValue, definitions, installDirectory, environment, stack);
            }
            else if (Path.IsPathRooted(parentValue))
            {
                parent = NormalizePath(parentValue);
            }
            else
            {
                parent = Path.GetFullPath(Path.Combine(installDirectory, NormalizePath(parentValue)));
            }

            if (parent is null)
            {
                return null;
            }

            var child = ExpandEnvironment(values[3], environment);
            return Path.GetFullPath(Path.Combine(parent, NormalizePath(child)));
        }
        finally
        {
            stack.Remove(alias);
        }
    }

    private static ReadOnlyCollection<string> GetDefaultSteamRoots(
        SaveDiscoveryPlatform platform,
        string home,
        string localAppData,
        IReadOnlyDictionary<string, string> environment)
    {
        if (platform == SaveDiscoveryPlatform.Windows)
        {
            var roots = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var steamKey = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam");
                    if (steamKey?.GetValue("SteamPath") is string registryPath && !string.IsNullOrWhiteSpace(registryPath))
                    {
                        roots.Add(registryPath);
                    }
                }
                catch (System.Security.SecurityException)
                {
                    // Standard Steam install locations below remain available if the registry is unreadable.
                }
            }

            foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
            {
                var basePath = GetEnvironment(environment, variable);
                if (!string.IsNullOrWhiteSpace(basePath))
                {
                    roots.Add(Path.Combine(basePath, "Steam"));
                }
            }

            roots.Add(Path.Combine(localAppData, "Programs", "Steam"));
            return roots.AsReadOnly();
        }

        if (platform == SaveDiscoveryPlatform.MacOS)
        {
            return Array.AsReadOnly(new[] { Path.Combine(home, "Library", "Application Support", "Steam") });
        }

        return Array.AsReadOnly(new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
        });
    }

    private static ReadOnlyCollection<string> GetKnownRoots(
        IReadOnlyList<string?> parents,
        IReadOnlyList<string> knownNames)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var parent in parents)
        {
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            foreach (var knownName in knownNames)
            {
                AddRoot(Path.Combine(parent, knownName));
            }

            try
            {
                if (!Directory.Exists(parent))
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(parent))
                {
                    if (knownNames.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                    {
                        AddRoot(child);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Fixed candidate paths are still returned when localized folder enumeration fails.
            }
        }

        return roots.AsReadOnly();

        void AddRoot(string path)
        {
            var fullPath = Path.TrimEndingDirectorySeparator(FullPath(path));
            if (seen.Add(fullPath))
            {
                roots.Add(fullPath);
            }
        }
    }

    private static void Add(
        List<SaveDirectoryCandidate> candidates,
        HashSet<string> seen,
        ReleaseLocation release,
        string path)
    {
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(FullPath(path));
            var key = $"{release.Id}\0{fullPath}";
            if (seen.Add(key))
            {
                candidates.Add(new SaveDirectoryCandidate(release.Family, release.Id, fullPath));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            // Ignore an invalid candidate and continue searching the remaining known locations.
        }
    }

    private static Dictionary<string, string> ReadEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string? GetEnvironment(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string ExpandEnvironment(string value, IReadOnlyDictionary<string, string> environment) =>
        EnvironmentVariableRegex().Replace(value, match =>
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return GetEnvironment(environment, name) ?? match.Value;
        });

    private static string NormalizePath(string path) => OperatingSystem.IsWindows()
        ? path.Replace('/', Path.DirectorySeparatorChar)
        : path.Replace('\\', Path.DirectorySeparatorChar);

    private static string FullPath(string path) => Path.GetFullPath(path);

    private static SaveDiscoveryPlatform GetPlatform(SaveDiscoveryPlatform requested) => requested != SaveDiscoveryPlatform.Current
        ? requested
        : OperatingSystem.IsWindows()
            ? SaveDiscoveryPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? SaveDiscoveryPlatform.MacOS
                : SaveDiscoveryPlatform.Linux;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    [GeneratedRegex("^(\\$[^$]+\\$)\\s*=\\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FsgameLineRegex();

    [GeneratedRegex("%([^%]+)%|\\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariableRegex();

    private sealed record ReleaseLocation(
        string Family,
        string Id,
        string Edition,
        int AppId,
        string[] InstallDirectories);
}
