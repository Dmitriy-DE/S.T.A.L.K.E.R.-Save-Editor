using System.Text;
using System.Text.Json;

namespace StalkerSaveEditor.Core.Patching;

public sealed partial class GameFixEngine
{
    /// <summary>Lists which original copy belongs to which game file after a rollback that did not complete.</summary>
    private string WriteRecoveryNote(string fixDirectory, string fixId, IReadOnlyList<PreparedFileChange> changes,
        IReadOnlyList<PreparedFileChange> applied, IReadOnlyList<string> errors)
    {
        try
        {
            var lines = new List<string>
            {
                "Game Fix " + fixId + ": the installation failed and the game files could not all be restored.",
                "Copy each original file back over the game file, then delete this folder.",
                string.Empty,
            };
            for (var index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                var state = applied.Contains(change) ? "may be changed" : "not touched";
                lines.Add(change.TargetExistedBefore
                    ? $"backups/file-{index:D4}.before -> {change.RelativePath} ({state}, original sha256 {change.BeforeSha256})"
                    : $"{change.RelativePath} ({state}): did not exist before; delete it if present");
            }
            lines.Add(string.Empty);
            lines.AddRange(errors.Select(error => "problem: " + error));
            var notePath = Path.Combine(fixDirectory, RecoveryNoteFileName);
            _fileSystem.WriteAllBytes(notePath, Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"));
            return "; see " + notePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static ManagedGameFile? PriorFile(GameFixManifest? priorManifest, PreparedFileChange change) =>
        priorManifest?.Files.FirstOrDefault(file => PathComparer.Equals(NormalizeRelativePath(file.RelativePath), change.RelativePath));

    private void WriteJournal(string fixDirectory, GameFixJournal journal)
    {
        var path = Path.Combine(fixDirectory, JournalFileName);
        AtomicGameFileWriter.Write(_fileSystem, path, JsonSerializer.SerializeToUtf8Bytes(journal, GameFixJsonContext.Default.GameFixJournal),
            overwrite: _fileSystem.FileExists(path));
    }

    private void DeleteJournal(string fixDirectory)
    {
        var path = Path.Combine(fixDirectory, JournalFileName);
        if (_fileSystem.FileExists(path)) _fileSystem.DeleteFile(path);
    }

    /// <summary>
    /// Finishes every fix transaction a killed process or a power loss left half-done in this game folder. Both an
    /// interrupted installation and an interrupted removal end the same way: the original game files are back.
    /// Returns the fix ids that were repaired; throws when a file can no longer be restored safely.
    /// </summary>
    public IReadOnlyList<string> RecoverInterrupted(string gameDirectory)
    {
        var root = NormalizeRoot(gameDirectory);
        var stateDirectory = Path.Combine(root, StateDirectoryName);
        if (!_fileSystem.DirectoryExists(stateDirectory)) return [];
        CheckExistingPathForLinks(root, stateDirectory);
        var recovered = new List<string>();
        foreach (var directory in _fileSystem.EnumerateDirectories(stateDirectory, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray())
        {
            var id = Path.GetFileName(directory);
            if (IdPattern.IsMatch(id) && FinishInterruptedTransaction(root, id)) recovered.Add(id);
        }
        return recovered;
    }

    private bool FinishInterruptedTransaction(string root, string fixId)
    {
        var fixDirectory = GetFixDirectory(root, fixId);
        var journalPath = Path.Combine(fixDirectory, JournalFileName);
        if (!_fileSystem.FileExists(journalPath)) return false;
        CheckExistingPathForLinks(root, journalPath);
        GameFixJournal journal;
        try
        {
            journal = JsonSerializer.Deserialize(_fileSystem.ReadAllBytes(journalPath), GameFixJsonContext.Default.GameFixJournal)
                ?? throw new JsonException("empty journal");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Game Fix transaction journal is unreadable; restore the game files manually: " + journalPath, exception);
        }
        if (journal.SchemaVersion != JournalSchemaVersion || journal.Kind is not (JournalInstall or JournalUninstall) || journal.Files is null)
            throw new InvalidDataException("The Game Fix transaction journal has an unknown format: " + journalPath);

        var manifestPath = GetManifestPath(root, fixId);
        var manifest = _fileSystem.FileExists(manifestPath) ? ReadManifest(manifestPath, fixId) : null;
        // The manifest is written last, so its state tells whether the interrupted operation had already finished.
        var committed = journal.Kind == JournalInstall ? manifest is { Installed: true } : manifest is { Installed: false };
        if (committed)
        {
            DeleteJournal(fixDirectory);
            return false;
        }

        var errors = new List<string>();
        foreach (var file in journal.Files)
        {
            try
            {
                var relative = NormalizeRelativePath(file.RelativePath);
                var path = ResolveGamePath(root, relative);
                CheckExistingPathForLinks(root, path);
                if (!_fileSystem.FileExists(path))
                {
                    if (!file.TargetExistedBefore) continue;
                    errors.Add("file is missing: " + relative);
                    continue;
                }
                var current = Hash(_fileSystem.ReadAllBytes(path));
                if (file.TargetExistedBefore && string.Equals(current, file.BeforeSha256, StringComparison.Ordinal)) continue;
                if (!string.Equals(current, file.AfterSha256, StringComparison.Ordinal))
                {
                    errors.Add("file was changed by something else: " + relative);
                    continue;
                }
                if (!file.TargetExistedBefore)
                {
                    _fileSystem.DeleteFile(path);
                    continue;
                }
                var backupPath = ResolveStatePath(fixDirectory, file.BackupPath);
                if (!_fileSystem.FileExists(backupPath))
                {
                    errors.Add("recovery copy is missing: " + relative);
                    continue;
                }
                var before = _fileSystem.ReadAllBytes(backupPath);
                if (!string.Equals(Hash(before), file.BeforeSha256, StringComparison.Ordinal))
                {
                    errors.Add("recovery copy failed its hash check: " + relative);
                    continue;
                }
                AtomicGameFileWriter.Write(_fileSystem, path, before, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                errors.Add(file.RelativePath + ": " + exception.Message);
            }
        }
        if (errors.Count > 0)
            throw new IOException(
                $"An earlier Game Fix operation on {fixId} was interrupted and could not be undone ({string.Join("; ", errors)}). " +
                $"The original files are kept in {Path.Combine(fixDirectory, "backups")}.");

        if (journal.Kind == JournalUninstall)
        {
            AtomicGameFileWriter.Write(_fileSystem, manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(manifest! with { Installed = false }, GameFixJsonContext.Default.GameFixManifest), overwrite: true);
            DeleteJournal(fixDirectory);
            return true;
        }

        if (journal.FreshState)
        {
            foreach (var file in journal.Files)
            {
                var backupPath = ResolveStatePath(fixDirectory, file.BackupPath);
                if (_fileSystem.FileExists(backupPath)) _fileSystem.DeleteFile(backupPath);
            }
            var notePath = Path.Combine(fixDirectory, RecoveryNoteFileName);
            if (_fileSystem.FileExists(notePath)) _fileSystem.DeleteFile(notePath);
        }
        DeleteJournal(fixDirectory);
        if (journal.FreshState) DeleteEmptyStateTree(fixDirectory);
        return true;
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

                // An existing file was replaced by the uninstall and is verified above, so it may be replaced back;
                // a removed overlay must not exist any more, so it is created exclusively.
                AtomicGameFileWriter.Write(_fileSystem, change.AbsolutePath, change.BeforeBytes, overwrite: change.TargetExistedBefore);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                errors.Add(change.RelativePath + ": " + exception.Message);
            }
        }

        return errors;
    }
}
