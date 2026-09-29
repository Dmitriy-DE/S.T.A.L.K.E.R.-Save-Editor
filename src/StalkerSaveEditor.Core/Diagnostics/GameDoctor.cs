using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Storage;
using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Core.Diagnostics;

/// <summary>Distinct release targets; enhanced editions do not inherit original-trilogy fix applicability.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameTarget>))]
public enum GameTarget
{
    ShadowOfChernobyl,
    ClearSky,
    CallOfPripyat,
    ShadowOfChernobylEnhancedEdition,
    ClearSkyEnhancedEdition,
    CallOfPripyatEnhancedEdition,
    Stalker2,
}

[JsonConverter(typeof(JsonStringEnumConverter<GameDoctorStatus>))]
public enum GameDoctorStatus
{
    Ok,
    Warning,
    Error,
    Unknown,
}

public sealed record GameDoctorCheck(string Id, GameDoctorStatus Status, string Summary, string Detail = "");

public sealed record GameDoctorFileAudit(string RelativePath, string Owner, GameDoctorStatus Status, string Detail);

public sealed record GameDoctorReport(
    GameTarget Target,
    string GameDirectory,
    string? SteamBuildId,
    IReadOnlyList<GameDoctorCheck> Checks,
    IReadOnlyList<string> LooseFiles,
    IReadOnlyList<GameFixInstalledInfo> InstalledFixes)
{
    public IReadOnlyList<GameDoctorFileAudit> FileAudit { get; init; } = [];
}

public sealed record GameTargetDescriptor(string Id, string Title, int? SteamAppId, bool IsXRay, bool CompanionSupported);

public sealed record GameDoctorInstallation(GameTarget Target, string Directory, GameInstallSource Source, string? BuildId);

public static class GameTargetCatalog
{
    private static readonly GameTargetDescriptor[] Descriptors =
    [
        new("soc", "Shadow of Chernobyl", 4_500, true, true),
        new("cs", "Clear Sky", 20_510, true, true),
        new("cop", "Call of Pripyat", 41_700, true, true),
        new("soc-ee", "Shadow of Chornobyl Enhanced Edition", 2_427_410, true, false),
        new("cs-ee", "Clear Sky Enhanced Edition", 2_427_420, true, false),
        new("cop-ee", "Call of Prypiat Enhanced Edition", 2_427_430, true, false),
        new("s2", "S.T.A.L.K.E.R. 2", Stalker2CompanionInstaller.SteamAppId, false, false),
    ];

    public static GameTargetDescriptor Get(GameTarget target) => Descriptors[(int)target];

    public static bool TryParse(string id, out GameTarget target)
    {
        ArgumentNullException.ThrowIfNull(id);
        for (var index = 0; index < Descriptors.Length; index++)
        {
            if (string.Equals(Descriptors[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                target = (GameTarget)index;
                return true;
            }
        }

        target = default;
        return false;
    }
}

/// <summary>
/// Read-only, explicitly targeted installation audit. It reports loose files as unclassified because
/// this application does not ship clean retail baselines for every storefront/build.
/// </summary>
public static class GameDoctor
{
    private const int MaxLooseFiles = 2_000;

    public static GameDoctorReport Analyze(GameTarget target, string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var descriptor = GameTargetCatalog.Get(target);
        var fullPath = Path.GetFullPath(gameDirectory);
        var checks = new List<GameDoctorCheck>();
        var looseFiles = new List<string>();
        if (!Directory.Exists(fullPath))
        {
            checks.Add(new GameDoctorCheck("installation", GameDoctorStatus.Error, "Installation directory not found."));
            return new GameDoctorReport(target, fullPath, null, checks.AsReadOnly(), looseFiles.AsReadOnly(), []);
        }

        var markerFound = HasExpectedMarker(target, fullPath);
        checks.Add(markerFound
            ? new GameDoctorCheck("installation", GameDoctorStatus.Ok, "Expected game data marker found.", "This is a structural check; it does not verify every retail file.")
            : new GameDoctorCheck("installation", GameDoctorStatus.Error, "Expected game data marker is missing.", descriptor.IsXRay ? "No root fsgame.ltx was found." : "No Stalker2/Content/Paks directory was found."));

        var buildId = TryReadSteamBuildId(fullPath, descriptor.SteamAppId);
        checks.Add(buildId is null
            ? new GameDoctorCheck("version", GameDoctorStatus.Unknown, "Steam build ID unavailable.", "The selected folder is not a matching Steam installation or its app manifest is unreadable.")
            : new GameDoctorCheck("version", GameDoctorStatus.Ok, "Steam build ID detected.", buildId));

        var modDirectory = descriptor.IsXRay
            ? Path.Combine(fullPath, "gamedata")
            : Path.Combine(fullPath, "Stalker2", "Content", "Paks", "~mods");
        var scanResult = ReadLooseFiles(fullPath, modDirectory, looseFiles);
        checks.Add(scanResult switch
        {
            ScanResult.NoFiles => new GameDoctorCheck("loose-files", GameDoctorStatus.Ok, "No loose mod files found."),
            ScanResult.Files => new GameDoctorCheck("loose-files", GameDoctorStatus.Warning, "Loose files need review.",
                $"{looseFiles.Count} file(s) found under {Path.GetRelativePath(fullPath, modDirectory).Replace('\\', '/')}. Their source and compatibility are unknown because no retail baseline is bundled."),
            ScanResult.Truncated => new GameDoctorCheck("loose-files", GameDoctorStatus.Warning, "Loose-file scan reached its limit.",
                $"At least {looseFiles.Count} files found; the listing is capped at {MaxLooseFiles} and was not classified."),
            _ => new GameDoctorCheck("loose-files", GameDoctorStatus.Warning, "Loose-file scan was incomplete.", "The directory could not be read completely."),
        });

        if (target == GameTarget.Stalker2)
        {
            var modState = Stalker2ModToggle.GetState(fullPath);
            checks.Add(modState switch
            {
                Stalker2ModState.Enabled => new GameDoctorCheck("s2-custom-mods", GameDoctorStatus.Warning, "Custom S2 mods are present.", "The official Update 2.0 guidance recommends starting without stale custom mods."),
                Stalker2ModState.Disabled => new GameDoctorCheck("s2-custom-mods", GameDoctorStatus.Ok, "Custom S2 mods are disabled.", "The folder is preserved outside Content/Paks and can be restored."),
                Stalker2ModState.Conflict => new GameDoctorCheck("s2-custom-mods", GameDoctorStatus.Warning, "Both active and recovery mod folders exist.", "No automatic action is available until the folders are reviewed."),
                Stalker2ModState.InstallationMissing => new GameDoctorCheck("s2-custom-mods", GameDoctorStatus.Unknown, "S2 mod state could not be checked."),
                _ => new GameDoctorCheck("s2-custom-mods", GameDoctorStatus.Ok, "No custom S2 mod folder found."),
            });
        }

        if (descriptor.CompanionSupported)
        {
            checks.Add(CheckCompanion(fullPath, target));
        }

        IReadOnlyList<GameFixInstalledInfo> installedFixes;
        try
        {
            installedFixes = new GameFixEngine().ListInstalled(fullPath);
            var modified = installedFixes.Count(fix => fix.State == GameFixState.Modified);
            var available = GameFixCatalog.ForGame(target);
            var recommended = GameFixCatalog.ForPreset(target, GameFixPreset.Recommended);
            var recommendedBuildCompatible = recommended.Count > 0 && buildId is { } currentBuild &&
                recommended.All(fix => fix.SupportedSteamBuildIds.Contains(currentBuild, StringComparer.Ordinal));
            var applicableRecommended = recommendedBuildCompatible ? recommended : [];
            var installedIds = installedFixes
                .Where(fix => fix.State == GameFixState.Installed)
                .Select(fix => fix.Id)
                .ToHashSet(StringComparer.Ordinal);
            var missingRecommended = applicableRecommended.Count(fix => !installedIds.Contains(fix.Id));
            var experimental = available.Count(fix => fix.Maturity == GameFixMaturity.Experimental);
            checks.Add(installedFixes.Count == 0
                ? available.Count == 0
                    ? new GameDoctorCheck("game-fixes", GameDoctorStatus.Unknown, "No toolkit Game Fixes are installed.",
                        "No built-in fix is catalogued for this target; this is not a clean-file verification.")
                : recommended.Count == 0
                    ? new GameDoctorCheck("game-fixes", GameDoctorStatus.Unknown, "No safe Game Fix recommendation is available.",
                        $"{available.Count} fix(es) are catalogued; {experimental} experimental fix(es) are excluded from safe presets.")
                : !recommendedBuildCompatible
                    ? new GameDoctorCheck("game-fixes", GameDoctorStatus.Unknown, "No compatible Game Fix recommendation is available.",
                        $"{recommended.Count} safe fix(es) are catalogued, but Steam build {buildId ?? "unknown"} is not supported by the complete preset.")
                    : new GameDoctorCheck("game-fixes", GameDoctorStatus.Warning, "Recommended Game Fixes are not installed.",
                        $"{missingRecommended} of {applicableRecommended.Count} safe recommendation(s) are not installed; {available.Count} fix(es) are catalogued.")
                : modified > 0
                    ? new GameDoctorCheck("game-fixes", GameDoctorStatus.Warning, "Some managed Game Fix files have changed.",
                        $"{modified} of {installedFixes.Count} installed fix(es) differ from their recorded hashes.")
                : !recommendedBuildCompatible
                    ? new GameDoctorCheck("game-fixes", GameDoctorStatus.Warning, "Installed Game Fixes are being inspected on an unsupported build.",
                        $"Steam build {buildId ?? "unknown"} is not supported by the complete safe preset; review the managed files before continuing.")
                    : missingRecommended > 0
                        ? new GameDoctorCheck("game-fixes", GameDoctorStatus.Warning, "Recommended Game Fixes are not installed.",
                        $"{missingRecommended} of {applicableRecommended.Count} safe recommendation(s) are not installed; {installedFixes.Count} fix(es) are installed.")
                : new GameDoctorCheck("game-fixes", GameDoctorStatus.Ok, "Toolkit Game Fix manifests and file hashes are valid.",
                        $"{installedFixes.Count} fix(es) installed; {available.Count} catalogued; {applicableRecommended.Count} compatible safe recommendation(s)."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            installedFixes = [];
            checks.Add(new GameDoctorCheck("game-fixes", GameDoctorStatus.Warning, "Game Fix state needs review.", exception.Message));
        }

        var report = new GameDoctorReport(target, fullPath, buildId, checks.AsReadOnly(), looseFiles.AsReadOnly(), installedFixes);
        return report with { FileAudit = AuditManagedFiles(target, fullPath, looseFiles, checks) };
    }

    public static IReadOnlyList<GameDoctorInstallation> DiscoverInstallations(IEnumerable<string>? steamRoots = null)
    {
        var includeNonSteam = steamRoots is null;
        var roots = steamRoots?.ToArray() ?? SaveDirectoryLocator.DefaultSteamRoots().ToArray();
        var libraries = SteamLibraryFolderLocator.GetLibraries(roots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new Dictionary<GameTarget, HashSet<string>>();
        var installations = new List<GameDoctorInstallation>();

        void Add(GameTarget target, string? directory, GameInstallSource source)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            string fullPath;
            try
            {
                fullPath = ResolveDirectoryIdentity(directory);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return;
            }

            if (!HasExpectedMarker(target, fullPath)) return;
            if (!seen.TryGetValue(target, out var targetPaths))
                seen.Add(target, targetPaths = new HashSet<string>(comparer));
            if (!targetPaths.Add(fullPath)) return;
            installations.Add(new GameDoctorInstallation(
                target,
                fullPath,
                source,
                TryReadSteamBuildId(fullPath, GameTargetCatalog.Get(target).SteamAppId)));
        }

        foreach (var target in Enum.GetValues<GameTarget>())
        {
            var descriptor = GameTargetCatalog.Get(target);
            if (descriptor.SteamAppId is not { } appId) continue;
            foreach (var library in libraries)
            {
                var directory = SteamLibraryFolderLocator.GetManifestInstallDirectory(library, appId);
                if (directory is not null && IsUnderSteamCommon(library, directory))
                    Add(target, directory, GameInstallSource.Steam);
            }
        }

        if (includeNonSteam)
        {
            foreach (var game in Enum.GetValues<CompanionGame>())
            {
                var target = game switch
                {
                    CompanionGame.ShadowOfChernobyl => GameTarget.ShadowOfChernobyl,
                    CompanionGame.ClearSky => GameTarget.ClearSky,
                    CompanionGame.CallOfPripyat => GameTarget.CallOfPripyat,
                    _ => throw new InvalidOperationException("Unsupported Companion game target."),
                };
                foreach (var installation in GameInstallLocator.FindNonSteam(game))
                    Add(target, installation.Directory, installation.Source);
            }
        }

        return installations
            .OrderBy(installation => installation.Target)
            .ThenBy(installation => installation.Directory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasExpectedMarker(GameTarget target, string gameDirectory)
    {
        var descriptor = GameTargetCatalog.Get(target);
        return descriptor.IsXRay
            ? File.Exists(Path.Combine(gameDirectory, "fsgame.ltx")) || File.Exists(Path.Combine(gameDirectory, "fsgame_soc.ltx"))
            : Directory.Exists(Path.Combine(gameDirectory, "Stalker2", "Content", "Paks"));
    }

    private static bool IsUnderSteamCommon(string library, string gameDirectory)
    {
        try
        {
            var commonDirectory = ResolveDirectoryIdentity(Path.Combine(library, "steamapps", "common"));
            var fullGameDirectory = ResolveDirectoryIdentity(gameDirectory);
            var relative = Path.GetRelativePath(commonDirectory, fullGameDirectory);
            return relative != "." &&
                !Path.IsPathRooted(relative) &&
                !string.Equals(relative, "..", PathComparison) &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) &&
                !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The install folder with every linked component resolved (macOS /var, Steam root links), used to spot duplicates.</summary>
    private static string ResolveDirectoryIdentity(string directory) =>
        Storage.SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(directory));

    private static GameDoctorFileAudit[] AuditManagedFiles(
        GameTarget target,
        string gameDirectory,
        IReadOnlyList<string> looseFiles,
        List<GameDoctorCheck> checks)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var managed = new Dictionary<string, List<(string Owner, bool Exists, bool Matches)>>(comparer);

        void Add(string path, string owner, bool exists, bool matches)
        {
            var normalized = path.Replace('\\', '/');
            if (!managed.TryGetValue(normalized, out var owners)) managed.Add(normalized, owners = []);
            owners.Add((owner, exists, matches));
        }

        try
        {
            foreach (var file in new GameFixEngine().GetManagedFileStatus(gameDirectory))
                Add(file.RelativePath, "Game Fix: " + file.FixId, file.Exists, file.MatchesExpectedHash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            checks.Add(new GameDoctorCheck("file-audit-game-fixes", GameDoctorStatus.Warning,
                "Game Fix file ownership could not be audited.", exception.Message));
        }

        if (GameTargetCatalog.Get(target).CompanionSupported)
        {
            var companionGame = target switch
            {
                GameTarget.ShadowOfChernobyl => CompanionGame.ShadowOfChernobyl,
                GameTarget.ClearSky => CompanionGame.ClearSky,
                GameTarget.CallOfPripyat => CompanionGame.CallOfPripyat,
                _ => throw new ArgumentOutOfRangeException(nameof(target)),
            };
            try
            {
                var installer = new CompanionInstaller(CompanionAssetLocator.ResolveSourceRoot());
                foreach (var file in installer.GetManagedFileStatus(companionGame, gameDirectory))
                    Add(file.RelativePath, "Companion", file.Exists, file.MatchesExpectedHash);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                checks.Add(new GameDoctorCheck("file-audit-companion", GameDoctorStatus.Warning,
                    "Companion file ownership could not be audited.", exception.Message));
            }
        }

        foreach (var path in looseFiles)
        {
            var normalized = path.Replace('\\', '/');
            if (!managed.ContainsKey(normalized)) Add(normalized, "Unclassified", true, false);
        }

        return managed
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(pair =>
            {
                var owners = pair.Value;
                if (owners.Count > 1)
                    return owners.Select(owner => new GameDoctorFileAudit(
                        pair.Key,
                        owner.Owner,
                        GameDoctorStatus.Warning,
                        "Multiple toolkit components claim this path; review the component manifests."));

                var single = owners[0];
                if (single.Owner == "Unclassified")
                    return [new GameDoctorFileAudit(pair.Key, single.Owner, GameDoctorStatus.Unknown,
                        "The file is present, but its source and compatibility are unknown.")];
                if (!single.Exists)
                    return [new GameDoctorFileAudit(pair.Key, single.Owner, GameDoctorStatus.Warning,
                        "The toolkit-managed file is missing.")];
                if (!single.Matches)
                    return [new GameDoctorFileAudit(pair.Key, single.Owner, GameDoctorStatus.Warning,
                        "The toolkit-managed file differs from its recorded installation hash.")];
                return [new GameDoctorFileAudit(pair.Key, single.Owner, GameDoctorStatus.Ok,
                    "The toolkit-managed file matches its recorded installation hash.")];
            })
            .ToArray();
    }

    private static GameDoctorCheck CheckCompanion(string gameDirectory, GameTarget target)
    {
        var manifestPath = Path.Combine(gameDirectory, ".save-editor-companion", "manifest.json");
        var markerPath = Path.Combine(gameDirectory, "gamedata", "scripts", "save_editor_companion.script");
        if (!File.Exists(manifestPath) && !File.Exists(markerPath))
        {
            return new GameDoctorCheck("companion", GameDoctorStatus.Ok, "Companion is not installed.");
        }

        var game = target switch
        {
            GameTarget.ShadowOfChernobyl => CompanionGame.ShadowOfChernobyl,
            GameTarget.ClearSky => CompanionGame.ClearSky,
            GameTarget.CallOfPripyat => CompanionGame.CallOfPripyat,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        try
        {
            var installer = new CompanionInstaller(CompanionAssetLocator.ResolveSourceRoot());
            var status = installer.GetStatus(game, gameDirectory);
            if (status.ModInstalled && status.Issues.Count == 0)
            {
                return new GameDoctorCheck("companion", GameDoctorStatus.Ok, "Companion manifest and managed file hashes are valid.", status.Version ?? string.Empty);
            }

            return new GameDoctorCheck("companion", GameDoctorStatus.Warning, "Companion state needs review.",
                status.Issues.Count > 0 ? string.Join("; ", status.Issues) : "Companion files are present without a verifiable install manifest.");
        }
        catch (CompanionInstallerException exception)
        {
            return new GameDoctorCheck("companion", GameDoctorStatus.Warning, "Companion state needs review.", exception.Message);
        }
    }

    private static ScanResult ReadLooseFiles(string root, string directory, List<string> destination)
    {
        if (!Directory.Exists(directory)) return ScanResult.NoFiles;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MaxRecursionDepth = 32,
            };
            foreach (var path in Directory.EnumerateFiles(directory, "*", options))
            {
                if (destination.Count == MaxLooseFiles) return ScanResult.Truncated;
                destination.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            }

            destination.Sort(StringComparer.OrdinalIgnoreCase);
            return destination.Count == 0 ? ScanResult.NoFiles : ScanResult.Files;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ScanResult.Incomplete;
        }
    }

    private static string? TryReadSteamBuildId(string gameDirectory, int? appId)
    {
        if (appId is null) return null;
        var libraryRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(gameDirectory)));
        if (libraryRoot is null) return null;
        var manifestPath = Path.Combine(libraryRoot, "steamapps", $"appmanifest_{appId.Value}.acf");
        try
        {
            var document = SteamVdfParser.Parse(File.ReadAllText(manifestPath));
            if (!SteamVdfParser.TryGetObject(document, "AppState", out var state) ||
                !SteamVdfParser.TryGetString(state, "appid", out var actualAppId) || actualAppId != appId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !SteamVdfParser.TryGetString(state, "installdir", out var installDirectory) ||
                !string.Equals(Path.GetFullPath(Path.Combine(libraryRoot, "steamapps", "common", installDirectory)), gameDirectory, PathComparison) ||
                !SteamVdfParser.TryGetString(state, "buildid", out var buildId) || string.IsNullOrWhiteSpace(buildId))
            {
                return null;
            }

            return buildId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private enum ScanResult
    {
        NoFiles,
        Files,
        Truncated,
        Incomplete,
    }
}
