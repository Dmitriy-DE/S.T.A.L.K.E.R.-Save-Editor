using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Patching;

public sealed record ToolkitSnapshotFile(string Provider, string RelativePath, string Sha256, long Length);

public sealed record ToolkitSnapshotInfo(
    string Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<GameTarget>))]
    GameTarget Target,
    string GameDirectory,
    DateTime CreatedUtc,
    IReadOnlyList<string> FixIds,
    bool CompanionInstalled,
    IReadOnlyList<ToolkitSnapshotFile> Files,
    string? UserLtxPath,
    IReadOnlyDictionary<string, string>? UserLtxOverrides);

public sealed record ToolkitSnapshotRestoreResult(bool Restored, string Message, string? SafetySnapshotId);

public sealed record ToolkitFixPresetApplicationResult(
    GameFixPresetResult? PresetResult,
    string? Error,
    string? SafetySnapshotId,
    bool PriorStateRestored)
{
    public bool Applied => PresetResult is not null;
}

/// <summary>
/// Captures hashes and content only for Game Fix, Companion and explicitly managed user.ltx state.
/// Restore always replays through those providers; content-addressed objects are never copied into a game.
/// </summary>
public sealed partial class ToolkitSnapshotService
{
    private const int SchemaVersion = 1;
    private readonly string _root;
    private readonly GameFixEngine _engine;
    private readonly IReadOnlyDictionary<string, GameFixDefinition> _definitions;
    private readonly CompanionInstaller? _companion;
    private readonly string _configStateDirectory;
    public ToolkitSnapshotService()
        : this(AppPaths.Snapshots, new GameFixEngine(), GameFixCatalog.All.ToDictionary(fix => fix.Id, StringComparer.Ordinal),
            new CompanionInstaller(CompanionAssetLocator.ResolveSourceRoot()), AppPaths.ToolkitConfig)
    {
    }

    internal ToolkitSnapshotService(
        string snapshotRoot,
        GameFixEngine engine,
        IReadOnlyDictionary<string, GameFixDefinition> definitions,
        CompanionInstaller? companion = null,
        string? configStateDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotRoot);
        _root = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(snapshotRoot));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _companion = companion;
        _configStateDirectory = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(configStateDirectory ?? AppPaths.ToolkitConfig));
    }

    public ToolkitSnapshotInfo Create(GameTarget target, string gameDirectory, string? userLtxPath = null)
    {
        var gameRoot = CanonicalDirectory(gameDirectory);
        if (GameTargetCatalog.Get(target).IsXRay is false)
            throw new NotSupportedException("Toolkit snapshots currently support X-Ray managed providers only.");

        var installed = _engine.ListInstalled(gameRoot);
        var modified = installed.FirstOrDefault(fix => fix.State != GameFixState.Installed);
        if (modified is not null)
            throw new InvalidOperationException($"Game Fix {modified.Id} has drifted; resolve its conflict before creating a snapshot.");

        var files = new List<ToolkitSnapshotFile>();
        foreach (var status in _engine.GetManagedFileStatus(gameRoot))
        {
            if (!status.Exists || !status.MatchesExpectedHash)
                throw new InvalidOperationException($"Managed Game Fix file changed or is missing: {status.RelativePath}");
            AddObject("GameFix", gameRoot, status.RelativePath, files);
        }

        foreach (var fix in installed)
        {
            var statePath = Path.Combine(gameRoot, ".save-editor-game-fixes", fix.Id);
            AddProviderDirectory("GameFixState", gameRoot, statePath, files);
        }

        var companionInstalled = CaptureCompanion(target, gameRoot, files);
        string? canonicalUserLtx = null;
        IReadOnlyDictionary<string, string>? overrides = null;
        if (!string.IsNullOrWhiteSpace(userLtxPath))
        {
            var inspection = ManagedUserLtxSettings.Inspect(userLtxPath, _configStateDirectory);
            if (inspection.HasConflict)
                throw new InvalidOperationException(inspection.Conflict ?? "Managed user.ltx settings need review.");
            canonicalUserLtx = inspection.FilePath;
            overrides = inspection.Settings
                .Where(setting => setting.ChangedByToolkit && setting.CurrentValue is not null)
                .ToDictionary(setting => setting.Key, setting => setting.CurrentValue!, StringComparer.OrdinalIgnoreCase);
            if (ManagedUserLtxSettings.ManagedManifestPath(canonicalUserLtx, _configStateDirectory) is { } settingsManifest)
                AddExternalObject("UserLtxState", settingsManifest, Path.GetFileName(settingsManifest), files);
        }

        var snapshotId = Guid.NewGuid().ToString("N");
        var info = new ToolkitSnapshotInfo(snapshotId, target, gameRoot, DateTime.UtcNow,
            installed.Select(fix => fix.Id).Order(StringComparer.Ordinal).ToArray(), companionInstalled,
            files.OrderBy(file => file.Provider, StringComparer.Ordinal).ThenBy(file => file.RelativePath, StringComparer.Ordinal).ToArray(),
            canonicalUserLtx, overrides);
        Persist(info);
        return info;
    }

    /// <summary>Applies a multi-fix preset only after recording a compatible installation's managed state.</summary>
    public ToolkitFixPresetApplicationResult ApplyFixPreset(GameTarget target, string gameDirectory, GameFixPreset preset)
    {
        var gameRoot = CanonicalDirectory(gameDirectory);
        var definitions = GameFixCatalog.ForPreset(target, preset);
        var report = GameDoctor.Analyze(target, gameRoot);
        var compatibleBuild = report.SteamBuildId is { } buildId && definitions.Count > 0 &&
            definitions.All(definition => definition.SupportedSteamBuildIds.Contains(buildId, StringComparer.Ordinal));
        var requiresChange = compatibleBuild && definitions.Any(definition =>
            _engine.GetStatus(definition, gameRoot) != GameFixState.Installed);
        var safety = requiresChange ? Create(target, gameRoot) : null;

        try
        {
            var result = _engine.ApplyPreset(target, preset, gameRoot);
            return new ToolkitFixPresetApplicationResult(result, null, safety?.Id, PriorStateRestored: false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        {
            if (safety is null)
                return new ToolkitFixPresetApplicationResult(null, failure.Message, null, PriorStateRestored: false);

            try
            {
                var rollback = Restore(safety.Id);
                return new ToolkitFixPresetApplicationResult(null,
                    rollback.Restored
                        ? $"Preset application failed and the previous managed state was restored: {failure.Message}"
                        : $"Preset application failed: {failure.Message}. {rollback.Message}",
                    safety.Id,
                    rollback.Restored);
            }
            catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or
                ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return new ToolkitFixPresetApplicationResult(null,
                    $"Preset application failed: {failure.Message}. Automatic restore also failed: {rollbackFailure.Message}. Safety snapshot {safety.Id} is retained.",
                    safety.Id,
                    PriorStateRestored: false);
            }
        }
    }

    public IReadOnlyList<ToolkitSnapshotInfo> List()
    {
        var directory = SnapshotDirectory;
        if (!Directory.Exists(directory)) return [];
        RejectLink(directory);
        var results = new List<ToolkitSnapshotInfo>();
        foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            RejectLink(child);
            var path = Path.Combine(child, "snapshot.json");
            if (!File.Exists(path)) continue;
            RejectLink(path);
            results.Add(ReadSnapshot(path, validateObjects: true, reuseSessionHash: true));
        }
        return results.OrderByDescending(snapshot => snapshot.CreatedUtc).ThenBy(snapshot => snapshot.Id, StringComparer.Ordinal).ToArray();
    }

    public ToolkitSnapshotInfo Inspect(string id)
    {
        ValidateId(id);
        return ReadSnapshot(SnapshotPath(id), validateObjects: true);
    }

    public void Delete(string id)
    {
        ValidateId(id);
        var path = SnapshotPath(id);
        if (!File.Exists(path)) return;
        RejectLink(path);
        _ = ReadSnapshot(path, validateObjects: true);
        File.Delete(path);
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        PruneObjects();
    }

    public ToolkitSnapshotRestoreResult Restore(string id)
    {
        var snapshot = Inspect(id);
        var currentRoot = CanonicalDirectory(snapshot.GameDirectory);
        if (!PathEquals(currentRoot, snapshot.GameDirectory))
            throw new InvalidOperationException("The target game directory no longer resolves to the snapshot installation.");
        var safety = Create(snapshot.Target, currentRoot, snapshot.UserLtxPath);

        try
        {
            EnsureNoCurrentDrift(snapshot.Target, currentRoot);
            ReconcileFixes(snapshot.Target, currentRoot, snapshot.FixIds);
            ReconcileCompanion(snapshot.Target, currentRoot, snapshot.CompanionInstalled, snapshot.Files);
            ReconcileUserLtx(snapshot, restoreOwnershipManifest: true);
            VerifyRestoredState(snapshot);
            return new ToolkitSnapshotRestoreResult(true,
                $"Snapshot restored. Automatic safety snapshot: {safety.Id}.", safety.Id);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            try
            {
                EnsureNoCurrentDrift(safety.Target, currentRoot);
                ReconcileFixes(safety.Target, currentRoot, safety.FixIds);
                ReconcileCompanion(safety.Target, currentRoot, safety.CompanionInstalled, safety.Files);
                ReconcileUserLtx(safety, restoreOwnershipManifest: true);
            }
            catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                throw new IOException($"Snapshot restore failed: {failure.Message}; automatic rollback could not finish: {rollbackFailure.Message}. Safety snapshot {safety.Id} is retained.", failure);
            }

            return new ToolkitSnapshotRestoreResult(false,
                $"Snapshot restore failed and the prior managed state was replayed: {failure.Message}. Safety snapshot {safety.Id} is retained.", safety.Id);
        }
    }

    /// <summary>Applies one explicit managed state and creates a recovery snapshot before any change.</summary>
    public ToolkitSnapshotRestoreResult ApplyManagedState(
        GameTarget target,
        string gameDirectory,
        IReadOnlyCollection<string> fixIds,
        bool companionInstalled,
        string? userLtxPath = null,
        IReadOnlyDictionary<string, string>? userLtxOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(fixIds);
        var root = CanonicalDirectory(gameDirectory);
        var desiredIds = fixIds.Distinct(StringComparer.Ordinal).ToArray();
        _ = TopologicalOrder(desiredIds);
        foreach (var id in desiredIds)
        {
            if (!_definitions.TryGetValue(id, out var definition) || definition.Game != target)
                throw new InvalidDataException($"Unknown or mismatched Game Fix requested by the profile: {id}");
        }
        if (companionInstalled) _ = ToCompanionGame(target);
        if (userLtxOverrides is { Count: > 0 })
        {
            if (userLtxPath is null) throw new ArgumentException("A user.ltx path is required for profile config overrides.", nameof(userLtxPath));
            userLtxOverrides = ManagedUserLtxSettings.NormalizeOverrides(userLtxOverrides);
        }

        var safety = Create(target, root, userLtxPath);
        try
        {
            EnsureNoCurrentDrift(target, root);
            ReconcileFixes(target, root, desiredIds);
            ReconcileCompanion(target, root, companionInstalled);
            var desired = new ToolkitSnapshotInfo(safety.Id, target, root, safety.CreatedUtc, desiredIds,
                companionInstalled, [], userLtxPath is null ? null : ManagedUserLtxSettings.Inspect(userLtxPath, _configStateDirectory).FilePath,
                userLtxOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            ReconcileUserLtx(desired, restoreOwnershipManifest: false);

            var actualFixIds = _engine.ListInstalled(root).Select(fix => fix.Id).Order(StringComparer.Ordinal).ToArray();
            if (!actualFixIds.SequenceEqual(desiredIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new IOException("The installed Game Fix set does not match the requested profile.");
            if (userLtxPath is not null)
            {
                var inspection = ManagedUserLtxSettings.Inspect(userLtxPath, _configStateDirectory);
                var actualOverrides = inspection.Settings.Where(setting => setting.ChangedByToolkit && setting.CurrentValue is not null)
                    .ToDictionary(setting => setting.Key, setting => setting.CurrentValue!, StringComparer.OrdinalIgnoreCase);
                if (inspection.HasConflict || actualOverrides.Count != (userLtxOverrides?.Count ?? 0) ||
                    (userLtxOverrides ?? new Dictionary<string, string>()).Any(pair => !actualOverrides.TryGetValue(pair.Key, out var value) || !string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("The managed user.ltx settings do not match the requested profile.");
            }
            return new ToolkitSnapshotRestoreResult(true, $"Managed state applied. Automatic safety snapshot: {safety.Id}.", safety.Id);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            var rollback = Restore(safety.Id);
            if (!rollback.Restored)
                throw new IOException($"Profile change failed: {failure.Message}. Automatic rollback failed: {rollback.Message}", failure);
            return new ToolkitSnapshotRestoreResult(false,
                $"Profile change failed and the prior managed state was restored: {failure.Message}. Safety snapshot {safety.Id} is retained.", safety.Id);
        }
    }

    private void EnsureNoCurrentDrift(GameTarget target, string gameRoot)
    {
        var changedFix = _engine.ListInstalled(gameRoot).FirstOrDefault(fix => fix.State != GameFixState.Installed);
        if (changedFix is not null) throw new InvalidOperationException($"Game Fix {changedFix.Id} has drifted; refusing snapshot restore.");
        foreach (var file in _engine.GetManagedFileStatus(gameRoot))
            if (!file.Exists || !file.MatchesExpectedHash)
                throw new InvalidOperationException($"Managed Game Fix file changed or is missing: {file.RelativePath}");
        var companionManifest = Path.Combine(gameRoot, ".save-editor-companion", "manifest.json");
        var companionMarker = Path.Combine(gameRoot, "gamedata", "scripts", "save_editor_companion.script");
        if (!File.Exists(companionManifest) && File.Exists(companionMarker))
            throw new InvalidOperationException("Companion files exist without a verifiable manifest; review them before changing a profile.");
        if (File.Exists(companionManifest))
        {
            var ignored = new List<ToolkitSnapshotFile>();
            if (!CaptureCompanion(target, gameRoot, ignored))
                throw new InvalidOperationException("Companion state needs review before changing a profile.");
        }
    }

    private void ReconcileFixes(GameTarget target, string gameRoot, IReadOnlyCollection<string> desiredIds)
    {
        var desired = desiredIds.ToHashSet(StringComparer.Ordinal);
        foreach (var installed in _engine.ListInstalled(gameRoot).OrderByDescending(fix => DependencyDepth(fix.Id)).ThenBy(fix => fix.Id, StringComparer.Ordinal))
        {
            if (!desired.Contains(installed.Id)) _engine.Uninstall(installed.Id, gameRoot);
        }
        foreach (var id in TopologicalOrder(desiredIds))
        {
            if (!_definitions.TryGetValue(id, out var definition) || definition.Game != target)
                throw new InvalidDataException($"Snapshot refers to an unknown or mismatched Game Fix: {id}");
            if (_engine.GetStatus(definition, gameRoot) != GameFixState.Installed) _engine.Install(definition, gameRoot);
        }
    }

    private void ReconcileCompanion(
        GameTarget target,
        string gameRoot,
        bool desiredInstalled,
        IReadOnlyList<ToolkitSnapshotFile>? snapshotFiles = null)
    {
        if (_companion is null)
        {
            if (desiredInstalled) throw new NotSupportedException("Companion snapshot restore is unavailable because its provider is not configured.");
            return;
        }
        var manifest = Path.Combine(gameRoot, ".save-editor-companion", "manifest.json");
        var currentlyManaged = File.Exists(manifest);
        // The companion exists for the original trilogy only. For any other target "absent → absent" is nothing to do;
        // asking for it (or finding one there) is still refused below.
        if (!desiredInstalled && !currentlyManaged && target is not (GameTarget.ShadowOfChernobyl or GameTarget.ClearSky or GameTarget.CallOfPripyat))
            return;
        var game = ToCompanionGame(target);
        if (currentlyManaged)
        {
            var status = _companion.GetStatus(game, gameRoot);
            if (!status.ModInstalled || status.Issues.Any(issue => issue.Contains("changed or missing", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Companion files or manifest need review before a snapshot restore.");
        }
        if (desiredInstalled && !currentlyManaged)
        {
            var result = _companion.Install(game, gameRoot);
            if (!result.Success) throw new InvalidOperationException("Companion could not be installed from the current bundle.");
        }
        else if (!desiredInstalled && currentlyManaged)
        {
            var result = _companion.Uninstall(game, gameRoot);
            if (!result.Success) throw new InvalidOperationException("Companion uninstall reported a managed-file conflict.");
        }

        if (desiredInstalled && snapshotFiles is not null)
        {
            var stateFiles = snapshotFiles.Where(file => file.Provider == "CompanionState")
                .ToDictionary(file => file.RelativePath, file => File.ReadAllBytes(Path.Combine(ObjectDirectory, file.Sha256)), StringComparer.Ordinal);
            if (stateFiles.Count == 0) throw new InvalidDataException("Companion snapshot state is missing its ownership manifest.");
            _companion.RestoreManagedSnapshot(game, gameRoot, stateFiles);
        }
    }

    private void ReconcileUserLtx(ToolkitSnapshotInfo snapshot, bool restoreOwnershipManifest)
    {
        if (snapshot.UserLtxPath is null) return;
        var inspection = ManagedUserLtxSettings.Inspect(snapshot.UserLtxPath, _configStateDirectory);
        if (inspection.HasConflict) throw new InvalidOperationException(inspection.Conflict ?? "Managed user.ltx settings have drifted.");
        var wanted = snapshot.UserLtxOverrides ?? new Dictionary<string, string>();
        foreach (var setting in inspection.Settings.Where(setting => setting.ChangedByToolkit && !wanted.ContainsKey(setting.Key)))
            ManagedUserLtxSettings.RestoreDefault(snapshot.UserLtxPath, _configStateDirectory, setting.Key);
        if (wanted.Count > 0) ManagedUserLtxSettings.SetOverrides(snapshot.UserLtxPath, _configStateDirectory, wanted);
        if (restoreOwnershipManifest)
        {
            var state = snapshot.Files.FirstOrDefault(file => file.Provider == "UserLtxState");
            var manifest = state is null ? null : File.ReadAllBytes(Path.Combine(ObjectDirectory, state.Sha256));
            ManagedUserLtxSettings.RestoreOwnershipManifest(snapshot.UserLtxPath, _configStateDirectory, manifest);
        }
    }

    private bool CaptureCompanion(GameTarget target, string gameRoot, List<ToolkitSnapshotFile> files)
    {
        var manifest = Path.Combine(gameRoot, ".save-editor-companion", "manifest.json");
        if (!File.Exists(manifest)) return false;
        if (_companion is null) throw new NotSupportedException("Companion is installed but the snapshot provider is unavailable.");
        var game = ToCompanionGame(target);
        var status = _companion.GetStatus(game, gameRoot);
        if (!status.ModInstalled || status.Issues.Any(issue => issue.Contains("changed or missing", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Companion files or manifest need review before creating a snapshot.");
        foreach (var file in _companion.GetManagedFileStatus(game, gameRoot))
        {
            if (!file.Exists || !file.MatchesExpectedHash)
                throw new InvalidOperationException($"Managed Companion file changed or is missing: {file.RelativePath}");
            AddObject("Companion", gameRoot, file.RelativePath, files);
        }
        AddProviderDirectory("CompanionState", gameRoot, Path.Combine(gameRoot, ".save-editor-companion"), files);
        return true;
    }

    private void VerifyRestoredState(ToolkitSnapshotInfo snapshot)
    {
        var actual = new List<ToolkitSnapshotFile>();
        foreach (var status in _engine.GetManagedFileStatus(snapshot.GameDirectory))
        {
            if (!status.Exists || !status.MatchesExpectedHash) throw new IOException($"Restored Game Fix file failed its provider hash check: {status.RelativePath}");
            AddObject("GameFix", snapshot.GameDirectory, status.RelativePath, actual);
        }
        foreach (var fix in _engine.ListInstalled(snapshot.GameDirectory))
            AddProviderDirectory("GameFixState", snapshot.GameDirectory, Path.Combine(snapshot.GameDirectory, ".save-editor-game-fixes", fix.Id), actual);
        if (_companion is not null && snapshot.CompanionInstalled)
        {
            var game = ToCompanionGame(snapshot.Target);
            foreach (var status in _companion.GetManagedFileStatus(game, snapshot.GameDirectory))
            {
                if (!status.Exists || !status.MatchesExpectedHash) throw new IOException($"Restored Companion file failed its provider hash check: {status.RelativePath}");
                AddObject("Companion", snapshot.GameDirectory, status.RelativePath, actual);
            }
            AddProviderDirectory("CompanionState", snapshot.GameDirectory, Path.Combine(snapshot.GameDirectory, ".save-editor-companion"), actual);
        }
        var expected = snapshot.Files.Where(file => file.Provider is "GameFix" or "GameFixState" or "Companion" or "CompanionState")
            .Select(file => (file.Provider, NormalizeRel(file.RelativePath), file.Sha256)).OrderBy(x => x.Provider).ThenBy(x => x.Item2).ToArray();
        var observed = actual.Select(file => (file.Provider, NormalizeRel(file.RelativePath), file.Sha256)).OrderBy(x => x.Provider).ThenBy(x => x.Item2).ToArray();
        if (!expected.SequenceEqual(observed))
        {
            var expectedMap = expected.ToDictionary(item => (item.Provider, item.Item2), item => item.Sha256);
            var observedMap = observed.ToDictionary(item => (item.Provider, item.Item2), item => item.Sha256);
            var firstDifference = expectedMap.Keys.Union(observedMap.Keys)
                .First(key => !expectedMap.TryGetValue(key, out var expectedHash) ||
                    !observedMap.TryGetValue(key, out var observedHash) || !string.Equals(expectedHash, observedHash, StringComparison.Ordinal));
            throw new IOException($"Provider replay differs from the saved managed-state hashes at {firstDifference.Provider}:{firstDifference.Item2}; the safety snapshot remains available.");
        }
        if (snapshot.UserLtxPath is not null)
        {
            var inspection = ManagedUserLtxSettings.Inspect(snapshot.UserLtxPath, _configStateDirectory);
            var expectedOverrides = snapshot.UserLtxOverrides ?? new Dictionary<string, string>();
            var actualOverrides = inspection.Settings.Where(setting => setting.ChangedByToolkit && setting.CurrentValue is not null)
                .ToDictionary(setting => setting.Key, setting => setting.CurrentValue!, StringComparer.OrdinalIgnoreCase);
            if (inspection.HasConflict || actualOverrides.Count != expectedOverrides.Count ||
                expectedOverrides.Any(pair => !actualOverrides.TryGetValue(pair.Key, out var value) || !string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Restored user.ltx overrides differ from the snapshot.");
            var expectedManifest = snapshot.Files.FirstOrDefault(file => file.Provider == "UserLtxState");
            var actualManifest = ManagedUserLtxSettings.ManagedManifestPath(snapshot.UserLtxPath, _configStateDirectory);
            if (expectedManifest is null)
            {
                if (actualManifest is not null) throw new IOException("The restored user.ltx still has a toolkit ownership record absent from the snapshot.");
            }
            else if (actualManifest is null || Hash(File.ReadAllBytes(actualManifest)) != expectedManifest.Sha256)
            {
                throw new IOException("The restored user.ltx ownership record differs from the snapshot.");
            }
        }
    }

    private void AddProviderDirectory(string provider, string gameRoot, string directory, List<ToolkitSnapshotFile> files)
    {
        if (!Directory.Exists(directory)) return;
        RejectLink(directory);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MaxRecursionDepth = 64,
        };
        foreach (var path in Directory.EnumerateFiles(directory, "*", options).Order(StringComparer.Ordinal))
        {
            RejectLink(path);
            var relative = Path.GetRelativePath(gameRoot, path).Replace('\\', '/');
            AddObject(provider, gameRoot, relative, files);
        }
    }

    private void AddObject(string provider, string gameRoot, string relative, List<ToolkitSnapshotFile> files)
    {
        var normalized = NormalizeRel(relative);
        var path = Path.GetFullPath(Path.Combine(gameRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(gameRoot, path);
        RejectLink(path);
        var bytes = File.ReadAllBytes(path);
        AddBytes(provider, normalized, bytes, files);
    }

    private void AddExternalObject(string provider, string path, string relative, List<ToolkitSnapshotFile> files)
    {
        RejectLink(path);
        AddBytes(provider, relative.Replace('\\', '/'), File.ReadAllBytes(path), files);
    }

    private void AddBytes(string provider, string relative, byte[] bytes, List<ToolkitSnapshotFile> files)
    {
        var hash = Hash(bytes);
        var objectPath = Path.Combine(ObjectDirectory, hash);
        RejectLink(ObjectDirectory);
        Directory.CreateDirectory(ObjectDirectory);
        RejectLink(ObjectDirectory);
        if (File.Exists(objectPath))
        {
            RejectLink(objectPath);
            if (Hash(File.ReadAllBytes(objectPath)) != hash) throw new InvalidDataException("A content-addressed snapshot object failed its hash check.");
        }
        else
        {
            WriteAtomically(objectPath, bytes);
        }
        files.Add(new ToolkitSnapshotFile(provider, NormalizeRel(relative), hash, bytes.LongLength));
    }

    private void Persist(ToolkitSnapshotInfo info)
    {
        RejectLink(SnapshotDirectory);
        var folder = Path.Combine(SnapshotDirectory, info.Id);
        RejectLink(folder);
        Directory.CreateDirectory(folder);
        WriteAtomically(Path.Combine(folder, "snapshot.json"),
            JsonSerializer.SerializeToUtf8Bytes(new SnapshotDocument(SchemaVersion, info), SnapshotJsonContext.Default.SnapshotDocument));
    }

    /// <summary>Objects already hashed in this session, valid while size and write time are unchanged.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, long WriteTicks)> VerifiedObjects = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="reuseSessionHash"/> is for listing: the store is content-addressed, so snapshots share objects, and
    /// hashing every object again for every snapshot on every refresh threw that benefit away. Inspect, restore and
    /// delete still hash each object.
    /// </summary>
    private ToolkitSnapshotInfo ReadSnapshot(string path, bool validateObjects, bool reuseSessionHash = false)
    {
        RejectLink(SnapshotDirectory);
        RejectLink(Path.GetDirectoryName(path)!);
        if (Directory.Exists(ObjectDirectory)) RejectLink(ObjectDirectory);
        var document = JsonSerializer.Deserialize(File.ReadAllBytes(path), SnapshotJsonContext.Default.SnapshotDocument)
            ?? throw new InvalidDataException("Snapshot metadata is empty.");
        if (document.SchemaVersion != SchemaVersion || document.Info is null || document.Info.Files is null || !Enum.IsDefined(document.Info.Target))
            throw new InvalidDataException("Unsupported or incomplete snapshot metadata.");
        var info = document.Info;
        ValidateId(info.Id);
        if (!PathEquals(path, SnapshotPath(info.Id))) throw new InvalidDataException("Snapshot id does not match its metadata path.");
        var distinct = new HashSet<(string Provider, string Path)>(new ProviderPathComparer());
        foreach (var file in info.Files)
        {
            if (file is null || file.Provider is not ("GameFix" or "GameFixState" or "Companion" or "CompanionState" or "UserLtxState"))
                throw new InvalidDataException("Snapshot contains an unsupported provider entry.");
            ValidateHash(file.Sha256);
            var relative = NormalizeRel(file.RelativePath);
            if (!distinct.Add((file.Provider, relative))) throw new InvalidDataException("Snapshot contains duplicate managed file entries.");
            if (validateObjects)
            {
                var objectPath = Path.Combine(ObjectDirectory, file.Sha256);
                RejectLink(objectPath);
                var objectInfo = new FileInfo(objectPath);
                if (!objectInfo.Exists || objectInfo.Length != file.Length)
                    throw new InvalidDataException($"Snapshot object {file.Sha256} is missing or corrupt.");
                var stamp = (objectInfo.Length, objectInfo.LastWriteTimeUtc.Ticks);
                if (reuseSessionHash && VerifiedObjects.TryGetValue(objectPath, out var known) && known == stamp) continue;
                using (var stream = new FileStream(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
                {
                    if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Snapshot object {file.Sha256} is missing or corrupt.");
                }
                VerifiedObjects[objectPath] = stamp;
            }
        }
        return info;
    }

    private void PruneObjects()
    {
        if (!Directory.Exists(SnapshotDirectory) || !Directory.Exists(ObjectDirectory)) return;
        RejectLink(SnapshotDirectory);
        RejectLink(ObjectDirectory);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in Directory.EnumerateDirectories(SnapshotDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            RejectLink(child);
            var path = Path.Combine(child, "snapshot.json");
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize(File.ReadAllBytes(path), SnapshotJsonContext.Default.SnapshotDocument);
                if (document?.Info is not null)
                    foreach (var file in document.Info.Files) if (file is not null) used.Add(file.Sha256);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // Keep objects referenced by unreadable metadata; only verified orphan objects are removed.
                return;
            }
        }
        foreach (var path in Directory.EnumerateFiles(ObjectDirectory))
        {
            RejectLink(path);
            if (!used.Contains(Path.GetFileName(path))) File.Delete(path);
        }
    }

    private List<string> TopologicalOrder(IEnumerable<string> ids)
    {
        var requested = ids.ToHashSet(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        while (result.Count < requested.Count)
        {
            var next = requested.Where(id => !emitted.Contains(id))
                .Where(id => _definitions.TryGetValue(id, out var definition) &&
                    definition.DependsOn.All(requested.Contains) && definition.DependsOn.All(emitted.Contains))
                .Order(StringComparer.Ordinal).FirstOrDefault();
            if (next is null) throw new InvalidDataException("Snapshot Game Fix dependencies contain a cycle or an unknown fix.");
            emitted.Add(next);
            result.Add(next);
        }
        return result;
    }

    private int DependencyDepth(string id) => !_definitions.TryGetValue(id, out var definition) ? 0 : 1 + (definition.DependsOn.Count == 0 ? 0 : definition.DependsOn.Max(DependencyDepth));

    private string SnapshotDirectory => Path.Combine(_root, "snapshots");
    private string ObjectDirectory => Path.Combine(_root, "objects");
    private string SnapshotPath(string id) => Path.Combine(SnapshotDirectory, id, "snapshot.json");

    private static string CanonicalDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(path));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static CompanionGame ToCompanionGame(GameTarget target) => target switch
    {
        GameTarget.ShadowOfChernobyl => CompanionGame.ShadowOfChernobyl,
        GameTarget.ClearSky => CompanionGame.ClearSky,
        GameTarget.CallOfPripyat => CompanionGame.CallOfPripyat,
        _ => throw new NotSupportedException("Companion snapshots are available for the original trilogy only."),
    };

    private static void ValidateId(string id)
    {
        if (id.Length != 32 || id.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("Invalid toolkit snapshot id.", nameof(id));
    }

    private static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("Snapshot contains an invalid content hash.");
    }

    private static string NormalizeRel(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        var slash = relative.Replace('\\', '/');
        if (slash.StartsWith('/') || Path.IsPathRooted(slash) || slash.Split('/').Any(segment => segment is "" or "." or ".." || segment.Contains(':')))
            throw new InvalidDataException("Snapshot contains a rooted or unsafe relative path.");
        return slash;
    }

    private static void EnsureInside(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Managed provider path escapes the selected game directory.");
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(left)), SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void RejectLink(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"Refusing to follow linked toolkit snapshot state: {path}");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteAtomically(string path, byte[] bytes) => Storage.AtomicFile.WriteAllBytes(path, bytes);

    private sealed record SnapshotDocument(int SchemaVersion, ToolkitSnapshotInfo Info);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(SnapshotDocument), TypeInfoPropertyName = "SnapshotDocument")]
    private partial class SnapshotJsonContext : JsonSerializerContext { }

    private sealed class ProviderPathComparer : IEqualityComparer<(string Provider, string Path)>
    {
        public bool Equals((string Provider, string Path) x, (string Provider, string Path) y) =>
            string.Equals(x.Provider, y.Provider, StringComparison.Ordinal) &&
            string.Equals(x.Path, y.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        public int GetHashCode((string Provider, string Path) value) => HashCode.Combine(value.Provider, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path) : StringComparer.Ordinal.GetHashCode(value.Path));
    }
}
