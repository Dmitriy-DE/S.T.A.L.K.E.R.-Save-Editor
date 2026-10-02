using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

public sealed partial class GameFixEngine
{
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

    /// <param name="issues">Null: any unreadable state folder is an error. A list: it is recorded there and skipped.</param>
    private List<GameFixManifest> ReadActiveManifests(string root, List<string>? issues = null)
    {
        var directory = Path.Combine(root, StateDirectoryName);
        if (!_fileSystem.DirectoryExists(directory)) return [];
        CheckExistingPathForLinks(root, directory);
        var result = new List<GameFixManifest>();
        foreach (var fixDirectory in _fileSystem.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                CheckExistingPathForLinks(root, fixDirectory);
                var manifestPath = Path.Combine(fixDirectory, ManifestFileName);
                CheckExistingPathForLinks(root, manifestPath);
                if (!_fileSystem.FileExists(manifestPath))
                {
                    // An installation that was killed before its manifest: nothing is installed, the next operation undoes it.
                    if (_fileSystem.FileExists(Path.Combine(fixDirectory, JournalFileName))) continue;
                    throw new InvalidDataException("Game Fix state directory is missing a manifest: " + Path.GetFileName(fixDirectory));
                }
                var manifest = ReadManifest(manifestPath, Path.GetFileName(fixDirectory), _fileSystem);
                if (manifest.Installed) result.Add(manifest);
            }
            catch (Exception exception) when (issues is not null && exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                issues.Add(Path.GetFileName(fixDirectory) + ": " + exception.Message);
            }
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
        if (definition.Implementation is not (GameFixImplementationType.ExactTextReplacement or GameFixImplementationType.Overlay or GameFixImplementationType.Structured))
            throw new NotSupportedException("This engine implements exact text replacements, whole-file overlays and structural all.spawn edits only.");
        if (definition.TextPatches is null)
            throw new ArgumentException("Operation lists cannot be null.", nameof(definition));
        var kinds = (definition.TextPatches.Count > 0 ? 1 : 0) + (definition.Overlays.Count > 0 ? 1 : 0) + (definition.SpawnEdits.Count > 0 ? 1 : 0);
        var matches = definition.Implementation switch
        {
            GameFixImplementationType.ExactTextReplacement => definition.TextPatches.Count > 0,
            GameFixImplementationType.Overlay => definition.Overlays.Count > 0,
            _ => definition.SpawnEdits.Count > 0,
        };
        if (kinds != 1 || !matches)
            throw new ArgumentException("A fix has exactly one kind of operation (text patches, whole-file overlays or all.spawn edits) matching its implementation.", nameof(definition));
        foreach (var edit in definition.SpawnEdits)
        {
            if (edit is null) throw new ArgumentException("all.spawn edits cannot be null.", nameof(definition));
            var normalized = NormalizeRelativePath(edit.RelativePath);
            if (!normalized.EndsWith("all.spawn", StringComparison.OrdinalIgnoreCase) || !Enum.IsDefined(edit.Kind) || string.IsNullOrEmpty(edit.Target))
                throw new ArgumentException("all.spawn edits need an all.spawn path, a known kind and a target.", nameof(definition));
            if (edit.ExpectedFileSha256 is not null && !IsSha256(edit.ExpectedFileSha256))
                throw new ArgumentException("Expected source-file SHA-256 values must be lowercase hexadecimal.", nameof(definition));
        }
        foreach (var overlay in definition.Overlays)
        {
            if (overlay is null) throw new ArgumentException("Overlay operations cannot be null.", nameof(definition));
            _ = NormalizeRelativePath(overlay.RelativePath);
            if (!IsSha256(overlay.ContentSha256) || overlay.ExpectedFileSha256 is { } expected && !IsSha256(expected))
                throw new ArgumentException("Overlay SHA-256 values must be lowercase hexadecimal.", nameof(definition));
        }
        if (definition.Overlays.Select(overlay => NormalizeRelativePath(overlay.RelativePath)).Distinct(PathComparer).Count() != definition.Overlays.Count)
            throw new ArgumentException("An overlay path appears more than once.", nameof(definition));
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
}
