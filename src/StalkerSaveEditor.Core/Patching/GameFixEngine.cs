using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

/// <summary>
/// Applies exact, byte-preserving text transformations with build gates, before/after hashes, private backups,
/// atomic file replacement, and drift-protected uninstall. Shipped definitions are supplied by GameFixCatalog.
/// </summary>
public sealed partial class GameFixEngine
{
    private const int ManifestSchemaVersion = 2;
    private const string StateDirectoryName = ".save-editor-game-fixes";
    private const string ManifestFileName = "manifest.json";
    private const string JournalFileName = "transaction.json";
    private const string RecoveryNoteFileName = "RECOVERY.txt";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9.-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IGameFileSystem _fileSystem;
    private readonly bool _allowSyntheticDefinitions;

    public GameFixEngine() : this(new PhysicalGameFileSystem(), allowSyntheticDefinitions: false) { }

    internal GameFixEngine(IGameFileSystem fileSystem, bool allowSyntheticDefinitions = false, Func<string, byte[]?>? overlayContent = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _allowSyntheticDefinitions = allowSyntheticDefinitions;
        _overlayContent = overlayContent ?? GameFixContentStore.Read;
    }

    private readonly Func<string, byte[]?> _overlayContent;
    private const string AbsentSourceFingerprint = "absent";

    public GameFixState GetStatus(GameFixDefinition definition, string gameDirectory)
    {
        ValidateDefinition(definition);
        var root = NormalizeRoot(gameDirectory);
        var manifestPath = GetManifestPath(root, definition.Id);
        CheckExistingPathForLinks(root, manifestPath);
        if (!_fileSystem.FileExists(manifestPath)) return GameFixState.NotInstalled;
        var manifest = ReadManifest(manifestPath, definition.Id);
        ValidateManifestTarget(manifest, definition.Game);
        if (!manifest.Installed) return GameFixState.Removed;
        return manifest.Files.All(file => MatchesHash(ResolveGamePath(root, file.RelativePath), file.AfterSha256))
            ? GameFixState.Installed
            : GameFixState.Modified;
    }

    public IReadOnlyList<GameFixInstalledInfo> ListInstalled(string gameDirectory) => ListInstalled(gameDirectory, strictIssues: null);

    /// <summary>
    /// For screens and reports: one fix whose state folder is damaged is named in <paramref name="issues"/> and left
    /// out, and the others are still listed. Installing, removing and snapshots keep using the strict form, which
    /// refuses to work on a game folder it cannot read completely.
    /// </summary>
    public IReadOnlyList<GameFixInstalledInfo> ListInstalled(string gameDirectory, out IReadOnlyList<string> issues)
    {
        var found = new List<string>();
        issues = found;
        return ListInstalled(gameDirectory, found);
    }

    private GameFixInstalledInfo[] ListInstalled(string gameDirectory, List<string>? strictIssues)
    {
        var root = NormalizeRoot(gameDirectory);
        return ReadActiveManifests(root, strictIssues)
            .Select(manifest => new GameFixInstalledInfo(
                manifest.FixId,
                manifest.Game,
                manifest.Version,
                manifest.Title,
                manifest.Category,
                manifest.Maturity,
                manifest.Files.All(file => MatchesHash(ResolveGamePath(root, file.RelativePath), file.AfterSha256))
                    ? GameFixState.Installed
                    : GameFixState.Modified,
                manifest.Files.Select(file => file.RelativePath).ToArray()))
            .ToArray();
    }

    public IReadOnlyList<GameFixManagedFileStatus> GetManagedFileStatus(string gameDirectory) => GetManagedFileStatus(gameDirectory, strictIssues: null);

    /// <summary>The tolerant form of <see cref="GetManagedFileStatus(string)"/>; see <see cref="ListInstalled(string, out IReadOnlyList{string})"/>.</summary>
    public IReadOnlyList<GameFixManagedFileStatus> GetManagedFileStatus(string gameDirectory, out IReadOnlyList<string> issues)
    {
        var found = new List<string>();
        issues = found;
        return GetManagedFileStatus(gameDirectory, found);
    }

    private GameFixManagedFileStatus[] GetManagedFileStatus(string gameDirectory, List<string>? strictIssues)
    {
        var root = NormalizeRoot(gameDirectory);
        return ReadActiveManifests(root, strictIssues)
            .SelectMany(manifest => manifest.Files.Select(file =>
            {
                var path = ResolveGamePath(root, file.RelativePath);
                var exists = _fileSystem.FileExists(path);
                return new GameFixManagedFileStatus(
                    manifest.FixId,
                    file.RelativePath,
                    exists,
                    exists && MatchesHash(path, file.AfterSha256));
            }))
            .ToArray();
    }

    public GameFixInstallResult Install(GameFixDefinition definition, string gameDirectory)
    {
        try
        {
            var result = Install(definition, gameDirectory, allowVersionTransition: false);
            Diagnostics.AppLog.Info($"game fix {definition?.Id} installed: changed={result.Changed}, files={result.Files.Count}");
            return result;
        }
        catch (Exception exception)
        {
            Diagnostics.AppLog.Warn($"game fix {definition?.Id} not installed", exception);
            throw;
        }
    }

    private GameFixInstallResult Install(GameFixDefinition definition, string gameDirectory, bool allowVersionTransition)
    {
        ValidateDefinition(definition);
        var root = NormalizeRoot(gameDirectory);
        CheckExistingPathForLinks(root, GetFixDirectory(root, definition.Id));
        RecoverInterrupted(root);
        var doctor = GameDoctor.Identify(definition.Game, root);
        if (!doctor.IsInstallation)
            throw new InvalidOperationException("The selected directory does not pass the structural game check.");
        if (doctor.SteamBuildId is null || !definition.SupportedSteamBuildIds.Contains(doctor.SteamBuildId, StringComparer.Ordinal))
            throw new NotSupportedException("This fix does not list the detected Steam build as supported.");
        if (!_allowSyntheticDefinitions && definition.VerificationState is GameFixVerificationState.Research or GameFixVerificationState.SyntheticTests)
            throw new NotSupportedException("Game Fix installation requires validation against the supported retail files; synthetic-test definitions are not installable.");

        var manifestPath = GetManifestPath(root, definition.Id);
        GameFixManifest? priorManifest = null;
        byte[]? priorManifestBytes = null;
        if (_fileSystem.FileExists(manifestPath))
        {
            priorManifestBytes = _fileSystem.ReadAllBytes(manifestPath);
            priorManifest = ReadManifest(manifestPath, definition.Id);
            ValidateManifestTarget(priorManifest, definition.Game);
            if (priorManifest.Installed)
            {
                if (priorManifest.Version != definition.Version)
                    throw new InvalidOperationException("A different version of this fix is installed; remove it before updating.");
                var status = GetStatus(definition, root);
                if (status == GameFixState.Installed)
                    return new GameFixInstallResult(false, status, priorManifest.Files.Select(file => file.RelativePath).ToArray());
                throw new InvalidOperationException("A managed game file changed after the fix was installed; refusing to overwrite it.");
            }

            if (priorManifest.Version != definition.Version && !allowVersionTransition)
                throw new InvalidOperationException("Reinstalling a removed fix requires the same fix version so its original backup remains authoritative.");
        }
        else if (_fileSystem.DirectoryExists(GetFixDirectory(root, definition.Id)))
        {
            throw new InvalidOperationException("Fix state exists without a valid manifest; refusing to reuse it.");
        }

        var activeManifests = ReadActiveManifests(root);
        var activeIds = activeManifests.Select(manifest => manifest.FixId).ToHashSet(StringComparer.Ordinal);
        foreach (var dependency in definition.DependsOn)
        {
            if (!activeIds.Contains(dependency)) throw new InvalidOperationException($"Required fix is not installed: {dependency}");
        }
        foreach (var conflict in definition.ConflictsWith)
        {
            if (activeIds.Contains(conflict)) throw new InvalidOperationException($"This fix conflicts with installed fix: {conflict}");
        }

        var managedPaths = activeManifests.SelectMany(manifest => manifest.Files).Select(file => file.RelativePath).ToHashSet(PathComparer);
        string Unmanaged(string relativePath)
        {
            var normalized = NormalizeRelativePath(relativePath);
            if (managedPaths.Contains(normalized))
                throw new InvalidOperationException($"Another active Game Fix manages {normalized}; layered transformations are not supported for this file.");
            return normalized;
        }

        var operations = definition.TextPatches
            .Select(operation => (Operation: operation, RelativePath: Unmanaged(operation.RelativePath)))
            .ToArray();

        if (priorManifest is not null)
        {
            var existingPaths = priorManifest.Files.Select(file => NormalizeRelativePath(file.RelativePath)).ToHashSet(PathComparer);
            var requestedPaths = operations.Select(operation => operation.RelativePath)
                .Concat(definition.Overlays.Select(overlay => NormalizeRelativePath(overlay.RelativePath)))
                .Concat(definition.SpawnEdits.Select(edit => NormalizeRelativePath(edit.RelativePath)))
                .ToHashSet(PathComparer);
            if (!existingPaths.SetEquals(requestedPaths))
                throw new InvalidOperationException("A fix version transition must keep the same managed file set so its recovery state remains authoritative.");
        }

        var overlayOperations = definition.Overlays
            .Select(overlay => (Overlay: overlay, RelativePath: Unmanaged(overlay.RelativePath)))
            .ToArray();
        var spawnPaths = definition.SpawnEdits.Select(edit => Unmanaged(edit.RelativePath)).Distinct(PathComparer).ToArray();
        EnsureNoCompanionOverlap(root, operations.Select(operation => operation.RelativePath).Concat(overlayOperations.Select(overlay => overlay.RelativePath)).Concat(spawnPaths));
        var changes = PrepareChanges(definition.Game, root, operations);
        changes.AddRange(overlayOperations.Select(overlay => PrepareOverlay(definition.Game, root, overlay.Overlay, overlay.RelativePath)));
        changes.AddRange(spawnPaths.Select(spawnPath => PrepareSpawnEdits(definition.Game, root, spawnPath,
            definition.SpawnEdits.Where(edit => PathComparer.Equals(NormalizeRelativePath(edit.RelativePath), spawnPath)).ToArray())));
        var fixDirectory = GetFixDirectory(root, definition.Id);
        var backupDirectory = Path.Combine(fixDirectory, "backups");
        var createdBackups = new List<string>();
        var applied = new List<PreparedFileChange>();
        try
        {
            _fileSystem.CreateDirectory(backupDirectory);
            var plannedFiles = changes.Select((change, index) => new ManagedGameFile(
                change.RelativePath,
                change.BeforeSha256,
                change.AfterSha256,
                PriorFile(priorManifest, change)?.BackupPath ?? $"backups/file-{index:D4}.before",
                change.TargetExistedBefore)).ToList();
            // Written before the first backup or game file: a killed process leaves this behind, and the next
            // operation on the fix puts the original files back from it.
            WriteJournal(fixDirectory, new GameFixJournal(JournalSchemaVersion, JournalInstall, priorManifest is null, plannedFiles));
            var manifestFiles = new List<ManagedGameFile>();
            for (var index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                var priorFile = PriorFile(priorManifest, change);
                var backupRelative = plannedFiles[index].BackupPath;
                var backupPath = ResolveStatePath(fixDirectory, backupRelative);
                if (priorFile is not null && _fileSystem.FileExists(backupPath))
                {
                    if (!string.Equals(Hash(_fileSystem.ReadAllBytes(backupPath)), change.BeforeSha256, StringComparison.Ordinal))
                        throw new InvalidDataException("The stored recovery file no longer matches the original hash: " + change.RelativePath);
                }
                else if (priorFile is not null)
                {
                    throw new InvalidDataException("The stored recovery file is missing: " + change.RelativePath);
                }
                else
                {
                    AtomicGameFileWriter.Write(_fileSystem, backupPath, change.BeforeBytes, overwrite: false);
                    createdBackups.Add(backupPath);
                }
                manifestFiles.Add(new ManagedGameFile(
                    change.RelativePath,
                    change.BeforeSha256,
                    change.AfterSha256,
                    backupRelative,
                    change.TargetExistedBefore));
            }

            foreach (var change in changes)
            {
                if (!MatchesPreflightSource(root, definition.Game, change))
                    throw new InvalidOperationException("A managed game file changed after preflight; refusing to overwrite it: " + change.RelativePath);
                AtomicGameFileWriter.Write(_fileSystem, change.AbsolutePath, change.AfterBytes, overwrite: true);
                applied.Add(change);
            }

            var manifest = new GameFixManifest(
                ManifestSchemaVersion,
                definition.Id,
                definition.Game,
                doctor.SteamBuildId,
                definition.Version,
                definition.Title,
                definition.Problem,
                definition.Description,
                definition.Implementation,
                definition.RequiresNewGame,
                definition.SaveCompatibility,
                definition.VerificationState,
                definition.DetectionMethod,
                definition.References.ToList(),
                definition.Category,
                definition.Maturity,
                definition.DependsOn.ToList(),
                definition.ConflictsWith.ToList(),
                definition.Source,
                Installed: true,
                manifestFiles);
            AtomicGameFileWriter.Write(_fileSystem, manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, GameFixJsonContext.Default.GameFixManifest), overwrite: priorManifestBytes is not null);
            DeleteJournal(fixDirectory);
            return new GameFixInstallResult(true, GameFixState.Installed, manifestFiles.Select(file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or JsonException)
        {
            var rollbackErrors = RollbackInstallation(applied);
            if (rollbackErrors.Count > 0)
            {
                // Some game files could not be put back. The recovery copies are the only way to restore them, so
                // nothing is cleaned up: the state directory stays and blocks reuse until the files are restored.
                var kept = WriteRecoveryNote(fixDirectory, definition.Id, changes, applied, rollbackErrors);
                throw new GameFixOperationException(GameFixFailure.RollbackIncomplete,
                    $"Game Fix installation failed: {exception.Message}; the game files could not all be restored ({string.Join("; ", rollbackErrors)}). " +
                    $"The original files are kept in {backupDirectory}{kept}.", exception);
            }
            try
            {
                if (priorManifestBytes is null)
                {
                    if (_fileSystem.FileExists(manifestPath)) _fileSystem.DeleteFile(manifestPath);
                }
                else
                {
                    AtomicGameFileWriter.Write(_fileSystem, manifestPath, priorManifestBytes, overwrite: true);
                }
                foreach (var backup in createdBackups)
                    if (_fileSystem.FileExists(backup)) _fileSystem.DeleteFile(backup);
                DeleteJournal(fixDirectory);
                if (priorManifestBytes is null)
                {
                    DeleteEmptyStateTree(fixDirectory);
                    var stateDirectory = Path.GetDirectoryName(fixDirectory);
                    if (stateDirectory is not null && _fileSystem.DirectoryExists(stateDirectory))
                    {
                        CheckExistingPathForLinks(root, stateDirectory);
                        if (!_fileSystem.EnumerateFiles(stateDirectory, "*", SearchOption.TopDirectoryOnly).Any() &&
                            !_fileSystem.EnumerateDirectories(stateDirectory, "*", SearchOption.TopDirectoryOnly).Any())
                            _fileSystem.DeleteDirectory(stateDirectory, recursive: false);
                    }
                }
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                rollbackErrors.Add(rollbackException.Message);
            }

            throw new GameFixOperationException(GameFixOperationException.KindOf(exception, rollbackErrors.Count), rollbackErrors.Count == 0
                ? $"Game Fix installation failed: {exception.Message}"
                : $"Game Fix installation failed: {exception.Message}; rollback problems: {string.Join("; ", rollbackErrors)}", exception);
        }
    }

    /// <summary>Updates an installed fix as one guarded transaction, retaining its original recovery bytes.</summary>
    public GameFixInstallResult Update(GameFixDefinition definition, string gameDirectory)
    {
        ValidateDefinition(definition);
        var root = NormalizeRoot(gameDirectory);
        var manifestPath = GetManifestPath(root, definition.Id);
        CheckExistingPathForLinks(root, manifestPath);
        RecoverInterrupted(root);
        if (!_fileSystem.FileExists(manifestPath)) return Install(definition, root);

        var oldManifestBytes = _fileSystem.ReadAllBytes(manifestPath);
        var oldManifest = ReadManifest(manifestPath, definition.Id);
        ValidateManifestTarget(oldManifest, definition.Game);
        if (!oldManifest.Installed || oldManifest.Version == definition.Version)
            return Install(definition, root);
        if (!Version.TryParse(oldManifest.Version, out var oldVersion) ||
            !Version.TryParse(definition.Version, out var newVersion) || newVersion <= oldVersion)
            throw new NotSupportedException("Fix updates must use an increasing numeric version; downgrade and ambiguous version transitions are blocked.");

        var doctor = GameDoctor.Identify(definition.Game, root);
        if (!doctor.IsInstallation)
            throw new InvalidOperationException("The selected directory does not pass the structural game check.");
        if (doctor.SteamBuildId is null || !definition.SupportedSteamBuildIds.Contains(doctor.SteamBuildId, StringComparer.Ordinal))
            throw new NotSupportedException("The updated fix does not list the detected Steam build as supported.");
        if (!_allowSyntheticDefinitions && definition.VerificationState is GameFixVerificationState.Research or GameFixVerificationState.SyntheticTests)
            throw new NotSupportedException("Game Fix installation requires validation against the supported retail files; synthetic-test definitions are not installable.");

        var oldPaths = oldManifest.Files.Select(file => NormalizeRelativePath(file.RelativePath)).ToHashSet(PathComparer);
        var newPaths = ManagedPaths(definition).ToHashSet(PathComparer);
        if (!oldPaths.SetEquals(newPaths))
            throw new NotSupportedException("In-place updates that change the managed file set are not supported; the old fix must be removed and reviewed first.");

        var oldCurrentFiles = new List<(ManagedGameFile Manifest, string Path, byte[] Bytes)>();
        foreach (var file in oldManifest.Files)
        {
            var path = ResolveGamePath(root, file.RelativePath);
            CheckExistingPathForLinks(root, path);
            if (!_fileSystem.FileExists(path))
                throw new InvalidOperationException("A managed game file is missing; refusing to update: " + file.RelativePath);
            var bytes = _fileSystem.ReadAllBytes(path);
            if (!string.Equals(Hash(bytes), file.AfterSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("A managed game file changed after installation; refusing to update it: " + file.RelativePath);
            oldCurrentFiles.Add((file, path, bytes));
        }

        Uninstall(definition.Id, definition.Game, root);
        var removedManifestBytes = _fileSystem.ReadAllBytes(manifestPath);
        try
        {
            return Install(definition, root, allowVersionTransition: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException or JsonException)
        {
            var rollbackErrors = new List<string>();
            foreach (var (file, path, bytes) in oldCurrentFiles.AsEnumerable().Reverse())
            {
                try
                {
                    if (_fileSystem.FileExists(path))
                    {
                        var currentHash = Hash(_fileSystem.ReadAllBytes(path));
                        if (string.Equals(currentHash, file.AfterSha256, StringComparison.Ordinal)) continue;
                        if (!string.Equals(currentHash, file.BeforeSha256, StringComparison.Ordinal))
                        {
                            rollbackErrors.Add("file changed during update rollback: " + file.RelativePath);
                            continue;
                        }
                        AtomicGameFileWriter.Write(_fileSystem, path, bytes, overwrite: true);
                    }
                    else
                    {
                        if (file.TargetExistedBefore)
                        {
                            rollbackErrors.Add("file disappeared during update rollback: " + file.RelativePath);
                            continue;
                        }
                        AtomicGameFileWriter.Write(_fileSystem, path, bytes, overwrite: false);
                    }
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    rollbackErrors.Add(file.RelativePath + ": " + rollbackException.Message);
                }
            }

            try
            {
                var currentManifest = _fileSystem.ReadAllBytes(manifestPath);
                if (currentManifest.AsSpan().SequenceEqual(removedManifestBytes) ||
                    currentManifest.AsSpan().SequenceEqual(oldManifestBytes))
                    AtomicGameFileWriter.Write(_fileSystem, manifestPath, oldManifestBytes, overwrite: true);
                else
                    rollbackErrors.Add("manifest changed during update rollback");
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                rollbackErrors.Add("manifest: " + rollbackException.Message);
            }

            throw new GameFixOperationException(GameFixOperationException.KindOf(exception, rollbackErrors.Count), rollbackErrors.Count == 0
                ? $"Game Fix update failed; the previous version was restored: {exception.Message}"
                : $"Game Fix update failed: {exception.Message}; rollback problems: {string.Join("; ", rollbackErrors)}", exception);
        }
    }

    public GameFixPresetResult ApplyPreset(GameTarget game, GameFixPreset preset, string gameDirectory)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        if (preset == GameFixPreset.Custom)
            throw new ArgumentException("Custom selections must be installed explicitly from the Game Fixes screen.", nameof(preset));

        var root = NormalizeRoot(gameDirectory);
        if (!_fileSystem.DirectoryExists(root)) throw new DirectoryNotFoundException("The selected game directory does not exist.");
        return ApplyFixes(game, preset, GameFixCatalog.ForPreset(game, preset), root);
    }

    internal GameFixPresetResult ApplyFixes(
        GameTarget game,
        GameFixPreset preset,
        IReadOnlyList<GameFixDefinition> definitions,
        string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (!Enum.IsDefined(preset) || preset == GameFixPreset.Custom)
            throw new ArgumentOutOfRangeException(nameof(preset));
        var selected = definitions.ToArray();
        if (selected.Any(definition => definition is null || definition.Game != game || !GameFixCatalog.IsIncludedInPreset(definition, preset)))
            throw new InvalidOperationException("A preset can contain only safe fixes for the selected game.");
        if (!_allowSyntheticDefinitions && selected.Any(definition =>
                definition.VerificationState is GameFixVerificationState.Research or GameFixVerificationState.SyntheticTests))
            throw new NotSupportedException("Preset application requires fixes validated against the supported retail files.");
        if (selected.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new InvalidOperationException("A preset cannot contain the same fix more than once.");

        var root = NormalizeRoot(gameDirectory);
        var doctor = GameDoctor.Identify(game, root);
        if (!doctor.IsInstallation)
            throw new InvalidOperationException("The selected directory does not pass the structural game check.");

        if (selected.Length > 0 && (doctor.SteamBuildId is null || selected.Any(definition =>
                !definition.SupportedSteamBuildIds.Contains(doctor.SteamBuildId, StringComparer.Ordinal))))
            throw new NotSupportedException("The detected Steam build is not supported by every selected Game Fix.");

        var pending = new List<GameFixDefinition>();
        var alreadyInstalled = new List<string>();
        foreach (var definition in selected)
        {
            var state = GetStatus(definition, root);
            if (state == GameFixState.Installed)
            {
                alreadyInstalled.Add(definition.Id);
            }
            else if (state == GameFixState.Modified)
            {
                throw new InvalidOperationException("A preset fix has an externally modified managed file; review it before applying the preset: " + definition.Id);
            }
            else
            {
                pending.Add(definition);
            }
        }

        var installed = new List<GameFixDefinition>();
        try
        {
            foreach (var definition in pending)
            {
                var result = Install(definition, root);
                if (result.Changed) installed.Add(definition);
                else alreadyInstalled.Add(definition.Id);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException or JsonException)
        {
            var rollbackErrors = new List<string>();
            foreach (var definition in installed.AsEnumerable().Reverse())
            {
                try
                {
                    Uninstall(definition, root);
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
                {
                    rollbackErrors.Add(definition.Id + ": " + rollbackException.Message);
                }
            }

            throw new GameFixOperationException(GameFixOperationException.KindOf(exception, rollbackErrors.Count), rollbackErrors.Count == 0
                ? $"Preset application failed; newly installed fixes were rolled back: {exception.Message}"
                : $"Preset application failed: {exception.Message}; rollback problems: {string.Join("; ", rollbackErrors)}", exception);
        }

        return new GameFixPresetResult(
            preset,
            selected.Length,
            installed.Select(definition => definition.Id).ToArray(),
            alreadyInstalled.Distinct(StringComparer.Ordinal).ToArray());
    }

    public GameFixInstallResult Uninstall(GameFixDefinition definition, string gameDirectory)
    {
        ValidateDefinition(definition);
        return Uninstall(definition.Id, definition.Game, gameDirectory);
    }

    public GameFixInstallResult Uninstall(string fixId, GameTarget expectedGame, string gameDirectory) =>
        Uninstall(fixId, (GameTarget?)expectedGame, gameDirectory);

    /// <summary>Removes a manifest-owned fix without requiring its definition to remain in the shipped catalogue.</summary>
    public GameFixInstallResult Uninstall(string fixId, string gameDirectory) => Uninstall(fixId, expectedGame: null, gameDirectory);

    /// <summary>Checks the same ownership, current-file and backup hashes as uninstall without changing files.</summary>
    public GameFixUninstallCheck CheckUninstall(string fixId, GameTarget expectedGame, string gameDirectory)
    {
        try
        {
            if (!IdPattern.IsMatch(fixId)) throw new ArgumentException("Fix ID must be a lowercase stable identifier.", nameof(fixId));
            var root = NormalizeRoot(gameDirectory);
            var manifestPath = GetManifestPath(root, fixId);
            CheckExistingPathForLinks(root, manifestPath);
            if (!_fileSystem.FileExists(manifestPath))
                return new GameFixUninstallCheck(false, "The provider manifest is missing.", []);

            var manifest = ReadManifest(manifestPath, fixId);
            ValidateManifestTarget(manifest, expectedGame);
            if (!manifest.Installed)
                return new GameFixUninstallCheck(false, "The manifest does not record an active fix.", []);

            var dependents = ReadActiveManifests(root)
                .Where(candidate => candidate.FixId != fixId && candidate.DependsOn.Contains(fixId, StringComparer.Ordinal))
                .Select(candidate => candidate.FixId)
                .ToArray();
            if (dependents.Length > 0)
                return new GameFixUninstallCheck(false, "Remove dependent fixes first: " + string.Join(", ", dependents), []);

            foreach (var file in manifest.Files)
            {
                var path = ResolveGamePath(root, file.RelativePath);
                CheckExistingPathForLinks(root, path);
                if (!_fileSystem.FileExists(path))
                    return new GameFixUninstallCheck(false, "A managed game file is missing: " + file.RelativePath, []);
                if (!MatchesHash(path, file.AfterSha256))
                    return new GameFixUninstallCheck(false, "A managed game file changed after installation: " + file.RelativePath, []);

                var backupPath = ResolveStatePath(GetFixDirectory(root, fixId), file.BackupPath);
                CheckExistingPathForLinks(root, backupPath);
                if (!_fileSystem.FileExists(backupPath))
                    return new GameFixUninstallCheck(false, "A recovery file is missing: " + file.RelativePath, []);
                if (!MatchesHash(backupPath, file.BeforeSha256))
                    return new GameFixUninstallCheck(false, "A recovery file failed its hash check: " + file.RelativePath, []);
            }

            return new GameFixUninstallCheck(true, null, manifest.Files.Select(file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException)
        {
            return new GameFixUninstallCheck(false, exception.Message, []);
        }
    }

    private GameFixInstallResult Uninstall(string fixId, GameTarget? expectedGame, string gameDirectory)
    {
        if (!IdPattern.IsMatch(fixId)) throw new ArgumentException("Fix ID must be a lowercase stable identifier.", nameof(fixId));
        var root = NormalizeRoot(gameDirectory);
        var manifestPath = GetManifestPath(root, fixId);
        CheckExistingPathForLinks(root, manifestPath);
        RecoverInterrupted(root);
        if (!_fileSystem.FileExists(manifestPath)) return new GameFixInstallResult(false, GameFixState.NotInstalled, []);
        var manifestBytes = _fileSystem.ReadAllBytes(manifestPath);
        var manifest = ReadManifest(manifestPath, fixId);
        if (expectedGame is { } game) ValidateManifestTarget(manifest, game);
        if (!manifest.Installed) return new GameFixInstallResult(false, GameFixState.Removed, manifest.Files.Select(file => file.RelativePath).ToArray());

        var dependents = ReadActiveManifests(root)
            .Where(candidate => candidate.FixId != fixId && candidate.DependsOn.Contains(fixId, StringComparer.Ordinal))
            .Select(candidate => candidate.FixId)
            .ToArray();
        if (dependents.Length > 0)
            throw new InvalidOperationException("Remove dependent fixes first: " + string.Join(", ", dependents));

        var changes = new List<PreparedFileChange>();
        foreach (var file in manifest.Files)
        {
            var path = ResolveGamePath(root, file.RelativePath);
            var current = _fileSystem.ReadAllBytes(path);
            if (!string.Equals(Hash(current), file.AfterSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("A managed game file changed after installation; refusing to restore it: " + file.RelativePath);
            var backupPath = ResolveStatePath(GetFixDirectory(root, fixId), file.BackupPath);
            var before = _fileSystem.ReadAllBytes(backupPath);
            if (!string.Equals(Hash(before), file.BeforeSha256, StringComparison.Ordinal))
                throw new InvalidDataException("A recovery file failed its hash check: " + file.RelativePath);
            changes.Add(new PreparedFileChange(
                file.RelativePath,
                path,
                current,
                before,
                file.AfterSha256,
                file.BeforeSha256,
                file.TargetExistedBefore,
                SourceFingerprint: null));
        }

        var applied = new List<PreparedFileChange>();
        var fixDirectory = GetFixDirectory(root, fixId);
        try
        {
            WriteJournal(fixDirectory, new GameFixJournal(JournalSchemaVersion, JournalUninstall, FreshState: false, manifest.Files));
            foreach (var change in changes)
            {
                CheckExistingPathForLinks(root, change.AbsolutePath);
                if (!MatchesHash(change.AbsolutePath, change.BeforeSha256))
                    throw new InvalidOperationException("A managed game file changed after removal preflight; refusing to restore it: " + change.RelativePath);
                if (change.TargetExistedBefore)
                {
                    AtomicGameFileWriter.Write(_fileSystem, change.AbsolutePath, change.AfterBytes, overwrite: true);
                }
                else
                {
                    _fileSystem.DeleteFile(change.AbsolutePath);
                }
                applied.Add(change);
            }
            var removed = manifest with { Installed = false };
            AtomicGameFileWriter.Write(_fileSystem, manifestPath, JsonSerializer.SerializeToUtf8Bytes(removed, GameFixJsonContext.Default.GameFixManifest), overwrite: true);
            DeleteJournal(fixDirectory);
            return new GameFixInstallResult(true, GameFixState.Removed, manifest.Files.Select(file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or JsonException)
        {
            var rollbackErrors = RollbackUninstallation(applied);
            try
            {
                AtomicGameFileWriter.Write(_fileSystem, manifestPath, manifestBytes, overwrite: true);
                // Everything is back as installed, so the journal must not "finish" the removal later.
                if (rollbackErrors.Count == 0) DeleteJournal(fixDirectory);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                rollbackErrors.Add(rollbackException.Message);
            }

            throw new GameFixOperationException(GameFixOperationException.KindOf(exception, rollbackErrors.Count), rollbackErrors.Count == 0
                ? $"Game Fix removal failed: {exception.Message}"
                : $"Game Fix removal failed: {exception.Message}; rollback problems: {string.Join("; ", rollbackErrors)}", exception);
        }
    }

    /// <summary>Paths claimed by active Game Fix manifests, used by Companion before it plans writes.</summary>
    public static IReadOnlySet<string> GetActiveManagedPaths(string gameDirectory)
    {
        var fileSystem = new PhysicalGameFileSystem();
        var root = NormalizeRoot(gameDirectory);
        return new GameFixEngine(fileSystem, allowSyntheticDefinitions: false).ReadActiveManifests(root)
            .SelectMany(manifest => manifest.Files)
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .ToHashSet(PathComparer);
    }

    private string ResolveGamePath(string root, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var absolute = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = EnsureTrailingSeparator(root);
        if (!absolute.StartsWith(rootPrefix, PathComparison))
            throw new InvalidDataException("Patch path escapes the selected game directory.");
        CheckExistingPathForLinks(root, absolute);
        return absolute;
    }

    private static string ResolveStatePath(string stateDirectory, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var absolute = Path.GetFullPath(Path.Combine(stateDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = EnsureTrailingSeparator(stateDirectory);
        if (!absolute.StartsWith(prefix, PathComparison)) throw new InvalidDataException("Backup path escapes Game Fix state.");
        return absolute;
    }

    private void CheckExistingPathForLinks(string root, string target)
    {
        var current = root;
        if (_fileSystem.DirectoryExists(current) && (_fileSystem.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing to patch through a linked game directory.");
        foreach (var segment in Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((_fileSystem.FileExists(current) || _fileSystem.DirectoryExists(current)) &&
                (_fileSystem.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to patch through a link or reparse point: " + segment);
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal))
            throw new InvalidDataException("Patch paths must be relative to the game directory.");
        var segments = path.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException("Patch path contains an empty or traversing segment.");
        if (segments[0].StartsWith(".save-editor-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Game Fixes cannot target application-managed state directories.");
        return string.Join('/', segments);
    }

    private void DeleteEmptyStateTree(string fixDirectory)
    {
        if (!_fileSystem.DirectoryExists(fixDirectory)) return;
        CheckExistingPathForLinks(Path.GetDirectoryName(Path.GetDirectoryName(fixDirectory)!)!, fixDirectory);
        foreach (var child in _fileSystem.EnumerateDirectories(fixDirectory, "*", SearchOption.TopDirectoryOnly).ToArray())
            DeleteEmptyStateTree(child);
        if (_fileSystem.DirectoryExists(fixDirectory) &&
            !_fileSystem.EnumerateFiles(fixDirectory, "*", SearchOption.TopDirectoryOnly).Any() &&
            !_fileSystem.EnumerateDirectories(fixDirectory, "*", SearchOption.TopDirectoryOnly).Any())
            _fileSystem.DeleteDirectory(fixDirectory, recursive: false);
    }

    /// <summary>Every game file a definition manages (text patches and overlays), normalised.</summary>
    public static IEnumerable<string> ManagedPaths(GameFixDefinition definition) =>
        definition.TextPatches.Select(patch => NormalizeRelativePath(patch.RelativePath))
            .Concat(definition.Overlays.Select(overlay => NormalizeRelativePath(overlay.RelativePath)))
            .Concat(definition.SpawnEdits.Select(edit => NormalizeRelativePath(edit.RelativePath)))
            .Distinct(PathComparer);

    private static string GetFixDirectory(string root, string id) => Path.Combine(root, StateDirectoryName, id);
    private static string GetManifestPath(string root, string id) => Path.Combine(GetFixDirectory(root, id), ManifestFileName);

    private static string NormalizeRoot(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
    }

    private static string EnsureTrailingSeparator(string path) => Path.EndsInDirectorySeparator(path)
        ? path
        : path + Path.DirectorySeparatorChar;

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Encoding GetTextEncoding(int codePage)
    {
        if (codePage == Encoding.Latin1.CodePage)
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        if (codePage != 1251)
            throw new NotSupportedException($"Text patch code page {codePage} is not supported.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private bool MatchesHash(string path, string expected) => _fileSystem.FileExists(path) &&
        string.Equals(Hash(_fileSystem.ReadAllBytes(path)), expected, StringComparison.Ordinal);
    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true)]
    [JsonSerializable(typeof(GameFixManifest), TypeInfoPropertyName = "GameFixManifest")]
    [JsonSerializable(typeof(GameFixJournal), TypeInfoPropertyName = "GameFixJournal")]
    private partial class GameFixJsonContext : JsonSerializerContext
    {
    }

    private sealed record GameFixManifest(
        int SchemaVersion,
        string FixId,
        [property: JsonConverter(typeof(JsonStringEnumConverter<GameTarget>))]
        GameTarget Game,
        string SteamBuildId,
        string Version,
        string Title,
        string Problem,
        string Description,
        [property: JsonConverter(typeof(JsonStringEnumConverter<GameFixImplementationType>))]
        GameFixImplementationType Implementation,
        bool RequiresNewGame,
        [property: JsonConverter(typeof(JsonStringEnumConverter<GameFixSaveCompatibility>))]
        GameFixSaveCompatibility SaveCompatibility,
        [property: JsonConverter(typeof(JsonStringEnumConverter<GameFixVerificationState>))]
        GameFixVerificationState VerificationState,
        string DetectionMethod,
        List<string> References,
        [property: JsonConverter(typeof(JsonStringEnumConverter<GameFixCategory>))]
        GameFixCategory Category,
        [property: JsonConverter(typeof(JsonStringEnumConverter<GameFixMaturity>))]
        GameFixMaturity Maturity,
        List<string> DependsOn,
        List<string> ConflictsWith,
        string Source,
        bool Installed,
        List<ManagedGameFile> Files);

    private const int JournalSchemaVersion = 1;
    private const string JournalInstall = "install";
    private const string JournalUninstall = "uninstall";

    /// <summary><c>FreshState</c>: the fix had no state folder before, so undoing the install removes it entirely.</summary>
    private sealed record GameFixJournal(int SchemaVersion, string Kind, bool FreshState, List<ManagedGameFile> Files);

    private sealed record ManagedGameFile(
        string RelativePath,
        string BeforeSha256,
        string AfterSha256,
        string BackupPath,
        bool TargetExistedBefore);

    private sealed record PreparedFileChange(
        string RelativePath,
        string AbsolutePath,
        byte[] BeforeBytes,
        byte[] AfterBytes,
        string BeforeSha256,
        string AfterSha256,
        bool TargetExistedBefore,
        string? SourceFingerprint);
}
