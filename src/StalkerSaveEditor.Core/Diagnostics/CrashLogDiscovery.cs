using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Diagnostics;

public sealed record DiscoveredCrashLog(
    GameTarget Game,
    string? BuildId,
    string Path,
    DateTimeOffset LastWriteTimeUtc,
    long Length);

/// <summary>Finds recent X-Ray logs from discovered trilogy installs and existing save-location candidates.</summary>
public static class CrashLogDiscovery
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// The newest log or minidump in the game's own log folders; null when there is none. A dump is preferred to a log
    /// that holds no crash: the game overwrites its log on the next start, the dump of the crash stays.
    /// </summary>
    public static CrashLogAnalysis? AnalyzeLatestInGameDirectory(string gameDirectory, string? game = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var files = new[] { "logs", Path.Combine("_appdata_", "logs"), Path.Combine("_appdata_", "log") }
            .Select(folder => Path.Combine(gameDirectory, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => new DirectoryInfo(folder).EnumerateFiles())
            .Where(file => IsCrashFile(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        if (files.Length == 0) return null;

        var newest = CrashLogAnalyzer.AnalyzeFile(files[0].FullName, game);
        if (newest.Kind != CrashLogKind.Unknown) return newest;
        var dump = files.FirstOrDefault(file => file.Extension.Equals(".mdmp", StringComparison.OrdinalIgnoreCase));
        return dump is null || dump == files[0] ? newest : CrashLogAnalyzer.AnalyzeFile(dump.FullName, game);
    }

    private static bool IsCrashFile(string name) =>
        Path.GetExtension(name) is { } extension &&
        (extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mdmp", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<DiscoveredCrashLog> DiscoverRecentLogs(
        IReadOnlyList<GameDoctorInstallation>? installations = null,
        SaveDirectoryDiscoveryOptions? saveDirectoryOptions = null,
        int maxResults = 20)
    {
        if (maxResults is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maxResults));
        saveDirectoryOptions ??= new SaveDirectoryDiscoveryOptions();
        installations ??= GameDoctor.DiscoverInstallations(saveDirectoryOptions.SteamRoots);

        var directories = new Dictionary<string, (GameTarget Game, string? BuildId)>(PathComparer);
        foreach (var installation in installations)
        {
            if (!IsOriginalTrilogy(installation.Target)) continue;
            AddDirectory(Path.Combine(installation.Directory, "logs"), installation.Target, installation.BuildId);
            AddDirectory(Path.Combine(installation.Directory, "_appdata_", "logs"), installation.Target, installation.BuildId);
            AddDirectory(Path.Combine(installation.Directory, "_appdata_", "log"), installation.Target, installation.BuildId);
        }

        var installationByTargetAndPath = installations
            .Where(installation => IsOriginalTrilogy(installation.Target))
            .ToDictionary(
                installation => (installation.Target, CanonicalDirectory(installation.Directory)),
                installation => installation.BuildId);
        foreach (var candidate in SaveDirectoryLocator.FindCandidateDirectories(saveDirectoryOptions))
        {
            if (!TryGetOriginalTarget(candidate.ReleaseId, out var target)) continue;
            var saveDirectory = Path.GetFullPath(candidate.DirectoryPath);
            var profileDirectory = Directory.GetParent(saveDirectory)?.FullName;
            if (profileDirectory is null) continue;

            string? buildId = null;
            // Save candidates inside a known install inherit its build; user-profile and Proton
            // candidates remain build-unknown unless Game Doctor resolved the install itself.
            var candidateIdentity = CanonicalDirectory(profileDirectory);
            if (installationByTargetAndPath.TryGetValue((target, candidateIdentity), out var matchedBuild))
                buildId = matchedBuild;
            AddDirectory(Path.Combine(profileDirectory, "logs"), target, buildId);
        }

        var files = new List<DiscoveredCrashLog>();
        foreach (var directory in directories)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory.Key, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!IsCrashFile(path)) continue;
                    try
                    {
                        var info = new FileInfo(path);
                        files.Add(new DiscoveredCrashLog(
                            directory.Value.Game,
                            directory.Value.BuildId,
                            SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(path)),
                            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                            info.Length));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        // A log can disappear or become unreadable while the list is being built.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // One unavailable game/profile log directory must not hide logs from other targets.
            }
        }

        return files
            .OrderByDescending(log => log.LastWriteTimeUtc)
            .ThenBy(log => log.Path, PathComparer)
            .Take(maxResults)
            .ToArray();

        void AddDirectory(string path, GameTarget target, string? buildId)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                var identity = CanonicalDirectory(path);
                directories.TryAdd(identity, (target, buildId));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Invalid, linked, or inaccessible optional directories are skipped independently.
            }
        }
    }

    private static bool IsOriginalTrilogy(GameTarget target) => target is
        GameTarget.ShadowOfChernobyl or GameTarget.ClearSky or GameTarget.CallOfPripyat;

    private static bool TryGetOriginalTarget(string releaseId, out GameTarget target)
    {
        target = releaseId switch
        {
            "stalker-soc" => GameTarget.ShadowOfChernobyl,
            "stalker-cs" => GameTarget.ClearSky,
            "stalker-cop" => GameTarget.CallOfPripyat,
            _ => default,
        };
        return releaseId is "stalker-soc" or "stalker-cs" or "stalker-cop";
    }

    private static string CanonicalDirectory(string path) =>
        SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(path));
}
