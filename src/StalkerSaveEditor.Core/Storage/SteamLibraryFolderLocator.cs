namespace StalkerSaveEditor.Core.Storage;

public static class SteamLibraryFolderLocator
{
    public static IReadOnlyList<string> GetLibraries(IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(steamRoots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var libraries = new List<string>();
        var seen = new HashSet<string>(comparer);
        foreach (var root in steamRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
                if (!Directory.Exists(fullRoot) || !seen.Add(fullRoot))
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                continue;
            }

            libraries.Add(fullRoot);
            var libraryFile = Path.Combine(fullRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            try
            {
                var document = SteamVdfParser.Parse(File.ReadAllText(libraryFile));
                if (!SteamVdfParser.TryGetObject(document, "libraryfolders", out var folders))
                {
                    continue;
                }

                foreach (var entry in folders.Values)
                {
                    var path = entry switch
                    {
                        string legacyPath => legacyPath,
                        IReadOnlyDictionary<string, object> properties when SteamVdfParser.TryGetString(properties, "path", out var modernPath) => modernPath,
                        _ => null,
                    };
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    try
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (Directory.Exists(fullPath) && seen.Add(fullPath))
                        {
                            libraries.Add(fullPath);
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
                    {
                        // One unusable library entry must not hide the remaining Steam libraries.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                // A malformed library file is isolated to its Steam root.
            }
        }

        return libraries.AsReadOnly();
    }

    public static string? GetManifestInstallDirectory(string libraryRoot, int appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appId);

        var library = Path.GetFullPath(libraryRoot);
        var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
        try
        {
            var document = SteamVdfParser.Parse(File.ReadAllText(manifest));
            if (!SteamVdfParser.TryGetObject(document, "AppState", out var appState) ||
                !SteamVdfParser.TryGetString(appState, "appid", out var manifestAppId) ||
                !int.TryParse(manifestAppId, out var actualAppId) || actualAppId != appId ||
                !SteamVdfParser.TryGetString(appState, "installdir", out var installDirectory) ||
                string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(library, "steamapps", "common", installDirectory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
