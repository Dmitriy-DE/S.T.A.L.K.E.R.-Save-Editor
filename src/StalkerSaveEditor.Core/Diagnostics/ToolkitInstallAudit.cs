using System.Security.Cryptography;
using StalkerSaveEditor.Core.Patching;

namespace StalkerSaveEditor.Core.Diagnostics;

public enum ToolkitAuditClassification
{
    Vanilla,
    ToolkitManaged,
    Unknown,
    OrphanedToolkitOwned,
    OrphanedStateNeedsReview,
}

public sealed record ToolkitInstallAuditEntry(
    string RelativePath,
    ToolkitAuditClassification Classification,
    GameDoctorStatus Status,
    string Detail,
    bool CanCleanup,
    string? OwnerId = null);

public sealed record ToolkitInstallAuditReport(
    GameTarget Target,
    string GameDirectory,
    bool VanillaBaselineBundled,
    IReadOnlyList<ToolkitInstallAuditEntry> Entries,
    int SafeCleanupCandidateCount);

/// <summary>Classifies files from exact hashes and provider manifests; cleanup replays only a verified provider uninstall.</summary>
public static class ToolkitInstallAudit
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static ToolkitInstallAuditReport Analyze(GameTarget target, string gameDirectory) =>
        Analyze(target, gameDirectory, GameFixCatalog.ForGame(target));

    internal static ToolkitInstallAuditReport Analyze(
        GameTarget target,
        string gameDirectory,
        IReadOnlyList<GameFixDefinition> knownDefinitions)
    {
        ArgumentNullException.ThrowIfNull(knownDefinitions);
        var report = GameDoctor.Analyze(target, gameDirectory);
        var engine = new GameFixEngine();
        var currentCatalogIds = GameFixCatalog.All.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var fixStatus = report.InstalledFixes
            .Where(fix => fix.Game == target && !currentCatalogIds.Contains(fix.Id))
            .ToDictionary(fix => fix.Id, GetOrphanStatus, StringComparer.Ordinal);

        var retailHashes = GetKnownRetailHashes(target, report.SteamBuildId, knownDefinitions);
        var entries = report.FileAudit.Select(ClassifyFile).ToList();
        if (report.Checks.Any(check => check.Id == "installation" && check.Status == GameDoctorStatus.Ok))
        {
            AddManifestlessFiles(Path.Combine(report.GameDirectory, ".save-editor-game-fixes"), report.GameDirectory, "Game Fix state");
            AddManifestlessFiles(Path.Combine(report.GameDirectory, ".save-editor-companion"), report.GameDirectory, "Companion state");
        }

        return new ToolkitInstallAuditReport(
            target,
            report.GameDirectory,
            VanillaBaselineBundled: false,
            entries.OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Classification)
                .ToArray(),
            SafeCleanupCandidateCount: fixStatus.Values.Count(status => status.CanCleanup));

        OrphanStatus GetOrphanStatus(GameFixInstalledInfo fix)
        {
            var preflight = engine.CheckUninstall(fix.Id, target, report.GameDirectory);
            var exclusiveOwnership = fix.Files.All(relativePath =>
            {
                var owners = report.FileAudit.Where(file => PathComparer.Equals(file.RelativePath, relativePath)).ToArray();
                return owners.Length == 1 &&
                    string.Equals(owners[0].Owner, "Game Fix: " + fix.Id, StringComparison.Ordinal) &&
                    owners[0].Status == GameDoctorStatus.Ok;
            });
            var canCleanup = fix.State == GameFixState.Installed && preflight.CanUninstall && exclusiveOwnership;
            var detail = canCleanup
                ? "This active Game Fix ID is absent from the current catalogue. Its current files and recovery backups match the provider manifest; cleanup restores the recorded prior bytes."
                : !preflight.CanUninstall
                    ? preflight.Reason ?? "The provider could not verify a safe removal."
                    : !exclusiveOwnership
                        ? "Another owner, missing path or file audit conflict prevents cleanup."
                        : "The provider manifest reports external drift; cleanup is disabled until the conflict is resolved.";
            return new OrphanStatus(canCleanup, detail);
        }

        ToolkitInstallAuditEntry ClassifyFile(GameDoctorFileAudit file)
        {
            const string gameFixOwnerPrefix = "Game Fix: ";
            if (file.Owner.StartsWith(gameFixOwnerPrefix, StringComparison.Ordinal))
            {
                var ownerId = file.Owner[gameFixOwnerPrefix.Length..];
                if (fixStatus.TryGetValue(ownerId, out var orphan))
                {
                    return new ToolkitInstallAuditEntry(
                        file.RelativePath,
                        ToolkitAuditClassification.OrphanedToolkitOwned,
                        file.Status,
                        orphan.Detail,
                        orphan.CanCleanup,
                        ownerId);
                }
            }

            if (string.Equals(file.Owner, "Unclassified", StringComparison.Ordinal))
            {
                if (retailHashes.TryGetValue(file.RelativePath, out var expectedHash) &&
                    TryHashLooseFile(report.GameDirectory, file.RelativePath, out var actualHash) &&
                    string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                {
                    return new ToolkitInstallAuditEntry(
                        file.RelativePath,
                        ToolkitAuditClassification.Vanilla,
                        GameDoctorStatus.Ok,
                        "This file matches a known retail SHA-256 for the exact detected Steam build.",
                        CanCleanup: false);
                }

                return new ToolkitInstallAuditEntry(file.RelativePath, ToolkitAuditClassification.Unknown,
                    file.Status, file.Detail, CanCleanup: false);
            }

            return new ToolkitInstallAuditEntry(
                file.RelativePath,
                ToolkitAuditClassification.ToolkitManaged,
                file.Status,
                file.Detail,
                CanCleanup: false);
        }

        void AddManifestlessFiles(string providerRoot, string root, string provider)
        {
            if (!Directory.Exists(providerRoot) || IsReparsePoint(providerRoot)) return;
            IEnumerable<string> directories;
            try
            {
                directories = provider == "Game Fix state"
                    ? Directory.EnumerateDirectories(providerRoot, "*", SearchOption.TopDirectoryOnly).ToArray()
                    : [providerRoot];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var directory in directories)
            {
                if (IsReparsePoint(directory)) continue;
                var manifestPath = Path.Combine(directory, "manifest.json");
                if (File.Exists(manifestPath)) continue;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint,
                            MaxRecursionDepth = 32,
                        })
                        .Where(path => !IsReparsePoint(path))
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var path in files)
                {
                    entries.Add(new ToolkitInstallAuditEntry(
                        Path.GetRelativePath(root, path).Replace('\\', '/'),
                        ToolkitAuditClassification.OrphanedStateNeedsReview,
                        GameDoctorStatus.Warning,
                        $"File under {provider} has no verifiable provider manifest. Ownership is unknown; retained without cleanup.",
                        CanCleanup: false));
                }
            }
        }
    }

    public static GameFixInstallResult Cleanup(GameTarget target, string gameDirectory, string fixId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixId);
        if (GameFixCatalog.All.Any(definition => string.Equals(definition.Id, fixId, StringComparison.Ordinal)))
            throw new InvalidOperationException("A current catalogue fix is not an orphan and cannot be cleaned up by this action.");

        var report = Analyze(target, gameDirectory);
        if (!report.Entries.Any(entry => entry.CanCleanup && string.Equals(entry.OwnerId, fixId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The orphan does not pass the provider's ownership, file and backup checks.");

        var engine = new GameFixEngine();
        var preflight = engine.CheckUninstall(fixId, target, report.GameDirectory);
        if (!preflight.CanUninstall)
            throw new InvalidOperationException(preflight.Reason ?? "The provider could not verify a safe removal.");
        return engine.Uninstall(fixId, target, report.GameDirectory);
    }

    private static Dictionary<string, string> GetKnownRetailHashes(
        GameTarget target,
        string? buildId,
        IReadOnlyList<GameFixDefinition> definitions)
    {
        if (buildId is null) return new Dictionary<string, string>(PathComparer);
        return definitions
            .Where(definition => definition.Game == target && definition.SupportedSteamBuildIds.Contains(buildId, StringComparer.Ordinal))
            .SelectMany(definition => definition.TextPatches.Select(operation => (operation.RelativePath, Sha256: operation.ExpectedFileSha256))
                .Concat(definition.SpawnEdits.Select(edit => (edit.RelativePath, Sha256: edit.ExpectedFileSha256))))
            .Where(operation => IsSha256(operation.Sha256))
            .GroupBy(operation => NormalizeRelativePath(operation.RelativePath), PathComparer)
            .Select(group => new
            {
                Path = group.Key,
                Hashes = group.Select(operation => operation.Sha256!.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray(),
            })
            .Where(group => group.Hashes.Length == 1)
            .ToDictionary(group => group.Path, group => group.Hashes[0], PathComparer);
    }

    private static bool TryHashLooseFile(string gameDirectory, string relativePath, out string sha256)
    {
        sha256 = string.Empty;
        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(gameDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var fromRoot = Path.GetRelativePath(gameDirectory, fullPath);
        if (Path.IsPathRooted(fromRoot) || fromRoot == ".." || fromRoot.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) ||
            fromRoot.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison) || !File.Exists(fullPath) || IsReparsePoint(fullPath))
            return false;

        try
        {
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsReparsePoint(string path) =>
        (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record OrphanStatus(bool CanCleanup, string Detail);
}
