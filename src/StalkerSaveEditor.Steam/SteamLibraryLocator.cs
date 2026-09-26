using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Steam;

internal static partial class SteamLibraryLocator
{
    public static string? FindLibraryPath() => FindLibraryPath(GetDefaultRoots(), OperatingSystem.IsWindows());

    internal static string? FindLibraryPath(IEnumerable<string> roots, bool isWindows)
    {
        var libraryName = isWindows ? "steam_api64.dll" : "libsteam_api.so";
        foreach (var root in ExpandSteamLibraries(roots))
        {
            var runtimeCandidates = new[]
            {
                Path.Combine(root, "steamrt64", "libsteam_api.so"),
                Path.Combine(root, "libsteam_api.so"),
            };
            if (!isWindows && runtimeCandidates.FirstOrDefault(File.Exists) is { } runtimeCandidate)
            {
                return runtimeCandidate;
            }

            if (isWindows)
            {
                var directCandidates = new[]
                {
                    Path.Combine(root, "steamapps", "common", "Stalker 2", "Binaries", "Win64", libraryName),
                    Path.Combine(root, "steamapps", "common", "S.T.A.L.K.E.R. 2", "Binaries", "Win64", libraryName),
                };
                var direct = directCandidates.FirstOrDefault(File.Exists);
                if (direct is not null)
                {
                    return direct;
                }

                var common = Path.Combine(root, "steamapps", "common");
                if (!Directory.Exists(common))
                {
                    continue;
                }

                foreach (var gameDirectory in Directory.EnumerateDirectories(common))
                {
                    var candidate = Path.Combine(gameDirectory, "Binaries", "Win64", libraryName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            else
            {
                var common = Path.Combine(root, "steamapps", "common");
                if (!Directory.Exists(common))
                {
                    continue;
                }

                foreach (var gameDirectory in Directory.EnumerateDirectories(common))
                {
                    var candidate = Path.Combine(gameDirectory, "Binaries", "Linux", libraryName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetDefaultRoots()
    {
        var roots = new List<string>();
        AddEnvironmentRoot(roots, "STEAM_DIR");
        if (OperatingSystem.IsWindows())
        {
            AddEnvironmentSteamRoot(roots, "ProgramFiles(x86)", "Steam");
            AddEnvironmentSteamRoot(roots, "ProgramFiles", "Steam");
            AddEnvironmentSteamRoot(roots, "LocalAppData", "Programs", "Steam");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                roots.Add(Path.Combine(home, ".steam", "steam"));
                roots.Add(Path.Combine(home, ".steam", "root"));
                roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            }

            roots.Add("/usr/lib/steam");
            roots.Add("/usr/lib/steam/steam");
        }

        return roots;
    }

    private static void AddEnvironmentRoot(ICollection<string> roots, string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            roots.Add(value);
        }
    }

    private static void AddEnvironmentSteamRoot(
        ICollection<string> roots,
        string variable,
        params string[] segments)
    {
        var basePath = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(basePath))
        {
            roots.Add(Path.Combine([basePath, .. segments]));
        }
    }

    private static IEnumerable<string> ExpandSteamLibraries(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = Path.GetFullPath(root);
            if (seen.Add(normalized))
            {
                yield return normalized;
            }

            var vdfPath = Path.Combine(normalized, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                continue;
            }

            foreach (Match match in LibraryPathRegex().Matches(File.ReadAllText(vdfPath)))
            {
                var libraryPath = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                if (Path.IsPathRooted(libraryPath))
                {
                    var fullPath = Path.GetFullPath(libraryPath);
                    if (seen.Add(fullPath))
                    {
                        yield return fullPath;
                    }
                }
            }
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();
}
