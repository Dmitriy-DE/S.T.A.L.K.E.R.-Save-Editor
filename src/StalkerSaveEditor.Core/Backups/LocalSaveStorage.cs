using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Backups;

public enum BackupVerificationStatus
{
    Verified,
    Missing,
    Corrupt,
}

public sealed record LocalSaveExportReceipt(
    string OutputPath,
    string BackupPath,
    string JournalPath,
    string OutputSha256);

public sealed record LocalSaveBackupRecord(
    string JournalPath,
    string BackupPath,
    string CreatedAt,
    string SourcePath,
    string SourceSha256,
    string? OutputPath,
    string? OutputSha256,
    JsonElement Operation,
    BackupVerificationStatus Status,
    string? ActualSha256 = null,
    string? Error = null);

public sealed record LocalSaveRestoreReceipt(
    string OutputPath,
    string BackupPath,
    string OutputSha256,
    string? SafetyBackupPath = null);

public static class LocalSaveStorage
{
    public static LocalSaveExportReceipt ExportLocal(
        string sourcePath,
        string outputPath,
        PreparedEdit prepared,
        string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var backupDirectoryFullPath = Path.GetFullPath(backupDirectory);
        if (SamePath(sourceFullPath, outputFullPath))
        {
            throw new IOException("Export output cannot be the selected source save.");
        }

        if (File.Exists(outputFullPath) || Directory.Exists(outputFullPath))
        {
            throw new IOException($"Export output already exists: {outputFullPath}.");
        }

        var sourceDirectory = Path.GetDirectoryName(sourceFullPath)
            ?? throw new IOException("Source save must have a parent directory.");
        ValidateBackupDirectory(sourceDirectory, backupDirectoryFullPath);
        var outputDirectory = Path.GetDirectoryName(outputFullPath)
            ?? throw new IOException("Export output must have a parent directory.");
        if (!Directory.Exists(outputDirectory))
        {
            throw new IOException($"Export output directory does not exist: {outputDirectory}.");
        }

        var sourceBytes = ReadValidatedSource(sourceFullPath, prepared);
        var outputBytes = prepared.Data.ToArray();
        var outputSha256 = Sha256(outputBytes);
        if (!string.Equals(outputSha256, prepared.OutputSha256, StringComparison.Ordinal))
        {
            throw new IOException("Prepared output bytes do not match their recorded SHA256.");
        }

        Directory.CreateDirectory(backupDirectoryFullPath);
        var (backupPath, journalPath) = CreateArtifactPaths(sourceFullPath, backupDirectoryFullPath);
        var temporaryOutputPath = TemporaryPath(outputDirectory, Path.GetFileName(outputFullPath));
        var temporaryJournalPath = TemporaryPath(backupDirectoryFullPath, Path.GetFileName(journalPath));
        var sourceSha256 = Sha256(sourceBytes);
        var journal = CreateJournal(
            "prepared",
            sourceFullPath,
            sourceSha256,
            outputFullPath,
            outputSha256,
            backupPath,
            OperationSummary(prepared.Plan, "export"));
        try
        {
            WriteNew(backupPath, sourceBytes);
            WriteTemporary(temporaryOutputPath, outputBytes);
            WriteNew(journalPath, SerializeJournal(journal));

            var currentSourceBytes = File.ReadAllBytes(sourceFullPath);
            if (!string.Equals(Sha256(currentSourceBytes), prepared.SourceSha256, StringComparison.Ordinal))
            {
                throw new IOException("Source changed immediately before export publication.");
            }

            File.Move(temporaryOutputPath, outputFullPath, overwrite: false);
            VerifyFile(outputFullPath, outputBytes, outputSha256, "Export output");
            journal["status"] = "verified";
            WriteTemporary(temporaryJournalPath, SerializeJournal(journal));
            File.Move(temporaryJournalPath, journalPath, overwrite: true);
            return new LocalSaveExportReceipt(outputFullPath, backupPath, journalPath, outputSha256);
        }
        finally
        {
            DeleteIfExists(temporaryOutputPath);
            DeleteIfExists(temporaryJournalPath);
        }
    }

    public static LocalSaveBackupRecord InspectBackup(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        var fullJournalPath = Path.GetFullPath(journalPath);
        var fallbackBackupPath = Path.ChangeExtension(fullJournalPath, ".sav");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(fullJournalPath));
        }
        catch (FileNotFoundException)
        {
            return InvalidRecord(fullJournalPath, fallbackBackupPath, "Journal is missing.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return InvalidRecord(fullJournalPath, fallbackBackupPath, $"Journal cannot be read: {exception.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return InvalidRecord(fullJournalPath, fallbackBackupPath, "Journal must contain a JSON object.");
            }

            var root = document.RootElement;
            try
            {
                if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 1)
                {
                    throw new InvalidDataException("Unsupported journal version.");
                }

                var createdAt = RequiredString(root, "created_at");
                var sourcePath = RequiredString(root, "source_path");
                var sourceSha256 = RequiredSha256(root, "source_sha256");
                var outputPath = RequiredString(root, "output_path");
                var outputSha256 = RequiredSha256(root, "output_sha256");
                var backupPathValue = RequiredString(root, "backup_path");
                if (!root.TryGetProperty("status", out var status) ||
                    status.ValueKind != JsonValueKind.String ||
                    !string.Equals(status.GetString(), "verified", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Journal status is not verified.");
                }

                if (!root.TryGetProperty("operation", out var operation) ||
                    operation.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Journal operation must be an object.");
                }

                var backupPath = Path.GetFullPath(
                    Path.IsPathRooted(backupPathValue)
                        ? backupPathValue
                        : Path.Combine(Path.GetDirectoryName(fullJournalPath)!, backupPathValue));
                byte[] backupBytes;
                try
                {
                    backupBytes = File.ReadAllBytes(backupPath);
                }
                catch (FileNotFoundException)
                {
                    return new LocalSaveBackupRecord(
                        fullJournalPath,
                        backupPath,
                        createdAt,
                        sourcePath,
                        sourceSha256,
                        outputPath,
                        outputSha256,
                        operation.Clone(),
                        BackupVerificationStatus.Missing,
                        Error: "Backup file is missing.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return new LocalSaveBackupRecord(
                        fullJournalPath,
                        backupPath,
                        createdAt,
                        sourcePath,
                        sourceSha256,
                        outputPath,
                        outputSha256,
                        operation.Clone(),
                        BackupVerificationStatus.Corrupt,
                        Error: $"Backup cannot be read: {exception.Message}");
                }

                var actualSha256 = Sha256(backupBytes);
                if (!string.Equals(actualSha256, sourceSha256, StringComparison.Ordinal))
                {
                    return new LocalSaveBackupRecord(
                        fullJournalPath,
                        backupPath,
                        createdAt,
                        sourcePath,
                        sourceSha256,
                        outputPath,
                        outputSha256,
                        operation.Clone(),
                        BackupVerificationStatus.Corrupt,
                        actualSha256,
                        "Backup SHA256 does not match the journal.");
                }

                return new LocalSaveBackupRecord(
                    fullJournalPath,
                    backupPath,
                    createdAt,
                    sourcePath,
                    sourceSha256,
                    outputPath,
                    outputSha256,
                    operation.Clone(),
                    BackupVerificationStatus.Verified,
                    actualSha256);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException or FormatException)
            {
                return InvalidRecord(fullJournalPath, fallbackBackupPath, $"Journal is corrupt: {exception.Message}");
            }
        }
    }

    public static IReadOnlyList<LocalSaveBackupRecord> ListBackups(IEnumerable<string> backupDirectories)
    {
        ArgumentNullException.ThrowIfNull(backupDirectories);
        var comparer = PathComparer;
        var directories = new List<string>();
        var directoryKeys = new HashSet<string>(comparer);
        foreach (var directory in backupDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(directory);
            if (directoryKeys.Add(fullPath))
            {
                directories.Add(fullPath);
            }
        }

        var records = new List<LocalSaveBackupRecord>();
        var journalKeys = new HashSet<string>(comparer);
        var knownBackupKeys = new HashSet<string>(comparer);
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var journalPath in SafeEnumerate(directory, "*.json"))
            {
                if (!journalKeys.Add(journalPath))
                {
                    continue;
                }

                var record = InspectBackup(journalPath);
                records.Add(record);
                knownBackupKeys.Add(Path.GetFullPath(record.BackupPath));
            }
        }

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var backupPath in SafeEnumerate(directory, "*_ORIGINAL.sav"))
            {
                if (knownBackupKeys.Contains(Path.GetFullPath(backupPath)))
                {
                    continue;
                }

                records.Add(InvalidRecord(
                    Path.ChangeExtension(backupPath, ".json"),
                    backupPath,
                    "Journal for this backup is missing."));
            }
        }

        records.Sort(static (left, right) =>
        {
            var byCreated = string.Compare(right.CreatedAt, left.CreatedAt, StringComparison.Ordinal);
            return byCreated != 0 ? byCreated : PathComparer.Compare(right.JournalPath, left.JournalPath);
        });
        return records.AsReadOnly();
    }

    public static LocalSaveRestoreReceipt RestoreBackup(string journalPath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var record = RequireVerified(InspectBackup(journalPath));
        var outputFullPath = Path.GetFullPath(outputPath);
        if (SamePath(record.BackupPath, outputFullPath))
        {
            throw new IOException("Restore output cannot be the backup file.");
        }

        if (File.Exists(outputFullPath) || Directory.Exists(outputFullPath))
        {
            throw new IOException($"Restore output already exists: {outputFullPath}.");
        }

        var outputDirectory = Path.GetDirectoryName(outputFullPath)
            ?? throw new IOException("Restore output must have a parent directory.");
        if (!Directory.Exists(outputDirectory))
        {
            throw new IOException($"Restore output directory does not exist: {outputDirectory}.");
        }

        var bytes = ReadVerifiedBackup(record);
        var temporaryPath = TemporaryPath(outputDirectory, Path.GetFileName(outputFullPath));
        var published = false;
        try
        {
            WriteTemporary(temporaryPath, bytes);
            File.Move(temporaryPath, outputFullPath, overwrite: false);
            published = true;
            VerifyFile(outputFullPath, bytes, record.SourceSha256, "Restored output");
            return new LocalSaveRestoreReceipt(outputFullPath, record.BackupPath, record.SourceSha256);
        }
        finally
        {
            if (!published)
            {
                DeleteIfExists(temporaryPath);
            }
        }
    }

    public static LocalSaveRestoreReceipt RestoreInPlace(string journalPath)
    {
        var record = RequireVerified(InspectBackup(journalPath));
        var operation = record.Operation;
        if (!operation.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String ||
            mode.GetString() is not ("replace" or "restore"))
        {
            throw new IOException("Journal does not describe an in-place save replacement.");
        }

        var sourceFullPath = Path.GetFullPath(record.SourcePath);
        if (record.OutputPath is null || !SamePath(sourceFullPath, record.OutputPath))
        {
            throw new IOException("Journal output path does not match its source save.");
        }

        if (IsSymbolicLink(sourceFullPath))
        {
            throw new IOException("Restoring a symbolic-link save is refused.");
        }

        var sourceDirectory = Path.GetDirectoryName(sourceFullPath)
            ?? throw new IOException("Source save must have a parent directory.");
        if (!Directory.Exists(sourceDirectory))
        {
            throw new IOException($"Source save directory does not exist: {sourceDirectory}.");
        }

        var sourceBytes = ReadVerifiedBackup(record);
        if (!File.Exists(sourceFullPath))
        {
            var restoredMissing = RestoreBackup(record.JournalPath, sourceFullPath);
            return restoredMissing;
        }

        var currentBytes = File.ReadAllBytes(sourceFullPath);
        var currentSha256 = Sha256(currentBytes);
        if (record.OutputSha256 is null || !string.Equals(currentSha256, record.OutputSha256, StringComparison.Ordinal))
        {
            throw new IOException("Current save changed after the journaled replacement; refusing to overwrite it.");
        }

        var backupDirectory = Path.GetDirectoryName(record.JournalPath)
            ?? throw new IOException("Backup journal must have a parent directory.");
        ValidateBackupDirectory(sourceDirectory, backupDirectory);
        var (safetyBackupPath, safetyJournalPath) = CreateArtifactPaths(sourceFullPath, backupDirectory);
        var temporaryPath = TemporaryPath(sourceDirectory, Path.GetFileName(sourceFullPath));
        var temporaryJournalPath = TemporaryPath(backupDirectory, Path.GetFileName(safetyJournalPath));
        var safetyJournal = CreateJournal(
            "prepared",
            sourceFullPath,
            currentSha256,
            sourceFullPath,
            record.SourceSha256,
            safetyBackupPath,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = "restore",
                ["restore_from"] = record.BackupPath,
            });
        try
        {
            WriteNew(safetyBackupPath, currentBytes);
            WriteTemporary(temporaryPath, sourceBytes);
            WriteNew(safetyJournalPath, SerializeJournal(safetyJournal));

            var freshBytes = File.ReadAllBytes(sourceFullPath);
            if (!string.Equals(Sha256(freshBytes), currentSha256, StringComparison.Ordinal))
            {
                throw new IOException("Current save changed immediately before in-place restore.");
            }

            File.Move(temporaryPath, sourceFullPath, overwrite: true);
            VerifyFile(sourceFullPath, sourceBytes, record.SourceSha256, "Restored save");
            safetyJournal["status"] = "verified";
            WriteTemporary(temporaryJournalPath, SerializeJournal(safetyJournal));
            File.Move(temporaryJournalPath, safetyJournalPath, overwrite: true);
            return new LocalSaveRestoreReceipt(
                sourceFullPath,
                record.BackupPath,
                record.SourceSha256,
                safetyBackupPath);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(temporaryJournalPath);
        }
    }

    private static LocalSaveBackupRecord RequireVerified(LocalSaveBackupRecord record)
    {
        if (record.Status != BackupVerificationStatus.Verified)
        {
            throw new IOException($"Backup is not restorable ({record.Status}): {record.Error}");
        }

        return record;
    }

    private static byte[] ReadVerifiedBackup(LocalSaveBackupRecord record)
    {
        var bytes = File.ReadAllBytes(record.BackupPath);
        if (!string.Equals(Sha256(bytes), record.SourceSha256, StringComparison.Ordinal))
        {
            throw new IOException("Backup changed after its last verification.");
        }

        return bytes;
    }

    private static byte[] ReadValidatedSource(string sourcePath, PreparedEdit prepared)
    {
        if (IsSymbolicLink(sourcePath))
        {
            throw new IOException("Writing through a symbolic-link save is refused.");
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source save does not exist.", sourcePath);
        }

        var sourceBytes = File.ReadAllBytes(sourcePath);
        if (!string.Equals(Sha256(sourceBytes), prepared.SourceSha256, StringComparison.Ordinal))
        {
            throw new IOException("Source changed since the edit was prepared.");
        }

        return sourceBytes;
    }

    private static Dictionary<string, object?> CreateJournal(
        string status,
        string sourcePath,
        string sourceSha256,
        string outputPath,
        string outputSha256,
        string backupPath,
        Dictionary<string, object?> operation) => new(StringComparer.Ordinal)
        {
            ["version"] = 1,
            ["status"] = status,
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["source_path"] = sourcePath,
            ["source_sha256"] = sourceSha256,
            ["output_path"] = outputPath,
            ["output_sha256"] = outputSha256,
            ["backup_path"] = backupPath,
            ["operation"] = operation,
        };

    private static Dictionary<string, object?> OperationSummary(EditPlan plan, string mode) => new(StringComparer.Ordinal)
    {
        ["mode"] = mode,
        ["money"] = plan.Money,
        ["stack_count"] = plan.StackCounts.Count,
        ["move_count"] = 0,
        ["detach_count"] = plan.DetachHandles.Count,
        ["attach_count"] = 0,
        ["raw_count"] = 0,
        ["add_count"] = plan.Adds.Count,
        ["durability_count"] = 0,
        ["upgrade_count"] = 0,
        ["relation_count"] = 0,
        ["player_faction"] = false,
    };

    private static byte[] SerializeJournal(Dictionary<string, object?> journal) =>
        JsonSerializer.SerializeToUtf8Bytes(journal, BackupJournalJsonContext.Default.Journal);

    private static (string BackupPath, string JournalPath) CreateArtifactPaths(string sourcePath, string backupDirectory)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (stem.Length > 64)
        {
            stem = stem[..64];
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var prefix = $"{stem}_{timestamp}_{Guid.NewGuid():N}";
        var backupPath = Path.Combine(backupDirectory, $"{prefix}_ORIGINAL.sav");
        return (backupPath, Path.ChangeExtension(backupPath, ".json"));
    }

    private static void ValidateBackupDirectory(string sourceDirectory, string backupDirectory)
    {
        var relative = Path.GetRelativePath(sourceDirectory, backupDirectory);
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Backup directory must be outside the save directory.", nameof(backupDirectory));
        }
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Journal field {property} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static string RequiredSha256(JsonElement root, string property)
    {
        var value = RequiredString(root, property);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"Journal field {property} is not a SHA256 value.");
        }

        return value.ToLowerInvariant();
    }

    private static LocalSaveBackupRecord InvalidRecord(string journalPath, string backupPath, string error) =>
        new(journalPath, backupPath, string.Empty, string.Empty, string.Empty, null, null, default, BackupVerificationStatus.Corrupt, Error: error);

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void WriteNew(string path, ReadOnlySpan<byte> bytes)
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

            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                DeleteIfExists(path);
            }

            throw;
        }
    }

    private static void WriteTemporary(string path, ReadOnlySpan<byte> bytes)
    {
        WriteNew(path, bytes);
    }

    private static string TemporaryPath(string directory, string fileName) =>
        Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");

    private static void VerifyFile(string path, ReadOnlySpan<byte> expected, string expectedSha256, string label)
    {
        var readBack = File.ReadAllBytes(path);
        if (!readBack.AsSpan().SequenceEqual(expected) || !string.Equals(Sha256(readBack), expectedSha256, StringComparison.Ordinal))
        {
            throw new IOException($"{label} read-back did not match its expected bytes.");
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Preserve the primary transaction result if temporary cleanup fails.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the primary transaction result if temporary cleanup fails.
        }
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SamePath(string left, string right) =>
        PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
