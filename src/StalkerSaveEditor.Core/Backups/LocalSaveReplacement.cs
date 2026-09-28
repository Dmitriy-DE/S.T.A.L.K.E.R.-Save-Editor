using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Backups;

public sealed record LocalSaveReplacementReceipt(
    string SourcePath,
    string BackupPath,
    string RecoveryPath,
    string JournalPath,
    string OutputSha256);

public sealed class LocalSaveReplacementException(
    string message,
    string backupPath,
    string recoveryPath,
    string journalPath,
    Exception innerException)
    : IOException(message, innerException)
{
    public string BackupPath { get; } = backupPath;

    public string RecoveryPath { get; } = recoveryPath;

    public string JournalPath { get; } = journalPath;
}

public static class LocalSaveReplacement
{
    /// <summary>
    /// Replaces a save with bytes from elsewhere (a Steam Cloud copy) through the same journaled backup,
    /// atomic replacement and read-back as an edit.
    /// </summary>
    public static LocalSaveReplacementReceipt ReplaceWithBytes(
        string sourcePath,
        ReadOnlySpan<byte> replacement,
        string backupDirectory,
        Action<ReadOnlyMemory<byte>> verifyReadback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var plan = new EditPlan(Sha256(File.ReadAllBytes(sourcePath)));
        return ReplaceLocal(sourcePath, new PreparedEdit(plan, replacement), backupDirectory, verifyReadback);
    }

    public static LocalSaveReplacementReceipt ReplaceLocal(
        string sourcePath,
        PreparedEdit prepared,
        string backupDirectory,
        Action<ReadOnlyMemory<byte>> verifyReadback,
        ILocalSaveFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentNullException.ThrowIfNull(verifyReadback);

        var files = fileSystem ?? new LocalSaveFileSystem();
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var sourceBytes = files.ReadAllBytes(sourceFullPath);
        var sourceSha256 = Sha256(sourceBytes);
        if (!string.Equals(sourceSha256, prepared.SourceSha256, StringComparison.Ordinal))
        {
            throw new IOException(
                $"Source changed since analysis: expected {prepared.SourceSha256}, found {sourceSha256}.");
        }

        var outputBytes = prepared.Data.ToArray();
        var outputSha256 = Sha256(outputBytes);
        if (!string.Equals(outputSha256, prepared.OutputSha256, StringComparison.Ordinal))
        {
            throw new IOException("Prepared output bytes do not match their recorded SHA256.");
        }

        var directory = Path.GetDirectoryName(sourceFullPath)
            ?? throw new IOException("Source save must have a parent directory.");
        var backupDirectoryFullPath = Path.GetFullPath(backupDirectory);
        if (IsSameOrNestedDirectory(directory, backupDirectoryFullPath))
        {
            throw new ArgumentException(
                "Backup directory must be outside the selected save directory.",
                nameof(backupDirectory));
        }

        Directory.CreateDirectory(backupDirectoryFullPath);
        var stem = Path.GetFileNameWithoutExtension(sourceFullPath);
        if (stem.Length > 64) stem = stem[..64];
        var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
        var prefix = $"{stem}_{stamp}_{token}";
        var backupPath = Path.Combine(backupDirectoryFullPath, $"{prefix}_ORIGINAL.sav");
        var recoveryPath = Path.Combine(backupDirectoryFullPath, $"{prefix}_EDITED.sav");
        var journalPath = Path.ChangeExtension(backupPath, ".json");
        var temporaryOutputPath = Path.Combine(directory, $".{Path.GetFileName(sourceFullPath)}.{Guid.NewGuid():N}.tmp");
        var temporaryJournalPath = Path.Combine(backupDirectoryFullPath, $".{Path.GetFileName(journalPath)}.{Guid.NewGuid():N}.tmp");
        var journal = CreateJournal(
            sourceFullPath,
            backupPath,
            recoveryPath,
            sourceSha256,
            outputSha256,
            prepared.Plan);
        var sourceReplaced = false;

        try
        {
            files.WriteNew(backupPath, sourceBytes);
            files.WriteNew(recoveryPath, outputBytes);
            files.WriteNew(journalPath, SerializeJournal(journal));

            files.WriteNew(temporaryOutputPath, outputBytes);

            var beforeReplace = files.ReadAllBytes(sourceFullPath);
            var beforeReplaceSha256 = Sha256(beforeReplace);
            if (!string.Equals(beforeReplaceSha256, prepared.SourceSha256, StringComparison.Ordinal))
            {
                throw new IOException(
                    $"Source changed before replacement: expected {prepared.SourceSha256}, found {beforeReplaceSha256}.");
            }

            files.Replace(temporaryOutputPath, sourceFullPath);
            sourceReplaced = true;

            var readBack = files.ReadAllBytes(sourceFullPath);
            if (!string.Equals(Sha256(readBack), outputSha256, StringComparison.Ordinal) ||
                !readBack.AsSpan().SequenceEqual(outputBytes))
            {
                throw new IOException("Replaced save read-back did not match the prepared output bytes.");
            }

            verifyReadback(readBack);
            journal["status"] = "verified";
            files.WriteNew(temporaryJournalPath, SerializeJournal(journal));
            files.Replace(temporaryJournalPath, journalPath);

            return new LocalSaveReplacementReceipt(
                sourceFullPath,
                backupPath,
                recoveryPath,
                journalPath,
                outputSha256);
        }
        catch (Exception exception) when (sourceReplaced)
        {
            throw new LocalSaveReplacementException(
                $"The save was replaced, but read-back verification did not complete. " +
                $"Original backup: {backupPath}; edited recovery: {recoveryPath}; journal: {journalPath}.",
                backupPath,
                recoveryPath,
                journalPath,
                exception);
        }
        finally
        {
            TryDelete(files, temporaryOutputPath);
            TryDelete(files, temporaryJournalPath);
        }
    }

    private static Dictionary<string, object?> CreateJournal(
        string sourcePath,
        string backupPath,
        string recoveryPath,
        string sourceSha256,
        string outputSha256,
        EditPlan plan) => new(StringComparer.Ordinal)
        {
            ["version"] = 1,
            ["status"] = "prepared",
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["source_path"] = sourcePath,
            ["source_sha256"] = sourceSha256,
            ["output_path"] = sourcePath,
            ["output_sha256"] = outputSha256,
            ["backup_path"] = backupPath,
            ["recovery_path"] = recoveryPath,
            ["operation"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = "replace",
                ["money"] = plan.Money,
                ["stack_count"] = plan.StackCounts.Count,
            },
        };

    private static byte[] SerializeJournal(Dictionary<string, object?> journal) =>
        JsonSerializer.SerializeToUtf8Bytes(journal, BackupJournalJsonContext.Default.Journal);

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static bool IsSameOrNestedDirectory(string parentDirectory, string candidateDirectory)
    {
        var relativePath = Path.GetRelativePath(parentDirectory, candidateDirectory);
        return relativePath == "." ||
            (!Path.IsPathRooted(relativePath) && relativePath != ".." &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static void TryDelete(ILocalSaveFileSystem files, string path)
    {
        try
        {
            files.DeleteIfExists(path);
        }
        catch (IOException)
        {
            // A leftover temporary file must not hide the transaction result.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temporary file must not hide the transaction result.
        }
    }

    private sealed class LocalSaveFileSystem : ILocalSaveFileSystem
    {
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void WriteNew(string path, byte[] data)
        {
            var created = false;
            try
            {
                using var stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                });
                created = true;
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                if (created) DeleteIfExists(path);
                throw;
            }
        }

        public void Replace(string sourcePath, string destinationPath) =>
            File.Move(sourcePath, destinationPath, overwrite: true);

        public void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
