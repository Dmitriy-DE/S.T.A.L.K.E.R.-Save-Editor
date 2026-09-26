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

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        return candidates
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Where(seen.Add)
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
}
