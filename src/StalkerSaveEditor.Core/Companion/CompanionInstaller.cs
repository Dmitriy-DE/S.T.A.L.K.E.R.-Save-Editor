using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Companion;

public sealed class CompanionInstaller
{
    private const int ManifestSchemaVersion = 1;
    private const string ManifestDirectoryName = ".save-editor-companion";
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
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _modSourceRoot;
    private readonly ICompanionInstallFileSystem _fileSystem;

    public CompanionInstaller(string modSourceRoot)
        : this(modSourceRoot, new PhysicalCompanionInstallFileSystem())
    {
    }

    internal CompanionInstaller(string modSourceRoot, ICompanionInstallFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modSourceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _modSourceRoot = Path.GetFullPath(modSourceRoot);
        _fileSystem = fileSystem;
    }

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
                return new CompanionInstallStatus(game, true, gameDirectory, true, manifest.Version, issues.AsReadOnly());
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

        var payloads = ReadModPayloads(definition);
        var oldFiles = priorManifest?.Files.ToDictionary(file => NormalizeRelative(file.Path), StringComparer.Ordinal)
            ?? new Dictionary<string, InstallFileManifest>(StringComparer.Ordinal);
        VerifyManagedFiles(gameDirectory, priorManifest);
        var plan = BuildInstallPlan(gameDirectory, definition, payloads, oldFiles);
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
        var manifestFiles = CreateFileManifest(gameDirectory, plan, oldFiles);
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

    private IReadOnlyList<PlannedInstallFile> BuildInstallPlan(
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

    private List<InstallFileManifest> CreateFileManifest(
        string gameDirectory,
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

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
            AtomicWrite(manifestPath, manifestBytes, overwrite: _fileSystem.FileExists(manifestPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            var rollbackErrors = RollbackInstall(gameDirectory, applied, staleOriginals);
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

    private IReadOnlyList<string> RollbackInstall(
        string gameDirectory,
        IReadOnlyList<(string Path, byte[]? Before)> applied,
        IReadOnlyList<(string Path, byte[] Bytes)> staleFiles)
    {
        var failures = new List<string>();
        foreach (var (path, before) in applied.Reverse())
        {
            try
            {
                if (before is null)
                {
                    _fileSystem.DeleteFile(path);
                }
                else
                {
                    AtomicWrite(path, before, overwrite: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        foreach (var (path, bytes) in staleFiles.Reverse())
        {
            try
            {
                AtomicWrite(path, bytes, overwrite: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        return failures.AsReadOnly();
    }

    private List<string> PreflightUninstall(string gameDirectory, InstallManifest manifest)
    {
        var conflicts = new List<string>();
        foreach (var entry in manifest.Files)
        {
            EnsureSafeWritePath(gameDirectory, entry.Path);
            var target = ResolveGamePath(gameDirectory, entry.Path);
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
            payloads = ReadModPayloads(definition);
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

            AtomicWrite(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions), overwrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new CompanionInstallerException($"Could not save companion install manifest and backups: {exception.Message}", null, exception);
        }
    }

    private IReadOnlyDictionary<string, byte[]> ReadModPayloads(CompanionGameDefinition definition)
    {
        if (!_fileSystem.DirectoryExists(_modSourceRoot))
        {
            throw new CompanionInstallerException($"Companion source directory does not exist: {_modSourceRoot}", _modSourceRoot);
        }

        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var sourceRoot in new[]
        {
            Path.Combine(_modSourceRoot, "gamedata"),
            Path.Combine(_modSourceRoot, definition.ModDirectory, "gamedata"),
        })
        {
            if (!_fileSystem.DirectoryExists(sourceRoot))
            {
                continue;
            }

            foreach (var source in _fileSystem.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(sourceRoot, source));
                var extension = Path.GetExtension(source).ToLowerInvariant();
                var bytes = _fileSystem.ReadAllBytes(source);
                payloads[relative] = extension switch
                {
                    ".script" or ".xml" or ".ltx" => ConvertUtf8ToWindows1251(bytes, source),
                    ".dds" => bytes,
                    _ => throw new CompanionInstallerException($"Unsupported companion asset type: {relative}", source),
                };
            }
        }

        if (payloads.Count == 0)
        {
            throw new CompanionInstallerException($"No companion assets found for {definition.Id}.", _modSourceRoot);
        }

        return payloads;
    }

    private IReadOnlyList<string> GetArchiveStatusIssues(
        string gameDirectory,
        CompanionGameDefinition definition)
    {
        var search = DiscoverArchives(gameDirectory, definition);
        var issues = new List<string>(search.Issues);
        if (search.Issues.Count > 0 || search.ArchivePaths.Count == 0)
        {
            return issues;
        }

        IReadOnlyList<HookFileTarget> hookTargets;
        try
        {
            hookTargets = GetHookTargets(gameDirectory, search);
        }
        catch (CompanionInstallerException exception)
        {
            issues.Add(exception.Message);
            return issues.AsReadOnly();
        }

        foreach (var hook in hookTargets)
        {
            if (_fileSystem.FileExists(ResolveGamePath(gameDirectory, hook.GameRelativePath)))
            {
                continue;
            }

            try
            {
                _ = ReadFromArchives(gameDirectory, hook.ArchiveRelativePath, definition, search);
            }
            catch (CompanionInstallerException exception)
            {
                issues.Add(exception.Message);
            }
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private byte[] ReadFromArchives(
        string gameDirectory,
        string relativePath,
        CompanionGameDefinition definition,
        CompanionArchiveSearchResult? archiveSearch = null)
    {
        var search = archiveSearch ?? DiscoverArchives(gameDirectory, definition);
        if (search.Issues.Count > 0)
        {
            throw new CompanionInstallerException(
                $"Could not resolve X-Ray archive paths from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}: {string.Join(" ", search.Issues)}",
                search.FsgamePath);
        }

        foreach (var archivePath in search.ArchivePaths.Reverse())
        {
            try
            {
                using var stream = _fileSystem.OpenRead(archivePath);
                using var archive = XRayArchiveReader.Open(stream);
                var entries = archive.Entries
                    .Where(entry => EntryMatches(entry.Name, relativePath))
                    .ToArray();
                if (entries.Length > 1)
                {
                    throw new CompanionInstallerException(
                        $"Archive contains multiple entries for {relativePath}: {archivePath}",
                        archivePath);
                }

                if (entries.Length == 1)
                {
                    return archive.ReadFile(entries[0].Name);
                }
            }
            catch (CompanionInstallerException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XRayFormatException)
            {
                throw new CompanionInstallerException(
                    $"Could not read configured X-Ray archive {archivePath}: {exception.Message}",
                    archivePath,
                    exception);
            }
        }

        var details = search.ArchivePaths.Count == 0
            ? "No archives were found in the configured fsgame paths."
            : "No configured archive contains the requested file.";
        throw new CompanionInstallerException(
            $"Could not find vanilla {relativePath}: {details} Check {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            relativePath);
    }

    private CompanionArchiveSearchResult DiscoverArchives(
        string gameDirectory,
        CompanionGameDefinition definition) =>
        CompanionArchiveLocator.Discover(
            _fileSystem,
            gameDirectory,
            definition.FsgameFileNames,
            definition.Game);

    private static IReadOnlyList<HookFileTarget> GetHookTargets(
        string gameDirectory,
        CompanionArchiveSearchResult search)
    {
        var resolvedGameDataDirectory = search.GameDataDirectory ?? throw new CompanionInstallerException(
            $"Could not resolve fsgame alias $game_data$ from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            search.FsgamePath);
        var gameDataDirectory = Path.TrimEndingDirectorySeparator(resolvedGameDataDirectory);
        var expectedGameDataDirectory = Path.GetFullPath(Path.Combine(gameDirectory, "gamedata"));
        if (!string.Equals(gameDataDirectory, expectedGameDataDirectory, PathComparison))
        {
            throw new CompanionInstallerException(
                $"The fsgame $game_data$ alias resolves outside the supported gamedata directory: {gameDataDirectory}",
                search.FsgamePath);
        }

        var configRelativePath = GetConfigRelativePath(search);
        var questItemsPath = JoinRelative(configRelativePath, "misc/quest_items.ltx");
        return Array.AsReadOnly<HookFileTarget>(
        [
            new HookFileTarget(HookFileKind.BindStalker, "gamedata/scripts/bind_stalker.script", "scripts/bind_stalker.script"),
            new HookFileTarget(HookFileKind.MainMenu, "gamedata/scripts/ui_main_menu.script", "scripts/ui_main_menu.script"),
            new HookFileTarget(HookFileKind.QuestItems, JoinRelative("gamedata", questItemsPath), questItemsPath),
        ]);
    }

    private static string GetConfigRelativePath(CompanionArchiveSearchResult search)
    {
        var gameDataDirectory = search.GameDataDirectory ?? throw new CompanionInstallerException(
            $"Could not resolve fsgame alias $game_data$ from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            search.FsgamePath);
        var gameConfigDirectory = search.GameConfigDirectory ?? throw new CompanionInstallerException(
            $"Could not resolve fsgame alias $game_config$ from {Path.GetFileName(search.FsgamePath ?? "fsgame.ltx")}.",
            search.FsgamePath);
        var relative = Path.GetRelativePath(gameDataDirectory, gameConfigDirectory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison))
        {
            throw new CompanionInstallerException(
                "The fsgame $game_config$ alias must stay under $game_data$.",
                search.FsgamePath);
        }

        return relative == "." ? string.Empty : NormalizeRelative(relative).TrimEnd('/');
    }

    private static string MapPayloadRelativePath(string relativePath, string configRelativePath)
    {
        const string sourceConfigPrefix = "configs/";
        var normalized = NormalizeRelative(relativePath);
        return normalized.StartsWith(sourceConfigPrefix, StringComparison.Ordinal)
            ? JoinRelative(configRelativePath, normalized[sourceConfigPrefix.Length..])
            : normalized;
    }

    private static string JoinRelative(params string[] paths) =>
        string.Join('/', paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeRelative(path).Trim('/')));

    private static bool EntryMatches(string entryName, string relativePath)
    {
        var name = entryName.Replace('\\', '/').TrimStart('/');
        var suffix = relativePath.Replace('\\', '/').TrimStart('/');
        return name.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ConvertUtf8ToWindows1251(byte[] bytes, string source)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text[1..];
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(text);
        }
        catch (Exception exception) when (exception is DecoderFallbackException or EncoderFallbackException)
        {
            throw new CompanionInstallerException(
                $"Companion text asset is not valid UTF-8 or cannot be represented as Windows-1251: {source}",
                source,
                exception);
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
            var manifest = JsonSerializer.Deserialize<InstallManifest>(_fileSystem.ReadAllBytes(manifestPath), ManifestJsonOptions)
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

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private bool FileMatches(string gameDirectory, string relativePath, string expectedSha)
    {
        var path = ResolveGamePath(gameDirectory, relativePath);
        return _fileSystem.FileExists(path) &&
            string.Equals(Sha256(_fileSystem.ReadAllBytes(path)), expectedSha, StringComparison.Ordinal);
    }

    private bool FileExistsInState(string gameDirectory, string relativePath) =>
        _fileSystem.FileExists(ResolveStatePath(gameDirectory, relativePath));

    private static IReadOnlyList<string> GetDefaultSteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(programFiles)) roots.Add(Path.Combine(programFiles, "Steam"));
            if (!string.IsNullOrWhiteSpace(localAppData)) roots.Add(Path.Combine(localAppData, "Programs", "Steam"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
        }
        else
        {
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        return roots.AsReadOnly();
    }

    private (string? Directory, string? Issue) ResolveGameDirectory(
        CompanionGameDefinition definition,
        string? selectedGameDirectory,
        IReadOnlyList<string>? steamRoots)
    {
        if (!string.IsNullOrWhiteSpace(selectedGameDirectory))
        {
            string selected;
            try
            {
                selected = Path.GetFullPath(selectedGameDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return (null, $"Selected game path is invalid: {exception.Message}");
            }

            return IsGameRoot(selected, definition)
                ? (selected, null)
                : (null, $"Selected directory is not a recognized {definition.Id} installation: {selected}");
        }

        var roots = steamRoots ?? GetDefaultSteamRoots();
        foreach (var library in SteamLibraryFolderLocator.GetLibraries(roots))
        {
            var manifestDirectory = SteamLibraryFolderLocator.GetManifestInstallDirectory(library, definition.SteamAppId);
            if (manifestDirectory is not null && IsGameRoot(manifestDirectory, definition))
            {
                return (Path.GetFullPath(manifestDirectory), null);
            }

            var commonDirectory = Path.Combine(library, "steamapps", "common");
            foreach (var name in definition.InstallDirectoryNames)
            {
                var candidate = Path.Combine(commonDirectory, name);
                if (IsGameRoot(candidate, definition))
                {
                    return (Path.GetFullPath(candidate), null);
                }
            }
        }

        return (null, $"Could not find an installed {definition.Id} game in the supplied Steam libraries.");
    }

    private string ResolveRequiredGameDirectory(
        CompanionGameDefinition definition,
        string? selectedGameDirectory,
        IReadOnlyList<string>? steamRoots)
    {
        var result = ResolveGameDirectory(definition, selectedGameDirectory, steamRoots);
        return result.Directory ?? throw new CompanionInstallerException(
            result.Issue ?? $"Could not find the {definition.Id} installation.");
    }

    private bool IsGameRoot(string path, CompanionGameDefinition definition) =>
        _fileSystem.DirectoryExists(path) && definition.FsgameFileNames.Any(file =>
            _fileSystem.FileExists(Path.Combine(path, file)));

    private static string GetManifestPath(string gameDirectory) =>
        Path.Combine(gameDirectory, ManifestDirectoryName, ManifestFileName);

    private string ResolveGamePath(string gameDirectory, string relativePath) =>
        ResolveUnderRoot(gameDirectory, ResolveGameRelative(relativePath));

    private string ResolveStatePath(string gameDirectory, string relativePath) =>
        ResolveUnderRoot(Path.Combine(gameDirectory, ManifestDirectoryName), ResolveStateRelative(relativePath));

    private static string ResolveGameRelative(string path) => ResolveRelative(path, "gamedata/");

    private static string ResolveStateRelative(string path) => ResolveRelative(path, "backups/");

    private static string ResolveRelative(string path, string expectedPrefix)
    {
        var normalized = NormalizeRelative(path);
        if (!normalized.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) || normalized.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new CompanionInstallerException($"Unsafe path in companion manifest: {path}", path);
        }

        return normalized;
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison) && !string.Equals(fullPath, normalizedRoot, PathComparison))
        {
            throw new CompanionInstallerException($"Path escapes its expected root: {relativePath}", relativePath);
        }

        return fullPath;
    }

    private void EnsureSafeWritePath(string gameDirectory, string relativePath)
    {
        var path = ResolveUnderRoot(gameDirectory, relativePath.Replace('\\', '/'));
        var relative = Path.GetRelativePath(gameDirectory, path);
        var current = Path.GetFullPath(gameDirectory);
        CheckNotLink(current);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => segment.Length > 0))
        {
            current = Path.Combine(current, segment);
            if (_fileSystem.FileExists(current) || _fileSystem.DirectoryExists(current))
            {
                CheckNotLink(current);
            }
        }
    }

    private void CheckNotLink(string path)
    {
        if ((_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new CompanionInstallerException($"Refusing to write through a link or reparse point: {path}", path);
        }
    }

    private void AtomicWrite(string path, byte[] bytes, bool overwrite)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Target path has no directory.", nameof(path));
        _fileSystem.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            _fileSystem.WriteAllBytes(temporary, bytes);
            _fileSystem.Move(temporary, path, overwrite);
        }
        finally
        {
            if (_fileSystem.FileExists(temporary))
            {
                _fileSystem.DeleteFile(temporary);
            }
        }
    }

    private void DeleteEmptyDirectoryTree(string path)
    {
        if (!_fileSystem.DirectoryExists(path))
        {
            return;
        }

        CheckNotLink(path);

        foreach (var child in _fileSystem.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            DeleteEmptyDirectoryTree(child);
        }

        if (!_fileSystem.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Any() &&
            !_fileSystem.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).Any())
        {
            _fileSystem.DeleteDirectory(path, recursive: false);
        }
    }

    private void RemoveManifestAndBackups(string gameDirectory, InstallManifest manifest)
    {
        var manifestPath = GetManifestPath(gameDirectory);
        EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, ManifestFileName));
        if (_fileSystem.FileExists(manifestPath))
        {
            _fileSystem.DeleteFile(manifestPath);
        }

        foreach (var entry in manifest.Files.Where(entry => entry.BackupPath is not null))
        {
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, entry.BackupPath!));
            var backup = ResolveStatePath(gameDirectory, entry.BackupPath!);
            if (_fileSystem.FileExists(backup))
            {
                _fileSystem.DeleteFile(backup);
            }
        }

        DeleteEmptyDirectoryTree(Path.GetDirectoryName(manifestPath)!);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

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

    private sealed record PlannedInstallFile(
        string RelativePath,
        byte[] Bytes,
        byte[]? BeforeBytes,
        string Kind,
        bool WasPresentBeforeInstall);
}
