using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Companion;

public sealed partial class CompanionInstaller
{
    private static string JournalPath(string gameDirectory) =>
        Path.Combine(gameDirectory, ManifestDirectoryName, JournalFileName);

    private static string PreImagePath(string gameDirectory, int index) =>
        Path.Combine(gameDirectory, ManifestDirectoryName, JournalDirectoryName, $"{index:D4}.pre");

    /// <summary>
    /// Records, durably and before the first game file is touched, what every file looks like now and what it will
    /// become, and keeps a copy of each file about to be replaced or deleted. An exception is rolled back in-process;
    /// this is for a killed process or a power loss, which the next install or removal undoes from the journal.
    /// </summary>
    private void BeginJournal(
        string gameDirectory,
        InstallManifest manifest,
        byte[] manifestBytes,
        IReadOnlyList<PlannedInstallFile> plan,
        IReadOnlyList<InstallFileManifest> staleFiles,
        bool stateDirectoryExisted)
    {
        var entries = new List<InstallJournalEntry>();
        var preImages = new List<(string Path, byte[] Bytes)>();
        void Add(string relativePath, string? afterSha256)
        {
            var target = ResolveGamePath(gameDirectory, relativePath);
            var existing = _fileSystem.FileExists(target) ? _fileSystem.ReadAllBytes(target) : null;
            var existingSha = existing is null ? null : Sha256(existing);
            if (string.Equals(existingSha, afterSha256, StringComparison.Ordinal)) return;
            if (existing is not null) preImages.Add((PreImagePath(gameDirectory, entries.Count), existing));
            entries.Add(new InstallJournalEntry(relativePath, existingSha, afterSha256, existing is null ? null : entries.Count));
        }

        foreach (var file in plan) Add(file.RelativePath, Sha256(file.Bytes));
        foreach (var stale in staleFiles) Add(NormalizeRelative(stale.Path), null);
        var newBackups = manifest.Files
            .Where(file => file.BackupPath is not null && !FileExistsInState(gameDirectory, file.BackupPath))
            .Select(file => file.BackupPath!)
            .ToList();
        var journal = new InstallJournal(JournalSchemaVersion, Sha256(manifestBytes), stateDirectoryExisted,
            !stateDirectoryExisted && manifest.GameDataCreatedFromScratch, entries, newBackups);
        EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, JournalFileName));
        var journalPath = JournalPath(gameDirectory);
        AtomicWrite(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal, CompanionManifestJsonContext.Default.InstallJournal),
            overwrite: _fileSystem.FileExists(journalPath));
        foreach (var (path, bytes) in preImages)
        {
            EnsureSafeWritePath(gameDirectory, Path.GetRelativePath(gameDirectory, path));
            AtomicWrite(path, bytes, overwrite: _fileSystem.FileExists(path));
        }
    }

    private void EndJournal(string gameDirectory)
    {
        var journalPath = JournalPath(gameDirectory);
        var directory = Path.Combine(gameDirectory, ManifestDirectoryName, JournalDirectoryName);
        // The journal goes first: without it the leftover copies mean nothing and are only tidied up.
        if (_fileSystem.FileExists(journalPath)) _fileSystem.DeleteFile(journalPath);
        if (!_fileSystem.DirectoryExists(directory)) return;
        CheckNotLink(directory);
        foreach (var file in _fileSystem.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            _fileSystem.DeleteFile(file);
        }
        _fileSystem.DeleteDirectory(directory, recursive: false);
    }

    /// <summary>
    /// Undoes an installation that a killed process or a power loss left half-done: every game file goes back to what
    /// it was before that installation started. A file that is neither the old nor the new version is left alone and
    /// reported. Returns true when something was undone.
    /// </summary>
    private bool FinishInterruptedInstall(string gameDirectory)
    {
        var journalPath = JournalPath(gameDirectory);
        var transactionDirectory = Path.Combine(gameDirectory, ManifestDirectoryName, JournalDirectoryName);
        if (!_fileSystem.FileExists(journalPath))
        {
            if (_fileSystem.DirectoryExists(transactionDirectory)) EndJournal(gameDirectory);
            return false;
        }

        EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, JournalFileName));
        InstallJournal journal;
        try
        {
            journal = JsonSerializer.Deserialize(_fileSystem.ReadAllBytes(journalPath), CompanionManifestJsonContext.Default.InstallJournal)
                ?? throw new JsonException("empty journal");
        }
        catch (JsonException exception)
        {
            throw new CompanionInstallerException($"The companion installation journal is unreadable: {journalPath}", journalPath, exception);
        }
        if (journal.SchemaVersion != JournalSchemaVersion || journal.Entries is null || journal.NewBackups is null)
        {
            throw new CompanionInstallerException($"The companion installation journal has an unknown format: {journalPath}", journalPath);
        }

        var manifestPath = GetManifestPath(gameDirectory);
        if (_fileSystem.FileExists(manifestPath) &&
            string.Equals(Sha256(_fileSystem.ReadAllBytes(manifestPath)), journal.ManifestSha256, StringComparison.Ordinal))
        {
            // The manifest is written last, so the installation had finished; only the clean-up was cut short.
            EndJournal(gameDirectory);
            return false;
        }

        var problems = new List<string>();
        foreach (var entry in journal.Entries.AsEnumerable().Reverse())
        {
            var relative = NormalizeRelative(entry.Path);
            var target = ResolveGamePath(gameDirectory, relative);
            EnsureSafeWritePath(gameDirectory, relative);
            var current = _fileSystem.FileExists(target) ? Sha256(_fileSystem.ReadAllBytes(target)) : null;
            if (string.Equals(current, entry.BeforeSha256, StringComparison.Ordinal)) continue;
            if (!string.Equals(current, entry.AfterSha256, StringComparison.Ordinal))
            {
                problems.Add($"{relative}: changed by something else");
                continue;
            }
            if (entry.BeforeSha256 is null)
            {
                _fileSystem.DeleteFile(target);
                continue;
            }
            var preImage = entry.PreImage is { } index ? PreImagePath(gameDirectory, index) : null;
            if (preImage is null || !_fileSystem.FileExists(preImage))
            {
                problems.Add($"{relative}: the saved copy is missing");
                continue;
            }
            var before = _fileSystem.ReadAllBytes(preImage);
            if (!string.Equals(Sha256(before), entry.BeforeSha256, StringComparison.Ordinal))
            {
                problems.Add($"{relative}: the saved copy is damaged");
                continue;
            }
            AtomicWrite(target, before, overwrite: current is not null);
        }
        if (problems.Count > 0)
        {
            throw new CompanionInstallerException(
                $"An earlier companion installation was interrupted and could not be undone: {string.Join("; ", problems)}. " +
                $"The previous files are kept in {transactionDirectory}.", transactionDirectory);
        }

        foreach (var backup in journal.NewBackups)
        {
            var backupPath = ResolveStatePath(gameDirectory, backup);
            EnsureSafeWritePath(gameDirectory, Path.Combine(ManifestDirectoryName, backup));
            if (_fileSystem.FileExists(backupPath)) _fileSystem.DeleteFile(backupPath);
        }
        EndJournal(gameDirectory);
        if (!journal.StateDirectoryExisted) DeleteEmptyDirectoryTree(Path.GetDirectoryName(manifestPath)!);
        else DeleteEmptyDirectoryTree(Path.Combine(gameDirectory, ManifestDirectoryName, "backups"));
        var gameData = Path.Combine(gameDirectory, "gamedata");
        if (journal.GameDataCreated && _fileSystem.DirectoryExists(gameData) &&
            !_fileSystem.EnumerateFiles(gameData, "*", SearchOption.AllDirectories).Any())
        {
            // The interrupted install created gamedata itself; only its empty folders are left.
            DeleteEmptyDirectoryTree(gameData);
        }
        return true;
    }

    private ReadOnlyCollection<string> RollbackInstall(
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
}
