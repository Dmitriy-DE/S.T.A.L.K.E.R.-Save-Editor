using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

[JsonConverter(typeof(JsonStringEnumConverter<GameFixCategory>))]
public enum GameFixCategory
{
    Essential,
    Recommended,
    Optional,
    Community,
    Experimental,
}

[JsonConverter(typeof(JsonStringEnumConverter<GameFixMaturity>))]
public enum GameFixMaturity
{
    Validated,
    Experimental,
    ResearchOnly,
}

[JsonConverter(typeof(JsonStringEnumConverter<GameFixState>))]
public enum GameFixState
{
    NotInstalled,
    Installed,
    Removed,
    Modified,
}

public enum GameFixImplementationType
{
    ExactTextReplacement,
    Structured,
    BinaryPatch,
    Overlay,
}

public enum GameFixVerificationState
{
    Research,
    SyntheticTests,
    RetailFilesVerified,
    InGameVerified,
    IssueReproduced,
}

public enum GameFixSaveCompatibility
{
    Unknown,
    ExistingSaves,
    NewGameRequired,
    Incompatible,
}

public sealed record TextPatchOperation(string RelativePath, string ExpectedText, string ReplacementText)
{
    public string? ExpectedFileSha256 { get; init; }

    /// <summary>Single-byte encoding for the target text; Latin-1 preserves legacy byte-oriented patches.</summary>
    public int CodePage { get; init; } = 28591;
}

public sealed record GameFixDefinition(
    string Id,
    GameTarget Game,
    string Version,
    string Title,
    IReadOnlyList<string> SupportedSteamBuildIds,
    GameFixCategory Category,
    GameFixMaturity Maturity,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> ConflictsWith,
    IReadOnlyList<TextPatchOperation> TextPatches,
    string Source)
{
    public string Problem { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public GameFixImplementationType Implementation { get; init; } = GameFixImplementationType.ExactTextReplacement;
    public bool RequiresNewGame { get; init; }
    public GameFixSaveCompatibility SaveCompatibility { get; init; } = GameFixSaveCompatibility.Unknown;
    public GameFixVerificationState VerificationState { get; init; } = GameFixVerificationState.Research;
    public string DetectionMethod { get; init; } = string.Empty;
    public IReadOnlyList<string> References { get; init; } = [];
}

public sealed record GameFixInstallResult(bool Changed, GameFixState State, IReadOnlyList<string> Files);

public sealed record GameFixUninstallCheck(bool CanUninstall, string? Reason, IReadOnlyList<string> Files);

public sealed record GameFixPresetResult(
    GameFixPreset Preset,
    int SelectedFixCount,
    IReadOnlyList<string> InstalledFixIds,
    IReadOnlyList<string> AlreadyInstalledFixIds)
{
    public bool Changed => InstalledFixIds.Count > 0;
}

public sealed record GameFixInstalledInfo(
    string Id,
    GameTarget Game,
    string Version,
    string Title,
    GameFixCategory Category,
    GameFixMaturity Maturity,
    GameFixState State,
    IReadOnlyList<string> Files);

public sealed record GameFixManagedFileStatus(string FixId, string RelativePath, bool Exists, bool MatchesExpectedHash);

/// <summary>
/// Applies exact, byte-preserving text transformations with build gates, before/after hashes, private backups,
/// atomic file replacement, and drift-protected uninstall. Shipped definitions are supplied by GameFixCatalog.
/// </summary>
public sealed partial class GameFixEngine
{
    private const int ManifestSchemaVersion = 2;
    private const string StateDirectoryName = ".save-editor-game-fixes";
    private const string ManifestFileName = "manifest.json";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9.-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IGameFileSystem _fileSystem;
    private readonly bool _allowSyntheticDefinitions;

    public GameFixEngine() : this(new PhysicalGameFileSystem(), allowSyntheticDefinitions: false) { }

    internal GameFixEngine(IGameFileSystem fileSystem, bool allowSyntheticDefinitions = false)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _allowSyntheticDefinitions = allowSyntheticDefinitions;
    }

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

    public IReadOnlyList<GameFixInstalledInfo> ListInstalled(string gameDirectory)
    {
        var root = NormalizeRoot(gameDirectory);
        return ReadActiveManifests(root)
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

    public IReadOnlyList<GameFixManagedFileStatus> GetManagedFileStatus(string gameDirectory)
    {
        var root = NormalizeRoot(gameDirectory);
        return ReadActiveManifests(root)
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
        var doctor = GameDoctor.Analyze(definition.Game, root);
        if (doctor.Checks.Any(check => check.Id == "installation" && check.Status != GameDoctorStatus.Ok))
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

        var operations = definition.TextPatches.Select(operation =>
        {
            var normalized = NormalizeRelativePath(operation.RelativePath);
            if (activeManifests.SelectMany(manifest => manifest.Files).Any(file => PathComparer.Equals(file.RelativePath, normalized)))
                throw new InvalidOperationException($"Another active Game Fix manages {normalized}; layered transformations are not supported for this file.");
            return (Operation: operation, RelativePath: normalized);
        }).ToArray();

        if (priorManifest is not null)
        {
            var existingPaths = priorManifest.Files.Select(file => NormalizeRelativePath(file.RelativePath)).ToHashSet(PathComparer);
            var requestedPaths = operations.Select(operation => operation.RelativePath).ToHashSet(PathComparer);
            if (!existingPaths.SetEquals(requestedPaths))
                throw new InvalidOperationException("A fix version transition must keep the same managed file set so its recovery state remains authoritative.");
        }

        EnsureNoCompanionOverlap(root, operations.Select(operation => operation.RelativePath));
        var changes = PrepareChanges(definition.Game, root, operations);
        var fixDirectory = GetFixDirectory(root, definition.Id);
        var backupDirectory = Path.Combine(fixDirectory, "backups");
        var createdBackups = new List<string>();
        var applied = new List<PreparedFileChange>();
        try
        {
            _fileSystem.CreateDirectory(backupDirectory);
            var manifestFiles = new List<ManagedGameFile>();
            for (var index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                var priorFile = priorManifest?.Files.FirstOrDefault(file =>
                    PathComparer.Equals(NormalizeRelativePath(file.RelativePath), change.RelativePath));
                var backupRelative = priorFile?.BackupPath ?? $"backups/file-{index:D4}.before";
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
            return new GameFixInstallResult(true, GameFixState.Installed, manifestFiles.Select(file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or JsonException)
        {
            var rollbackErrors = RollbackInstallation(applied);
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

            throw new IOException(rollbackErrors.Count == 0
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
        if (!_fileSystem.FileExists(manifestPath)) return Install(definition, root);

        var oldManifestBytes = _fileSystem.ReadAllBytes(manifestPath);
        var oldManifest = ReadManifest(manifestPath, definition.Id);
        ValidateManifestTarget(oldManifest, definition.Game);
        if (!oldManifest.Installed || oldManifest.Version == definition.Version)
            return Install(definition, root);
        if (!Version.TryParse(oldManifest.Version, out var oldVersion) ||
            !Version.TryParse(definition.Version, out var newVersion) || newVersion <= oldVersion)
            throw new NotSupportedException("Fix updates must use an increasing numeric version; downgrade and ambiguous version transitions are blocked.");

        var doctor = GameDoctor.Analyze(definition.Game, root);
        if (doctor.Checks.Any(check => check.Id == "installation" && check.Status != GameDoctorStatus.Ok))
            throw new InvalidOperationException("The selected directory does not pass the structural game check.");
        if (doctor.SteamBuildId is null || !definition.SupportedSteamBuildIds.Contains(doctor.SteamBuildId, StringComparer.Ordinal))
            throw new NotSupportedException("The updated fix does not list the detected Steam build as supported.");
        if (!_allowSyntheticDefinitions && definition.VerificationState is GameFixVerificationState.Research or GameFixVerificationState.SyntheticTests)
            throw new NotSupportedException("Game Fix installation requires validation against the supported retail files; synthetic-test definitions are not installable.");

        var oldPaths = oldManifest.Files.Select(file => NormalizeRelativePath(file.RelativePath)).ToHashSet(PathComparer);
        var newPaths = definition.TextPatches.Select(patch => NormalizeRelativePath(patch.RelativePath)).ToHashSet(PathComparer);
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

            throw new IOException(rollbackErrors.Count == 0
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
        var doctor = GameDoctor.Analyze(game, root);
        if (doctor.Checks.Any(check => check.Id == "installation" && check.Status != GameDoctorStatus.Ok))
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

            throw new IOException(rollbackErrors.Count == 0
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
        try
        {
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
            return new GameFixInstallResult(true, GameFixState.Removed, manifest.Files.Select(file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or JsonException)
        {
            var rollbackErrors = RollbackUninstallation(applied);
            try
            {
                AtomicGameFileWriter.Write(_fileSystem, manifestPath, manifestBytes, overwrite: true);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                rollbackErrors.Add(rollbackException.Message);
            }

            throw new IOException(rollbackErrors.Count == 0
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

    private List<PreparedFileChange> PrepareChanges(
        GameTarget game,
        string root,
        IReadOnlyList<(TextPatchOperation Operation, string RelativePath)> operations)
    {
        var comparer = PathComparer;
        var grouped = new Dictionary<string, (string Path, byte[] Before, string Text, Encoding Encoding, bool TargetExistedBefore, string? SourceFingerprint)>(comparer);
        foreach (var (operation, relativePath) in operations)
        {
            var path = ResolveGamePath(root, relativePath);
            var encoding = GetTextEncoding(operation.CodePage);
            if (!grouped.TryGetValue(relativePath, out var entry))
            {
                var source = ReadTargetSource(game, root, relativePath, path);
                entry = (path, source.Bytes, encoding.GetString(source.Bytes), encoding,
                    source.TargetExistedBefore, source.SourceFingerprint);
            }
            else if (entry.Encoding.CodePage != encoding.CodePage)
            {
                throw new ArgumentException("All text patches for one file must use the same code page.", nameof(operations));
            }

            if (entry.TargetExistedBefore && !_allowSyntheticDefinitions && operation.ExpectedFileSha256 is null)
                throw new InvalidDataException($"An existing loose game-data file has no verified source hash; refusing to patch a user override: {relativePath}.");

            if (operation.ExpectedFileSha256 is { } expectedFileSha256 &&
                !string.Equals(Hash(entry.Before), expectedFileSha256, StringComparison.Ordinal))
                throw new InvalidDataException($"The source file hash does not match the verified build for {relativePath}.");

            var first = entry.Text.IndexOf(operation.ExpectedText, StringComparison.Ordinal);
            if (first < 0 || entry.Text.IndexOf(operation.ExpectedText, first + operation.ExpectedText.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException($"Expected exactly one text anchor in {relativePath}; found zero or multiple matches.");
            var patched = entry.Text[..first] + operation.ReplacementText + entry.Text[(first + operation.ExpectedText.Length)..];
            grouped[relativePath] = (entry.Path, entry.Before, patched, entry.Encoding, entry.TargetExistedBefore, entry.SourceFingerprint);
        }

        return grouped.Select(pair =>
        {
            var afterBytes = pair.Value.Encoding.GetBytes(pair.Value.Text);
            if (afterBytes.AsSpan().SequenceEqual(pair.Value.Before))
                throw new InvalidDataException("Text patch produced no change: " + pair.Key);
            return new PreparedFileChange(
                pair.Key,
                pair.Value.Path,
                pair.Value.Before,
                afterBytes,
                Hash(pair.Value.Before),
                Hash(afterBytes),
                pair.Value.TargetExistedBefore,
                pair.Value.SourceFingerprint);
        }).ToList();
    }

    private (byte[] Bytes, bool TargetExistedBefore, string? SourceFingerprint) ReadTargetSource(
        GameTarget game,
        string root,
        string relativePath,
        string absolutePath)
    {
        if (_fileSystem.FileExists(absolutePath))
            return (_fileSystem.ReadAllBytes(absolutePath), TargetExistedBefore: true, SourceFingerprint: null);

        var contentRelativePath = relativePath.StartsWith("gamedata/", StringComparison.OrdinalIgnoreCase)
            ? relativePath["gamedata/".Length..]
            : relativePath;
        var (companionGame, fsgame) = game switch
        {
            GameTarget.ShadowOfChernobyl => (CompanionGame.ShadowOfChernobyl, "fsgame.ltx"),
            GameTarget.ClearSky => (CompanionGame.ClearSky, "fsgame.ltx"),
            GameTarget.CallOfPripyat => (CompanionGame.CallOfPripyat, "fsgame.ltx"),
            GameTarget.ShadowOfChernobylEnhancedEdition => (CompanionGame.ShadowOfChernobyl, "fsgame_soc.ltx"),
            GameTarget.ClearSkyEnhancedEdition => (CompanionGame.ClearSky, "fsgame_cs.ltx"),
            GameTarget.CallOfPripyatEnhancedEdition => (CompanionGame.CallOfPripyat, "fsgame_cop.ltx"),
            _ => throw new FileNotFoundException("The target is not a loose file and this game edition has no verified archive reader.", absolutePath),
        };
        var tree = GameFileTree.Load(companionGame, root,
            candidate => string.Equals(candidate, contentRelativePath, StringComparison.OrdinalIgnoreCase), _fileSystem, [fsgame]);
        if (!tree.Files.TryGetValue(contentRelativePath, out var gameFile))
            throw new FileNotFoundException("The target file is absent from the game archives and the loose game-data directory.", absolutePath);
        return (gameFile.Read(), TargetExistedBefore: false, tree.Fingerprint);
    }

    private bool MatchesPreflightSource(string root, GameTarget game, PreparedFileChange change)
    {
        CheckExistingPathForLinks(root, change.AbsolutePath);
        if (change.TargetExistedBefore)
            return MatchesHash(change.AbsolutePath, change.BeforeSha256);

        if (_fileSystem.FileExists(change.AbsolutePath)) return false;
        try
        {
            var current = ReadTargetSource(game, root, change.RelativePath, change.AbsolutePath);
            return !current.TargetExistedBefore &&
                string.Equals(current.SourceFingerprint, change.SourceFingerprint, StringComparison.Ordinal) &&
                string.Equals(Hash(current.Bytes), change.BeforeSha256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }

    private List<string> RollbackInstallation(IReadOnlyList<PreparedFileChange> applied)
    {
        var errors = new List<string>();
        foreach (var change in applied.Reverse())
        {
            try
            {
                if (!_fileSystem.FileExists(change.AbsolutePath)) continue;
                if (!string.Equals(Hash(_fileSystem.ReadAllBytes(change.AbsolutePath)), change.AfterSha256, StringComparison.Ordinal))
                {
                    errors.Add("file changed during rollback: " + change.RelativePath);
                    continue;
                }
                if (change.TargetExistedBefore)
                    AtomicGameFileWriter.Write(_fileSystem, change.AbsolutePath, change.BeforeBytes, overwrite: true);
                else
                    _fileSystem.DeleteFile(change.AbsolutePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                errors.Add(change.RelativePath + ": " + exception.Message);
            }
        }
        return errors;
    }

    private List<string> RollbackUninstallation(IReadOnlyList<PreparedFileChange> applied)
    {
        var errors = new List<string>();
        foreach (var change in applied.Reverse())
        {
            try
            {
                if (change.TargetExistedBefore)
                {
                    if (!MatchesHash(change.AbsolutePath, change.AfterSha256))
                    {
                        errors.Add("file changed during rollback: " + change.RelativePath);
                        continue;
                    }
                }
                else if (_fileSystem.FileExists(change.AbsolutePath))
                {
                    if (MatchesHash(change.AbsolutePath, change.BeforeSha256)) continue;
                    errors.Add("file changed during rollback: " + change.RelativePath);
                    continue;
                }

                AtomicGameFileWriter.Write(_fileSystem, change.AbsolutePath, change.BeforeBytes, overwrite: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                errors.Add(change.RelativePath + ": " + exception.Message);
            }
        }

        return errors;
    }

    private void EnsureNoCompanionOverlap(string root, IEnumerable<string> plannedPaths)
    {
        var manifestPath = Path.Combine(root, ".save-editor-companion", "manifest.json");
        var manualMarker = Path.Combine(root, "gamedata", "scripts", "save_editor_companion.script");
        CheckExistingPathForLinks(root, manifestPath);
        CheckExistingPathForLinks(root, manualMarker);
        if (!_fileSystem.FileExists(manifestPath))
        {
            if (_fileSystem.FileExists(manualMarker))
                throw new InvalidOperationException("A manual Companion installation has no verifiable file manifest; Game Fixes are blocked to avoid overwriting its layers.");
            return;
        }

        using var document = JsonDocument.Parse(_fileSystem.ReadAllBytes(manifestPath));
        if (!TryGetProperty(document.RootElement, "files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Companion manifest does not contain a valid file list; Game Fix installation is blocked.");
        var companionPaths = new HashSet<string>(PathComparer);
        foreach (var file in files.EnumerateArray())
        {
            if (!TryGetProperty(file, "path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String || pathElement.GetString() is not { Length: > 0 } path)
                throw new InvalidDataException("Companion manifest contains an invalid managed file path.");
            companionPaths.Add(NormalizeRelativePath(path));
        }

        var overlap = plannedPaths.FirstOrDefault(companionPaths.Contains);
        if (overlap is not null)
            throw new InvalidOperationException("Game Fix overlaps a Companion-managed file; layered transformations are not supported for " + overlap);
    }

    private List<GameFixManifest> ReadActiveManifests(string root)
    {
        var directory = Path.Combine(root, StateDirectoryName);
        if (!_fileSystem.DirectoryExists(directory)) return [];
        CheckExistingPathForLinks(root, directory);
        var result = new List<GameFixManifest>();
        foreach (var fixDirectory in _fileSystem.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            CheckExistingPathForLinks(root, fixDirectory);
            var manifestPath = Path.Combine(fixDirectory, ManifestFileName);
            CheckExistingPathForLinks(root, manifestPath);
            if (!_fileSystem.FileExists(manifestPath))
                throw new InvalidDataException("Game Fix state directory is missing a manifest: " + Path.GetFileName(fixDirectory));
            var manifest = ReadManifest(manifestPath, Path.GetFileName(fixDirectory), _fileSystem);
            if (manifest.Installed) result.Add(manifest);
        }
        return result;
    }

    private static GameFixManifest ReadManifest(string manifestPath, string expectedId, IGameFileSystem? fileSystem = null)
    {
        fileSystem ??= new PhysicalGameFileSystem();
        try
        {
            var manifest = JsonSerializer.Deserialize(fileSystem.ReadAllBytes(manifestPath), GameFixJsonContext.Default.GameFixManifest)
                ?? throw new InvalidDataException("Game Fix manifest is empty.");
            if (manifest.SchemaVersion != ManifestSchemaVersion || manifest.FixId != expectedId || !IdPattern.IsMatch(manifest.FixId) ||
                string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Title) ||
                string.IsNullOrWhiteSpace(manifest.Problem) || string.IsNullOrWhiteSpace(manifest.Description) ||
                string.IsNullOrWhiteSpace(manifest.Source) || string.IsNullOrWhiteSpace(manifest.DetectionMethod) || manifest.References is null ||
                !Enum.IsDefined(manifest.Game) || !Enum.IsDefined(manifest.Category) || !Enum.IsDefined(manifest.Maturity) ||
                !Enum.IsDefined(manifest.Implementation) || !Enum.IsDefined(manifest.SaveCompatibility) || !Enum.IsDefined(manifest.VerificationState) ||
                string.IsNullOrWhiteSpace(manifest.SteamBuildId) || manifest.SteamBuildId.Any(character => !char.IsAsciiDigit(character)) ||
                manifest.DependsOn is null || manifest.ConflictsWith is null || manifest.Files is null || manifest.Files.Count is < 1 or > 1_000 ||
                manifest.DependsOn.Any(id => string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id)) ||
                manifest.ConflictsWith.Any(id => string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id)))
                throw new InvalidDataException("Game Fix manifest failed validation.");

            var paths = new HashSet<string>(PathComparer);
            var backupPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                if (file is null || !IsSha256(file.BeforeSha256) || !IsSha256(file.AfterSha256))
                    throw new InvalidDataException("Game Fix manifest contains invalid file hashes.");
                var relative = NormalizeRelativePath(file.RelativePath);
                if (!string.Equals(relative, file.RelativePath, StringComparison.Ordinal) || !paths.Add(relative))
                    throw new InvalidDataException("Game Fix manifest contains a noncanonical or duplicate target path.");
                var backup = NormalizeRelativePath(file.BackupPath);
                if (!string.Equals(backup, file.BackupPath, StringComparison.Ordinal) ||
                    !backup.StartsWith("backups/", StringComparison.Ordinal) || !backupPaths.Add(backup))
                    throw new InvalidDataException("Game Fix manifest contains an invalid recovery path.");
            }
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Game Fix manifest is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Game Fix manifest contains an invalid path.", exception);
        }
    }

    private static void ValidateManifestTarget(GameFixManifest manifest, GameTarget target)
    {
        if (manifest.Game != target) throw new InvalidDataException("Game Fix manifest targets a different game.");
    }

    private static void ValidateDefinition(GameFixDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IdPattern.IsMatch(definition.Id)) throw new ArgumentException("Fix ID must be a lowercase stable identifier.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Version) || string.IsNullOrWhiteSpace(definition.Title) || string.IsNullOrWhiteSpace(definition.Source))
            throw new ArgumentException("Fix version, title and provenance are required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Problem) || string.IsNullOrWhiteSpace(definition.Description) ||
            string.IsNullOrWhiteSpace(definition.DetectionMethod) || definition.References is null ||
            definition.References.Any(reference => string.IsNullOrWhiteSpace(reference)))
            throw new ArgumentException("Fix problem, description, detection method and references metadata are required.", nameof(definition));
        if (definition.SupportedSteamBuildIds is null || definition.SupportedSteamBuildIds.Count == 0 ||
            definition.SupportedSteamBuildIds.Any(build => string.IsNullOrWhiteSpace(build) || build.Any(character => !char.IsAsciiDigit(character))))
            throw new ArgumentException("At least one supported Steam build ID is required.", nameof(definition));
        if (definition.Maturity == GameFixMaturity.ResearchOnly)
            throw new NotSupportedException("Research-only fixes cannot be installed.");
        if (!Enum.IsDefined(definition.Game) || !Enum.IsDefined(definition.Category) || !Enum.IsDefined(definition.Maturity) ||
            !Enum.IsDefined(definition.Implementation) || !Enum.IsDefined(definition.VerificationState) || !Enum.IsDefined(definition.SaveCompatibility))
            throw new ArgumentException("Fix target, category and maturity must be recognized values.", nameof(definition));
        if (definition.Implementation != GameFixImplementationType.ExactTextReplacement)
            throw new NotSupportedException("This engine only implements exact single-byte text replacements.");
        if (definition.TextPatches is null || definition.TextPatches.Count == 0)
            throw new ArgumentException("At least one text patch is required.", nameof(definition));
        foreach (var operation in definition.TextPatches)
        {
            if (operation is null) throw new ArgumentException("Text patch operations cannot be null.", nameof(definition));
            _ = NormalizeRelativePath(operation.RelativePath);
            if (string.IsNullOrEmpty(operation.ExpectedText) || operation.ReplacementText is null || operation.ExpectedText == operation.ReplacementText)
                throw new ArgumentException("Text patches need a nonempty anchor and a distinct replacement.", nameof(definition));
            if (operation.ExpectedText.Contains('\0') || operation.ReplacementText.Contains('\0'))
                throw new ArgumentException("Text patch anchors and replacements cannot contain NUL characters.", nameof(definition));
            try
            {
                var encoding = GetTextEncoding(operation.CodePage);
                _ = encoding.GetBytes(operation.ExpectedText);
                _ = encoding.GetBytes(operation.ReplacementText);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or EncoderFallbackException)
            {
                throw new ArgumentException("Text patch anchors and replacements must be representable in the selected single-byte game text encoding.", nameof(definition), exception);
            }
            if (operation.ExpectedFileSha256 is not null && !IsSha256(operation.ExpectedFileSha256))
                throw new ArgumentException("Expected source-file SHA-256 values must be lowercase hexadecimal.", nameof(definition));
        }
        if (definition.DependsOn is null || definition.ConflictsWith is null ||
            definition.DependsOn.Any(id => string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id) || id == definition.Id) ||
            definition.ConflictsWith.Any(id => string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id) || id == definition.Id) ||
            definition.DependsOn.Distinct(StringComparer.Ordinal).Count() != definition.DependsOn.Count ||
            definition.ConflictsWith.Distinct(StringComparer.Ordinal).Count() != definition.ConflictsWith.Count)
            throw new ArgumentException("Dependency and conflict IDs must be stable fix identifiers.", nameof(definition));
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
