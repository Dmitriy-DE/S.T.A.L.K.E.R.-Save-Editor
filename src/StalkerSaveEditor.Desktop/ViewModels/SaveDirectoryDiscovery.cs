namespace StalkerSaveEditor.Desktop.ViewModels;

public static class SaveDirectoryDiscovery
{
    private static readonly string[] DocumentFolderNames =
    [
        "Documents",
        "My Documents",
        "Документы",
        "Документи",
        "Documentos",
        "Dokumente",
        "Documenti",
    ];

    private static readonly string[][] EnhancedReleaseNames =
    [
        ["STALKER Shadow of Chornobyl - EE"],
        ["STALKER Clear Sky - EE"],
        ["STALKER Call of Prypiat - EE", "STALKER Call of Pripyat - EE"],
    ];

    public static IReadOnlyList<string> GetExistingDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>();
        var documentRoots = new List<string>();
        if (!string.IsNullOrWhiteSpace(documents)) documentRoots.Add(documents);
        if (!string.IsNullOrWhiteSpace(home))
        {
            documentRoots.AddRange(DocumentFolderNames.Select(name => Path.Combine(home, name)));
        }

        AddXRayDirectories(candidates, documentRoots);
        if (!string.IsNullOrWhiteSpace(home)) AddEnhancedDirectories(candidates, home);
        if (!string.IsNullOrWhiteSpace(documents)) AddEnhancedDirectories(candidates, documents);
        if (!string.IsNullOrWhiteSpace(localApplicationData)) AddStalker2Directories(candidates, localApplicationData);
        if (!string.IsNullOrWhiteSpace(home))
        {
            AddSteamInstallDirectories(candidates, home);
            AddProtonDirectories(candidates, home);
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        return candidates
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Where(path => seen.Add(StalkerSaveEditor.Core.Storage.SaveSlotDiscovery.ResolveLinks(path))) // ~/.steam/steam is a link
            .ToArray();
    }

    private static void AddXRayDirectories(List<string> candidates, IEnumerable<string> documentRoots)
    {
        var gameFolders = new[]
        {
            new[] { "stalker-shoc", "Stalker-SHOC" },
            new[] { "Stalker-STCS" },
            new[] { "S.T.A.L.K.E.R. - Call of Pripyat", "Stalker-COP" },
        };

        foreach (var root in documentRoots)
        {
            foreach (var folders in gameFolders)
            {
                foreach (var folder in folders)
                {
                    candidates.Add(Path.Combine(root, folder, "savedgames"));
                }
            }
        }
    }

    private static void AddEnhancedDirectories(List<string> candidates, string root)
    {
        foreach (var savedGamesRoot in new[] { "Saved Games", "My Saved Games", "Сохраненные игры", "Сохранённые игры", "Збережені ігри" })
        {
            foreach (var releaseNames in EnhancedReleaseNames)
            {
                foreach (var release in releaseNames)
                {
                    candidates.Add(Path.Combine(root, savedGamesRoot, release, "STEAM", "savedgames"));
                    candidates.Add(Path.Combine(root, savedGamesRoot, release, "gog", "savedgames"));
                }
            }
        }
    }

    private static void AddStalker2Directories(List<string> candidates, string localApplicationData)
    {
        var saved = Path.Combine(localApplicationData, "Stalker2", "Saved");
        foreach (var store in new[] { "", "STEAM", "EOS", "GOG" })
        {
            var storeRoot = store.Length == 0 ? saved : Path.Combine(saved, store);
            candidates.Add(Path.Combine(storeRoot, "SaveGames"));
            candidates.Add(Path.Combine(storeRoot, "SaveGames", "Data"));
        }
    }

    private static void AddSteamInstallDirectories(List<string> candidates, string home)
    {
        var steamRoots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            steamRoots.Add(@"C:\Program Files (x86)\Steam");
            steamRoots.Add(@"C:\Program Files\Steam");
        }
        else
        {
            steamRoots.Add(Path.Combine(home, ".steam", "steam"));
            steamRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
            steamRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam"));
        }

        var commonRelative = new[]
        {
            Path.Combine("steamapps", "common", "STALKER Shadow of Chernobyl", "_appdata_", "savedgames"),
            Path.Combine("steamapps", "common", "STALKER Clear Sky", "_appdata_", "savedgames"),
            Path.Combine("steamapps", "common", "Stalker Call of Pripyat", "_appdata_", "savedgames"),
        };

        foreach (var root in steamRoots)
        {
            foreach (var rel in commonRelative)
            {
                candidates.Add(Path.Combine(root, rel));
            }
        }
    }

    private static void AddProtonDirectories(List<string> candidates, string home)
    {
        var steamRoots = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam"),
        };

        var appProfiles = new (string AppId, string[] Paths)[]
        {
            ("4500", [
                "pfx/drive_c/users/steamuser/Documents/Stalker-SHOC/savedgames",
                "pfx/drive_c/users/Public/Documents/stalker-shoc/savedgames"
            ]),
            ("20510", [
                "pfx/drive_c/users/steamuser/Documents/Stalker-STCS/savedgames"
            ]),
            ("41700", [
                "pfx/drive_c/users/steamuser/Documents/Stalker-COP/savedgames",
                "pfx/drive_c/users/Public/Documents/S.T.A.L.K.E.R. - Call of Pripyat/savedgames"
            ]),
            ("2427410", [
                "pfx/drive_c/users/steamuser/Saved Games/STALKER Shadow of Chornobyl - EE/STEAM/savedgames"
            ]),
            ("2427420", [
                "pfx/drive_c/users/steamuser/Saved Games/STALKER Clear Sky - EE/STEAM/savedgames"
            ]),
            ("2427430", [
                "pfx/drive_c/users/steamuser/Saved Games/STALKER Call of Prypiat - EE/STEAM/savedgames"
            ]),
            ("1643320", [
                "pfx/drive_c/users/steamuser/AppData/Local/Stalker2/Saved/SaveGames",
                "pfx/drive_c/users/steamuser/AppData/Local/Stalker2/Saved/STEAM/SaveGames",
                "pfx/drive_c/users/steamuser/Local Settings/Application Data/Stalker2/Saved/STEAM/SaveGames/Data"
            ]),
        };

        foreach (var steamRoot in steamRoots)
        {
            var compat = Path.Combine(steamRoot, "steamapps", "compatdata");
            foreach (var (appId, relPaths) in appProfiles)
            {
                foreach (var relPath in relPaths)
                {
                    candidates.Add(Path.Combine(compat, appId, relPath));
                }
            }
        }
    }
}
