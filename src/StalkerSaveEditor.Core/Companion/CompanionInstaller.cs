using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Patching;

namespace StalkerSaveEditor.Core.Companion;

public sealed partial class CompanionInstaller
{
    private const int ManifestSchemaVersion = 1;
    private const string ManifestDirectoryName = ".save-editor-companion";
    private const string JournalFileName = "transaction.json";
    private const string JournalDirectoryName = "transaction";
    private const int JournalSchemaVersion = 1;
    private const string ManifestFileName = "manifest.json";
    private const string ModVersion = "v1";
    private enum HookFileKind
    {
        BindStalker,
        MainMenu,
        QuestItems,
    }

    private sealed record HookFileTarget(
        HookFileKind Kind,
        string GameRelativePath,
        string ArchiveRelativePath);
    private readonly string _modSourceRoot;
    private readonly IGameFileSystem _fileSystem;

    public CompanionInstaller(string modSourceRoot)
        : this(modSourceRoot, new PhysicalGameFileSystem())
    {
    }

    internal CompanionInstaller(string modSourceRoot, IGameFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modSourceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _modSourceRoot = Path.GetFullPath(modSourceRoot);
        _fileSystem = fileSystem;
    }

    /// <summary><c>MOD_BUILD</c> of the mod this editor installs, read from the bundled script; null when absent.</summary>
    public string? BundledModBuild
    {
        get
        {
            var script = Path.Combine(_modSourceRoot, "gamedata", "scripts", "save_editor_companion.script");
            try
            {
                if (!_fileSystem.FileExists(script)) return null;
                var match = ModBuildPattern().Match(_fileSystem.ReadAllText(script));
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary><c>MOD_BUILD</c> of the script installed in the game (the build the game runs after a restart).</summary>
    private string? InstalledModBuild(string gameDirectory, InstallManifest manifest)
    {
        var script = manifest.Files.FirstOrDefault(file => file.Path.Replace('\\', '/').EndsWith("scripts/save_editor_companion.script", StringComparison.OrdinalIgnoreCase));
        if (script is null) return null;
        try
        {
            var path = ResolveStatePath(gameDirectory, script.Path);
            if (!_fileSystem.FileExists(path)) return null;
            var match = ModBuildPattern().Match(_fileSystem.ReadAllText(path));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("local\\s+MOD_BUILD\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ModBuildPattern();

    public CompanionInstallStatus GetStatus(
        CompanionGame game,
        string? selectedGameDirectory = null,
        IReadOnlyList<string>? steamRoots = null)
    {
        var definition = CompanionGameDefinition.For(game);
        var (gameDirectory, locationIssue) = ResolveGameDirectory(definition, selectedGameDirectory, steamRoots);
        if (gameDirectory is null)
        {
            return new CompanionInstallStatus(game, false, null, false, null,
                locationIssue is null ? Array.Empty<string>() : [locationIssue]);
        }

        var issues = new List<string>();
        if (!_fileSystem.DirectoryExists(_modSourceRoot))
        {
            issues.Add($"Companion source directory is missing: {_modSourceRoot}");
        }

        var manifestPath = GetManifestPath(gameDirectory);
        if (_fileSystem.FileExists(manifestPath))
        {
            try
            {
                var manifest = ReadManifest(manifestPath, definition);
                var changed = manifest.Files
                    .Where(entry => !FileMatches(gameDirectory, entry.Path, entry.AfterSha256))
                    .Select(entry => entry.Path)
                    .ToArray();
                issues.AddRange(changed.Select(path => $"Installed file changed or missing: {path}"));
                return new CompanionInstallStatus(game, true, gameDirectory, true, InstalledModBuild(gameDirectory, manifest) ?? manifest.Version, issues.AsReadOnly());
            }
            catch (CompanionInstallerException exception)
            {
                issues.Add(exception.Message);
                return new CompanionInstallStatus(game, true, gameDirectory, false, null, issues.AsReadOnly());
            }
        }

        try
        {
            if (_fileSystem.DirectoryExists(_modSourceRoot) &&
                TryCreateManualManifest(gameDirectory, definition, out _, out _))
            {
                issues.Add("Recognized a manual companion installation; it can be adopted safely.");
                return new CompanionInstallStatus(game, true, gameDirectory, true, ModVersion, issues.AsReadOnly());
            }
        }
        catch (CompanionInstallerException exception)
        {
            issues.Add(exception.Message);
        }

        var marker = Path.Combine(gameDirectory, "gamedata", "scripts", "save_editor_companion.script");
        if (_fileSystem.FileExists(marker))
        {
            issues.Add("Companion files are present without a verifiable install manifest.");
        }

        issues.AddRange(GetArchiveStatusIssues(gameDirectory, definition));

        return new CompanionInstallStatus(
            game,
            true,
            gameDirectory,
            false,
            null,
            issues.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Returns only files owned by a valid toolkit manifest, with their current hash state.</summary>
    public IReadOnlyList<CompanionManagedFileStatus> GetManagedFileStatus(
        CompanionGame game,
        string? selectedGameDirectory = null,
        IReadOnlyList<string>? steamRoots = null)
    {
        var definition = CompanionGameDefinition.For(game);
        var gameDirectory = ResolveRequiredGameDirectory(definition, selectedGameDirectory, steamRoots);
        var manifestPath = GetManifestPath(gameDirectory);
        if (!_fileSystem.FileExists(manifestPath)) return [];

        var manifest = ReadManifest(manifestPath, definition);
        return manifest.Files
            .Select(entry =>
            {
                var path = ResolveGamePath(gameDirectory, entry.Path);
                var exists = _fileSystem.FileExists(path);
                return new CompanionManagedFileStatus(entry.Path, exists,
                    exists && FileMatches(gameDirectory, entry.Path, entry.AfterSha256));
            })
            .ToArray();
    }

    /// <summary>
    /// Reinstates the exact manifest and backup set captured by a toolkit snapshot after the
    /// regular installer has safely replayed the current Companion payload.
    /// </summary>
    internal void RestoreManagedSnapshot(
        CompanionGame game,
        string selectedGameDirectory,
        IReadOnlyDictionary<string, byte[]> stateFiles)
    {
        ArgumentNullException.ThrowIfNull(stateFiles);
        var definition = CompanionGameDefinition.For(game);
        var gameDirectory = ResolveRequiredGameDirectory(definition, selectedGameDirectory, steamRoots: []);
        var statePrefix = ManifestDirectoryName + "/";
        var normalizedFiles = stateFiles.ToDictionary(pair => NormalizeRelative(pair.Key), pair => pair.Value, StringComparer.Ordinal);
        if (!normalizedFiles.TryGetValue(statePrefix + ManifestFileName, out var manifestBytes))
            throw new InvalidDataException("Companion snapshot has no install manifest.");

        var currentManifestPath = GetManifestPath(gameDirectory);
        if (!_fileSystem.FileExists(currentManifestPath))
            throw new CompanionInstallerException("Companion must be installed by its provider before restoring its saved ownership data.", currentManifestPath);
        _ = ReadManifest(currentManifestPath, definition);

        var manifest = JsonSerializer.Deserialize(manifestBytes, CompanionManifestJsonContext.Default.InstallManifest)
            ?? throw new InvalidDataException("Saved Companion manifest is empty.");
        ValidateSnapshotManifest(manifest, definition);
        var expectedBackups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            if (!FileMatches(gameDirectory, entry.Path, entry.AfterSha256))
                throw new InvalidOperationException($"Installed Companion payload does not match saved manifest path {entry.Path}.");
            if (entry.BackupPath is null) continue;
            var relativeBackup = statePrefix + NormalizeRelative(entry.BackupPath);
            if (!normalizedFiles.TryGetValue(relativeBackup, out var backupBytes) || Sha256(backupBytes) != entry.BeforeSha256)
                throw new InvalidDataException($"Saved Companion backup is missing or corrupt: {entry.BackupPath}");
            expectedBackups.Add(relativeBackup);
        }

        var actualBackups = normalizedFiles.Keys.Where(path => path.StartsWith(statePrefix + "backups/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        if (!actualBackups.SetEquals(expectedBackups) || normalizedFiles.Keys.Any(path => path != statePrefix + ManifestFileName && !actualBackups.Contains(path)))
            throw new InvalidDataException("Companion snapshot contains unreferenced or unsupported provider state.");

        foreach (var relative in expectedBackups.Order(StringComparer.Ordinal))
        {
            var backupRelative = relative[statePrefix.Length..];
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, backupRelative));
            var backupPath = ResolveStatePath(gameDirectory, backupRelative);
            _fileSystem.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            AtomicWrite(backupPath, normalizedFiles[relative], overwrite: true);
        }

        EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, ManifestFileName));
        AtomicWrite(currentManifestPath, manifestBytes, overwrite: true);
    }

    private static void ValidateSnapshotManifest(InstallManifest manifest, CompanionGameDefinition definition)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion || !string.Equals(manifest.Game, definition.Id, StringComparison.Ordinal) ||
            manifest.Files is null || manifest.Files.Count == 0 || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("Saved Companion manifest schema, game, version or file list is invalid.");
        foreach (var entry in manifest.Files)
        {
            _ = ResolveGameRelative(entry.Path);
            if (!IsSha256(entry.AfterSha256) ||
                entry.BeforeSha256 is not null && !IsSha256(entry.BeforeSha256) ||
                entry.BeforeSha256 is null && entry.BackupPath is not null ||
                entry.BeforeSha256 is not null && string.IsNullOrWhiteSpace(entry.BackupPath) ||
                entry.WasPresentBeforeInstall && entry.BeforeSha256 is null)
                throw new InvalidDataException($"Saved Companion manifest entry is invalid: {entry.Path}");
            if (entry.BackupPath is not null) _ = ResolveStateRelative(entry.BackupPath);
        }
        if (manifest.Files.Select(file => NormalizeRelative(file.Path)).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count)
            throw new InvalidDataException("Saved Companion manifest contains duplicate file paths.");
    }

    public CompanionInstallerResult Install(
        CompanionGame game,
        string? selectedGameDirectory = null,
        IReadOnlyList<string>? steamRoots = null)
    {
        var definition = CompanionGameDefinition.For(game);
        var gameDirectory = ResolveRequiredGameDirectory(definition, selectedGameDirectory, steamRoots);
        var gameDataDirectory = Path.Combine(gameDirectory, "gamedata");
        var manifestPath = GetManifestPath(gameDirectory);
        var stateDirectory = Path.GetDirectoryName(manifestPath)!;
        FinishInterruptedInstall(gameDirectory);
        var priorStateExists = _fileSystem.DirectoryExists(stateDirectory);
        var priorManifest = _fileSystem.FileExists(manifestPath)
            ? ReadManifest(manifestPath, definition)
            : null;
        if (priorManifest is null && TryCreateManualManifest(gameDirectory, definition, out var manualManifest, out var manualBackups))
        {
            PersistManifest(gameDirectory, manualManifest, manualBackups, overwrite: false);
            priorManifest = manualManifest;
            priorStateExists = true;
        }
        else if (priorManifest is null && priorStateExists)
        {
            throw new CompanionInstallerException(
                $"Cannot install companion: state directory already exists without a valid manifest: {stateDirectory}",
                stateDirectory);
        }

        var payloads = ReadModPayloads(definition, gameDirectory);
        var oldFiles = priorManifest?.Files.ToDictionary(file => NormalizeRelative(file.Path), StringComparer.Ordinal)
            ?? new Dictionary<string, InstallFileManifest>(StringComparer.Ordinal);
        VerifyManagedFiles(gameDirectory, priorManifest);
        var plan = BuildInstallPlan(gameDirectory, definition, payloads, oldFiles);
        var managedByFixes = GameFixEngine.GetActiveManagedPaths(gameDirectory);
        var overlap = plan.FirstOrDefault(file => managedByFixes.Contains(NormalizeRelative(file.RelativePath)));
        if (overlap is not null)
        {
            throw new CompanionInstallerException(
                $"Cannot install Companion over an active Game Fix-managed file: {overlap.RelativePath}",
                overlap.RelativePath);
        }
        var plannedPaths = plan.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        var staleFiles = oldFiles.Values
            .Where(file => !plannedPaths.Contains(NormalizeRelative(file.Path)))
            .ToArray();
        var noChanges = priorManifest is not null && staleFiles.Length == 0 &&
            plan.Count == oldFiles.Count && plan.All(file =>
                oldFiles.TryGetValue(file.RelativePath, out var old) &&
                string.Equals(old.AfterSha256, Sha256(file.Bytes), StringComparison.Ordinal));
        if (noChanges)
        {
            return new CompanionInstallerResult(true, false, priorManifest!.Version, Array.Empty<string>());
        }

        var gameDataWasPresent = priorManifest?.GameDataCreatedFromScratch == false ||
            priorManifest is null && _fileSystem.DirectoryExists(gameDataDirectory);
        var gameDataCreated = priorManifest?.GameDataCreatedFromScratch ?? !gameDataWasPresent;
        var manifestFiles = CreateFileManifest(plan, oldFiles);
        var nextManifest = new InstallManifest(
            ManifestSchemaVersion,
            definition.Id,
            ModVersion,
            gameDataCreated,
            manifestFiles);
        ApplyInstallPlan(gameDirectory, nextManifest, plan, staleFiles, priorManifest, priorStateExists);
        return new CompanionInstallerResult(true, true, ModVersion, Array.Empty<string>());
    }

    public CompanionInstallerResult Uninstall(
        CompanionGame game,
        string? selectedGameDirectory = null,
        IReadOnlyList<string>? steamRoots = null)
    {
        var definition = CompanionGameDefinition.For(game);
        var gameDirectory = ResolveRequiredGameDirectory(definition, selectedGameDirectory, steamRoots);
        var manifestPath = GetManifestPath(gameDirectory);
        FinishInterruptedInstall(gameDirectory);
        if (!_fileSystem.FileExists(manifestPath))
        {
            if (!TryCreateManualManifest(gameDirectory, definition, out var manualManifest, out var manualBackups))
            {
                return new CompanionInstallerResult(true, false, ModVersion, Array.Empty<string>());
            }

            PersistManifest(gameDirectory, manualManifest, manualBackups, overwrite: false);
        }

        var manifest = ReadManifest(manifestPath, definition);
        EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, ManifestFileName));
        var conflicts = PreflightUninstall(gameDirectory, manifest);
        if (conflicts.Count > 0)
        {
            return new CompanionInstallerResult(false, false, manifest.Version, conflicts.AsReadOnly());
        }

        foreach (var entry in manifest.Files)
        {
            EnsureSafeWritePath(gameDirectory, entry.Path);
            var target = ResolveGamePath(gameDirectory, entry.Path);
            if (IsAlreadyUninstalled(target, entry))
            {
                continue;
            }

            if (entry.BeforeSha256 is null || !entry.WasPresentBeforeInstall)
            {
                _fileSystem.DeleteFile(target);
            }
            else
            {
                var backup = ResolveStatePath(gameDirectory, entry.BackupPath!);
                EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, entry.BackupPath!));
                AtomicWrite(target, _fileSystem.ReadAllBytes(backup), overwrite: true);
            }
        }

        var messages = new List<string>();
        var gameDataDirectory = Path.Combine(gameDirectory, "gamedata");
        if (manifest.GameDataCreatedFromScratch && _fileSystem.DirectoryExists(gameDataDirectory))
        {
            if (!_fileSystem.EnumerateFiles(gameDataDirectory, "*", SearchOption.AllDirectories).Any())
            {
                DeleteEmptyDirectoryTree(gameDataDirectory);
            }
            else
            {
                var leftovers = _fileSystem.EnumerateFiles(gameDataDirectory, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(gameDirectory, path))
                    .OrderBy(path => path, StringComparer.Ordinal);
                messages.Add($"Unowned files remain under gamedata; the directory was left in place: {string.Join(", ", leftovers)}.");
            }
        }

        RemoveManifestAndBackups(gameDirectory, manifest);
        return new CompanionInstallerResult(true, true, manifest.Version, messages.AsReadOnly());
    }

    private ReadOnlyCollection<PlannedInstallFile> BuildInstallPlan(
        string gameDirectory,
        CompanionGameDefinition definition,
        IReadOnlyDictionary<string, byte[]> payloads,
        IReadOnlyDictionary<string, InstallFileManifest> oldFiles)
    {
        var archiveSearch = DiscoverArchives(gameDirectory, definition);
        var hookTargets = GetHookTargets(gameDirectory, archiveSearch);
        var configRelativePath = GetConfigRelativePath(archiveSearch);
        var plan = new Dictionary<string, PlannedInstallFile>(StringComparer.Ordinal);
        foreach (var hook in hookTargets)
        {
            var normalized = NormalizeRelative(hook.GameRelativePath);
            var existing = oldFiles.TryGetValue(normalized, out var oldEntry) ? oldEntry : null;
            var target = ResolveGamePath(gameDirectory, normalized);
            var targetExisted = _fileSystem.FileExists(target);
            byte[] original;
            bool wasPresentBeforeInstall;
            if (existing is not null && existing.BeforeSha256 is not null)
            {
                var backup = ResolveStatePath(gameDirectory, existing.BackupPath!);
                original = _fileSystem.ReadAllBytes(backup);
                wasPresentBeforeInstall = existing.WasPresentBeforeInstall;
            }
            else if (existing is not null)
            {
                throw new CompanionInstallerException($"Managed hook file has no original backup: {normalized}", normalized);
            }
            else
            {
                original = targetExisted
                    ? _fileSystem.ReadAllBytes(target)
                    : ReadFromArchives(gameDirectory, hook.ArchiveRelativePath, definition, archiveSearch);
                wasPresentBeforeInstall = targetExisted;
            }

            var patched = hook.Kind switch
            {
                HookFileKind.BindStalker => CompanionHookPatcher.PatchBindStalker(original, definition.Game),
                HookFileKind.MainMenu => CompanionHookPatcher.PatchMainMenu(original),
                HookFileKind.QuestItems => CompanionHookPatcher.PatchQuestItems(original, hook.ArchiveRelativePath),
                _ => throw new InvalidOperationException($"Unknown companion hook target: {hook.GameRelativePath}"),
            };
            plan.Add(normalized, new PlannedInstallFile(normalized, patched, original, "modified", wasPresentBeforeInstall));
        }

        foreach (var (relative, bytes) in payloads)
        {
            var mappedRelative = MapPayloadRelativePath(relative, configRelativePath);
            var normalized = NormalizeRelative($"gamedata/{mappedRelative}");
            if (plan.ContainsKey(normalized))
            {
                throw new CompanionInstallerException($"Mod asset conflicts with hook target: {normalized}", normalized);
            }

            var target = ResolveGamePath(gameDirectory, normalized);
            var existing = oldFiles.TryGetValue(normalized, out var oldEntry) ? oldEntry : null;
            if (existing is null && _fileSystem.FileExists(target))
            {
                throw new CompanionInstallerException(
                    $"Refusing to overwrite an unowned game file: {normalized}",
                    normalized);
            }

            if (existing is not null && existing.BeforeSha256 is not null)
            {
                throw new CompanionInstallerException($"Mod asset collides with a backed-up game file: {normalized}", normalized);
            }

            plan.Add(normalized, new PlannedInstallFile(normalized, bytes, null, "copy", existing?.WasPresentBeforeInstall ?? false));
        }

        return Array.AsReadOnly(plan.Values.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray());
    }

    private static List<InstallFileManifest> CreateFileManifest(
        IReadOnlyList<PlannedInstallFile> plan,
        IReadOnlyDictionary<string, InstallFileManifest> oldFiles)
    {
        var entries = new List<InstallFileManifest>(plan.Count);
        foreach (var file in plan)
        {
            var before = file.BeforeBytes;
            string? beforeSha = before is null ? null : Sha256(before);
            string? backupPath = null;
            var wasPresent = file.WasPresentBeforeInstall;
            if (oldFiles.TryGetValue(file.RelativePath, out var oldEntry) && oldEntry.BeforeSha256 is not null)
            {
                beforeSha = oldEntry.BeforeSha256;
                backupPath = oldEntry.BackupPath;
                wasPresent = oldEntry.WasPresentBeforeInstall;
            }
            else if (before is not null)
            {
                backupPath = $"backups/{file.RelativePath}.original";
            }

            entries.Add(new InstallFileManifest(
                file.RelativePath,
                file.Kind,
                beforeSha,
                Sha256(file.Bytes),
                backupPath,
                wasPresent));
        }

        return entries;
    }

    private void ApplyInstallPlan(
        string gameDirectory,
        InstallManifest manifest,
        IReadOnlyList<PlannedInstallFile> plan,
        IReadOnlyList<InstallFileManifest> staleFiles,
        InstallManifest? oldManifest,
        bool stateDirectoryExisted)
    {
        var manifestPath = GetManifestPath(gameDirectory);
        var applied = new List<(string Path, byte[]? Before)>();
        var staleOriginals = new List<(string Path, byte[] Bytes)>();
        try
        {
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, "manifest.json"));
            _fileSystem.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, CompanionManifestJsonContext.Default.InstallManifest);
            BeginJournal(gameDirectory, manifest, manifestBytes, plan, staleFiles, stateDirectoryExisted);
            foreach (var file in plan)
            {
                if (file.BeforeBytes is null && oldManifest?.Files.Any(old =>
                    string.Equals(NormalizeRelative(old.Path), file.RelativePath, StringComparison.Ordinal)) != true)
                {
                    var current = ResolveGamePath(gameDirectory, file.RelativePath);
                    if (_fileSystem.FileExists(current))
                    {
                        throw new CompanionInstallerException($"Refusing to overwrite an unowned game file: {file.RelativePath}", file.RelativePath);
                    }
                }

                var entry = manifest.Files.Single(candidate => candidate.Path == file.RelativePath);
                if (entry.BackupPath is not null && !FileExistsInState(gameDirectory, entry.BackupPath))
                {
                    if (file.BeforeBytes is null || entry.BeforeSha256 is null ||
                        !string.Equals(Sha256(file.BeforeBytes), entry.BeforeSha256, StringComparison.Ordinal))
                    {
                        throw new CompanionInstallerException($"Cannot create a valid backup for {file.RelativePath}.", file.RelativePath);
                    }

                    var backupPath = ResolveStatePath(gameDirectory, entry.BackupPath);
                    EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, entry.BackupPath));
                    AtomicWrite(backupPath, file.BeforeBytes, overwrite: false);
                }
            }

            foreach (var file in plan)
            {
                var target = ResolveGamePath(gameDirectory, file.RelativePath);
                var existing = _fileSystem.FileExists(target) ? _fileSystem.ReadAllBytes(target) : null;
                if (existing is not null && existing.AsSpan().SequenceEqual(file.Bytes))
                {
                    continue;
                }

                EnsureSafeWritePath(gameDirectory, file.RelativePath);
                AtomicWrite(target, file.Bytes, overwrite: existing is not null);
                applied.Add((target, existing ?? file.BeforeBytes));
            }

            foreach (var stale in staleFiles)
            {
                var target = ResolveGamePath(gameDirectory, stale.Path);
                var current = _fileSystem.ReadAllBytes(target);
                staleOriginals.Add((target, current));
                EnsureSafeWritePath(gameDirectory, stale.Path);
                _fileSystem.DeleteFile(target);
            }

            AtomicWrite(manifestPath, manifestBytes, overwrite: _fileSystem.FileExists(manifestPath));
            EndJournal(gameDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            var rollbackErrors = RollbackInstall(applied, staleOriginals);
            if (rollbackErrors.Count == 0)
            {
                // Rolled back in-process; when it could not be, the journal stays and the next operation retries.
                try
                {
                    EndJournal(gameDirectory);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    rollbackErrors = new List<string> { cleanup.Message }.AsReadOnly();
                }
            }
            if (!stateDirectoryExisted && rollbackErrors.Count == 0)
            {
                DeleteEmptyDirectoryTree(Path.GetDirectoryName(manifestPath)!);
            }

            var detail = rollbackErrors.Count == 0
                ? exception.Message
                : $"{exception.Message} Rollback also failed: {string.Join("; ", rollbackErrors)}";
            throw new CompanionInstallerException($"Companion installation failed: {detail}", null, exception);
        }
    }

    /// <summary>
    /// A file already back in its pre-install state (removed, or restored to the original bytes) is done: this lets an
    /// uninstall that stopped half-way (a locked file, an I/O error) be run again instead of reporting it as a conflict.
    /// </summary>
    private bool IsAlreadyUninstalled(string target, InstallFileManifest entry)
    {
        if (!_fileSystem.FileExists(target)) return !entry.WasPresentBeforeInstall || entry.BeforeSha256 is null;
        return entry.WasPresentBeforeInstall && entry.BeforeSha256 is not null &&
            !string.Equals(entry.BeforeSha256, entry.AfterSha256, StringComparison.Ordinal) &&
            string.Equals(Sha256(_fileSystem.ReadAllBytes(target)), entry.BeforeSha256, StringComparison.Ordinal);
    }

    private List<string> PreflightUninstall(string gameDirectory, InstallManifest manifest)
    {
        var conflicts = new List<string>();
        foreach (var entry in manifest.Files)
        {
            EnsureSafeWritePath(gameDirectory, entry.Path);
            var target = ResolveGamePath(gameDirectory, entry.Path);
            if (IsAlreadyUninstalled(target, entry))
            {
                continue;
            }

            if (!_fileSystem.FileExists(target))
            {
                conflicts.Add($"Missing since install; left untouched: {entry.Path}");
                continue;
            }

            if (!string.Equals(Sha256(_fileSystem.ReadAllBytes(target)), entry.AfterSha256, StringComparison.Ordinal))
            {
                conflicts.Add($"Changed since install; left untouched: {entry.Path}");
                continue;
            }

            if (entry.BeforeSha256 is null)
            {
                continue;
            }

            if (entry.BackupPath is null)
            {
                conflicts.Add($"Backup reference is missing; left untouched: {entry.Path}");
                continue;
            }

            var backup = ResolveStatePath(gameDirectory, entry.BackupPath);
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, entry.BackupPath));
            if (!_fileSystem.FileExists(backup) ||
                !string.Equals(Sha256(_fileSystem.ReadAllBytes(backup)), entry.BeforeSha256, StringComparison.Ordinal))
            {
                conflicts.Add($"Backup is missing or corrupt; left untouched: {entry.Path}");
            }
        }

        return conflicts;
    }

    private void VerifyManagedFiles(string gameDirectory, InstallManifest? manifest)
    {
        if (manifest is null)
        {
            return;
        }

        foreach (var entry in manifest.Files)
        {
            EnsureSafeWritePath(gameDirectory, entry.Path);
            if (!FileMatches(gameDirectory, entry.Path, entry.AfterSha256))
            {
                throw new CompanionInstallerException(
                    $"Refusing to replace a companion file changed since installation: {entry.Path}",
                    entry.Path);
            }

            if (entry.BeforeSha256 is null)
            {
                continue;
            }

            if (entry.BackupPath is null)
            {
                throw new CompanionInstallerException($"Original-file backup reference is missing: {entry.Path}", entry.Path);
            }

            var backup = ResolveStatePath(gameDirectory, entry.BackupPath);
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, entry.BackupPath));
            if (!_fileSystem.FileExists(backup) || !string.Equals(
                Sha256(_fileSystem.ReadAllBytes(backup)), entry.BeforeSha256, StringComparison.Ordinal))
            {
                throw new CompanionInstallerException($"Original-file backup is missing or corrupt: {entry.Path}", entry.Path);
            }
        }
    }

    private bool TryBuildManualManifest(
        string gameDirectory,
        CompanionGameDefinition definition,
        out InstallManifest manifest,
        out IReadOnlyDictionary<string, byte[]> backups)
    {
        manifest = null!;
        backups = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var gameDataDirectory = Path.Combine(gameDirectory, "gamedata");
        if (!_fileSystem.DirectoryExists(gameDataDirectory))
        {
            return false;
        }

        IReadOnlyDictionary<string, byte[]> payloads;
        CompanionArchiveSearchResult archiveSearch;
        IReadOnlyList<HookFileTarget> hookTargets;
        string configRelativePath;
        try
        {
            archiveSearch = DiscoverArchives(gameDirectory, definition);
            hookTargets = GetHookTargets(gameDirectory, archiveSearch);
            configRelativePath = GetConfigRelativePath(archiveSearch);
            payloads = ReadModPayloads(definition, gameDirectory);
        }
        catch (CompanionInstallerException)
        {
            return false;
        }

        var backupBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var files = new List<InstallFileManifest>();
        foreach (var (relative, expectedBytes) in payloads)
        {
            var mappedRelative = MapPayloadRelativePath(relative, configRelativePath);
            var normalized = NormalizeRelative($"gamedata/{mappedRelative}");
            var path = ResolveGamePath(gameDirectory, normalized);
            if (!_fileSystem.FileExists(path))
            {
                return false;
            }

            var current = _fileSystem.ReadAllBytes(path);
            if (!current.AsSpan().SequenceEqual(expectedBytes))
            {
                return false;
            }

            files.Add(new InstallFileManifest(normalized, "copy", null, Sha256(current), null, false));
        }

        foreach (var hook in hookTargets)
        {
            var normalized = NormalizeRelative(hook.GameRelativePath);
            var path = ResolveGamePath(gameDirectory, normalized);
            if (!_fileSystem.FileExists(path))
            {
                return false;
            }

            var current = _fileSystem.ReadAllBytes(path);
            byte[] original;
            try
            {
                original = hook.Kind switch
                {
                    HookFileKind.BindStalker => CompanionHookPatcher.RemoveBindStalkerHooks(current, definition.Game),
                    HookFileKind.MainMenu => CompanionHookPatcher.RemoveMainMenuHook(current),
                    HookFileKind.QuestItems => CompanionHookPatcher.RemoveQuestInclude(current, hook.ArchiveRelativePath),
                    _ => throw new InvalidOperationException($"Unknown companion hook target: {hook.GameRelativePath}"),
                };
            }
            catch (CompanionInstallerException)
            {
                return false;
            }

            var archived = ReadFromArchives(gameDirectory, hook.ArchiveRelativePath, definition, archiveSearch);
            if (!original.AsSpan().SequenceEqual(archived))
            {
                return false;
            }

            var backupPath = $"backups/{normalized}.original";
            backupBytes[backupPath] = original;
            files.Add(new InstallFileManifest(normalized, "modified", Sha256(original), Sha256(current), backupPath, true));
        }

        var expectedPaths = files.Select(file => ResolveGamePath(gameDirectory, file.Path)).ToHashSet(PathComparer);
        var actualPaths = _fileSystem.EnumerateFiles(gameDataDirectory, "*", SearchOption.AllDirectories).ToHashSet(PathComparer);
        var createdFromScratch = expectedPaths.SetEquals(actualPaths);
        if (createdFromScratch)
        {
            files = files.Select(file => file.Kind == "modified"
                ? file with { WasPresentBeforeInstall = false }
                : file).ToList();
        }
        manifest = new InstallManifest(
            ManifestSchemaVersion,
            definition.Id,
            ModVersion,
            createdFromScratch,
            files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList());
        backups = backupBytes;
        return true;
    }

    private void PersistManifest(
        string gameDirectory,
        InstallManifest manifest,
        IReadOnlyDictionary<string, byte[]> backups,
        bool overwrite)
    {
        var manifestPath = GetManifestPath(gameDirectory);
        try
        {
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, "manifest.json"));
            _fileSystem.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            foreach (var (relative, bytes) in backups)
            {
                EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, relative));
                AtomicWrite(ResolveStatePath(gameDirectory, relative), bytes, overwrite: false);
            }

            AtomicWrite(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, CompanionManifestJsonContext.Default.InstallManifest), overwrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new CompanionInstallerException($"Could not save companion install manifest and backups: {exception.Message}", null, exception);
        }
    }

    private bool TryCreateManualManifest(
        string gameDirectory,
        CompanionGameDefinition definition,
        out InstallManifest manifest,
        out IReadOnlyDictionary<string, byte[]> backups)
    {
        try
        {
            return TryBuildManualManifest(gameDirectory, definition, out manifest, out backups);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or XRayFormatException)
        {
            manifest = null!;
            backups = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            return false;
        }
    }

    private InstallManifest ReadManifest(string manifestPath, CompanionGameDefinition definition)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize(_fileSystem.ReadAllBytes(manifestPath), CompanionManifestJsonContext.Default.InstallManifest)
                ?? throw new JsonException("Manifest is empty.");
            if (manifest.SchemaVersion != ManifestSchemaVersion ||
                !string.Equals(manifest.Game, definition.Id, StringComparison.Ordinal) ||
                manifest.Files is null || manifest.Files.Count == 0 || string.IsNullOrWhiteSpace(manifest.Version))
            {
                throw new JsonException("Manifest schema, game, version or file list is invalid.");
            }

            foreach (var entry in manifest.Files)
            {
                _ = ResolveGameRelative(entry.Path);
                if (!IsSha256(entry.AfterSha256) ||
                    entry.BeforeSha256 is not null && !IsSha256(entry.BeforeSha256) ||
                    entry.BeforeSha256 is null && entry.BackupPath is not null ||
                    entry.BeforeSha256 is not null && string.IsNullOrWhiteSpace(entry.BackupPath) ||
                    entry.WasPresentBeforeInstall && entry.BeforeSha256 is null)
                {
                    throw new JsonException($"Invalid manifest entry: {entry.Path}");
                }

                if (entry.BackupPath is not null)
                {
                    _ = ResolveStateRelative(entry.BackupPath);
                }
            }

            if (manifest.Files.Select(file => NormalizeRelative(file.Path)).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count)
            {
                throw new JsonException("Manifest contains duplicate file paths.");
            }

            return manifest;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new CompanionInstallerException($"Install manifest is invalid: {exception.Message}", manifestPath, exception);
        }
    }

    private sealed record InstallManifest(
        int SchemaVersion,
        string Game,
        string Version,
        bool GameDataCreatedFromScratch,
        List<InstallFileManifest> Files);

    private sealed record InstallFileManifest(
        string Path,
        string Kind,
        string? BeforeSha256,
        string AfterSha256,
        string? BackupPath,
        bool WasPresentBeforeInstall);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true)]
    [JsonSerializable(typeof(InstallManifest), TypeInfoPropertyName = "InstallManifest")]
    [JsonSerializable(typeof(InstallJournal), TypeInfoPropertyName = "InstallJournal")]
    private partial class CompanionManifestJsonContext : JsonSerializerContext
    {
    }

    /// <summary>A null hash means "no such file" (before: created by the install; after: deleted by it).</summary>
    private sealed record InstallJournalEntry(string Path, string? BeforeSha256, string? AfterSha256, int? PreImage);

    private sealed record InstallJournal(
        int SchemaVersion,
        string ManifestSha256,
        bool StateDirectoryExisted,
        bool GameDataCreated,
        List<InstallJournalEntry> Entries,
        List<string> NewBackups);

    private sealed record PlannedInstallFile(
        string RelativePath,
        byte[] Bytes,
        byte[]? BeforeBytes,
        string Kind,
        bool WasPresentBeforeInstall);
}
