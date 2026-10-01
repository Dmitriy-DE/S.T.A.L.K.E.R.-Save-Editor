using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Backups;

public sealed class LocalSaveStorageTests
{
    [Fact]
    public void Export_creates_a_verified_external_backup_and_keeps_the_source_unchanged()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var backupDirectory = Path.Combine(directory.Path, "editor-data", "backups");
        var sourcePath = Path.Combine(saves, "source.sav");
        var outputPath = Path.Combine(directory.Path, "exports", "Правка.sav");
        var secondOutputPath = Path.Combine(directory.Path, "exports", "Правка-2.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(sourcePath, sourceBytes);
        var prepared = Prepare(sourceBytes);

        var receipt = LocalSaveStorage.ExportLocal(sourcePath, outputPath, prepared, backupDirectory);
        var secondReceipt = LocalSaveStorage.ExportLocal(sourcePath, secondOutputPath, prepared, backupDirectory);

        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(outputPath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(secondOutputPath));
        Assert.NotEqual(receipt.BackupPath, secondReceipt.BackupPath);
        Assert.Equal(4, Directory.GetFiles(backupDirectory).Length);
        Assert.Equal(sourceBytes, File.ReadAllBytes(receipt.BackupPath));
        Assert.Equal(backupDirectory, Path.GetDirectoryName(receipt.BackupPath));
        Assert.Equal(backupDirectory, Path.GetDirectoryName(receipt.JournalPath));
        Assert.Equal(prepared.OutputSha256, receipt.OutputSha256);
        Assert.Equal([sourcePath], Directory.GetFiles(saves));
        using var journal = JsonDocument.Parse(File.ReadAllBytes(receipt.JournalPath));
        Assert.Equal("verified", journal.RootElement.GetProperty("status").GetString());
        Assert.Equal(Sha256(sourceBytes), journal.RootElement.GetProperty("source_sha256").GetString());
        Assert.Equal(prepared.OutputSha256, journal.RootElement.GetProperty("output_sha256").GetString());
    }

    [Fact]
    public void Export_rejects_a_stale_source_before_creating_the_backup_or_output()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var changedBytes = sourceBytes.Concat(new byte[] { 0xA5 }).ToArray();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var sourcePath = Path.Combine(saves, "source.sav");
        var outputPath = Path.Combine(directory.Path, "export.sav");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        File.WriteAllBytes(sourcePath, changedBytes);
        var prepared = Prepare(sourceBytes);

        Assert.Throws<IOException>(() => LocalSaveStorage.ExportLocal(
            sourcePath, outputPath, prepared, backupDirectory));

        Assert.Equal(changedBytes, File.ReadAllBytes(sourcePath));
        Assert.False(File.Exists(outputPath));
        Assert.False(Directory.Exists(backupDirectory));
    }

    [Fact]
    public void Export_refuses_an_existing_output_without_overwriting_it()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var sourcePath = Path.Combine(saves, "source.sav");
        var outputPath = Path.Combine(directory.Path, "export.sav");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        var existingBytes = "keep previous export"u8.ToArray();
        File.WriteAllBytes(sourcePath, sourceBytes);
        File.WriteAllBytes(outputPath, existingBytes);

        Assert.Throws<IOException>(() => LocalSaveStorage.ExportLocal(
            sourcePath, outputPath, Prepare(sourceBytes), backupDirectory));

        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(existingBytes, File.ReadAllBytes(outputPath));
        Assert.False(Directory.Exists(backupDirectory));
    }

    [Fact]
    public void Lists_verified_missing_corrupt_malformed_and_orphaned_backups()
    {
        using var directory = new TemporaryDirectory();
        var backupDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "backups")).FullName;
        var data = ReadFixture();
        var sha = Sha256(data);
        var valid = Path.Combine(backupDirectory, "valid_ORIGINAL.sav");
        File.WriteAllBytes(valid, data);
        WriteJournal(valid + ".json", valid, sha);

        var missing = Path.Combine(backupDirectory, "missing_ORIGINAL.sav");
        WriteJournal(missing + ".json", missing, sha);

        var corrupt = Path.Combine(backupDirectory, "corrupt_ORIGINAL.sav");
        File.WriteAllBytes(corrupt, "different bytes"u8.ToArray());
        WriteJournal(corrupt + ".json", corrupt, sha);

        File.WriteAllText(Path.Combine(backupDirectory, "malformed.json"), "{broken");
        File.WriteAllBytes(Path.Combine(backupDirectory, "orphan_ORIGINAL.sav"), data);

        var records = LocalSaveStorage.ListBackups([backupDirectory]);
        var byJournal = records.ToDictionary(record => Path.GetFileName(record.JournalPath));

        Assert.Equal(BackupVerificationStatus.Verified, byJournal["valid_ORIGINAL.sav.json"].Status);
        Assert.Equal(BackupVerificationStatus.Missing, byJournal["missing_ORIGINAL.sav.json"].Status);
        Assert.Equal(BackupVerificationStatus.Corrupt, byJournal["corrupt_ORIGINAL.sav.json"].Status);
        Assert.Equal(BackupVerificationStatus.Corrupt, byJournal["malformed.json"].Status);
        Assert.Equal(BackupVerificationStatus.Corrupt, byJournal["orphan_ORIGINAL.json"].Status);
        Assert.Equal(sha, byJournal["valid_ORIGINAL.sav.json"].ActualSha256);

        // A changed backup is noticed on the next listing (its size or write time differs from the hashed one) …
        File.WriteAllBytes(valid, "changed afterwards"u8.ToArray());
        Assert.Equal(BackupVerificationStatus.Corrupt,
            LocalSaveStorage.ListBackups([backupDirectory]).Single(record => record.BackupPath == valid).Status);
        // … and restore-time inspection never trusts the session's earlier hash, even for identical size and time.
        File.WriteAllBytes(valid, data);
        var stamp = File.GetLastWriteTimeUtc(valid);
        Assert.Equal(BackupVerificationStatus.Verified,
            LocalSaveStorage.ListBackups([backupDirectory]).Single(record => record.BackupPath == valid).Status);
        var tampered = data.ToArray();
        tampered[^1] ^= 0xFF;
        File.WriteAllBytes(valid, tampered);
        File.SetLastWriteTimeUtc(valid, stamp);
        Assert.Equal(BackupVerificationStatus.Corrupt, LocalSaveStorage.InspectBackup(valid + ".json").Status);
        Assert.Throws<IOException>(() => LocalSaveStorage.RestoreBackup(
            byJournal["missing_ORIGINAL.sav.json"].JournalPath,
            Path.Combine(directory.Path, "missing-restored.sav")));
        Assert.Throws<IOException>(() => LocalSaveStorage.RestoreBackup(
            byJournal["corrupt_ORIGINAL.sav.json"].JournalPath,
            Path.Combine(directory.Path, "corrupt-restored.sav")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "missing-restored.sav")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "corrupt-restored.sav")));
    }

    [Fact]
    public void Restore_copy_is_byte_exact_and_never_overwrites_an_existing_destination()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var sourcePath = Path.Combine(saves, "source.sav");
        var outputPath = Path.Combine(directory.Path, "edited.sav");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        File.WriteAllBytes(sourcePath, sourceBytes);
        var receipt = LocalSaveStorage.ExportLocal(sourcePath, outputPath, Prepare(sourceBytes), backupDirectory);
        var backupBeforeRestore = File.ReadAllBytes(receipt.BackupPath);
        var restoredPath = Path.Combine(directory.Path, "restored.sav");

        var restored = LocalSaveStorage.RestoreBackup(receipt.JournalPath, restoredPath);

        Assert.Equal(sourceBytes, File.ReadAllBytes(restoredPath));
        Assert.Equal(backupBeforeRestore, File.ReadAllBytes(receipt.BackupPath));
        Assert.Equal(receipt.BackupPath, restored.BackupPath);
        Assert.Equal(Sha256(sourceBytes), restored.OutputSha256);
        var existingPath = Path.Combine(directory.Path, "existing.sav");
        File.WriteAllBytes(existingPath, "keep existing"u8.ToArray());
        Assert.Throws<IOException>(() => LocalSaveStorage.RestoreBackup(receipt.JournalPath, existingPath));
        Assert.Equal("keep existing"u8.ToArray(), File.ReadAllBytes(existingPath));
    }

    [Fact]
    public void Restore_in_place_keeps_a_verified_safety_backup_and_restores_original_bytes()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var sourcePath = Path.Combine(saves, "source.sav");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        File.WriteAllBytes(sourcePath, sourceBytes);
        var prepared = Prepare(sourceBytes);
        var replacement = LocalSaveReplacement.ReplaceLocal(
            sourcePath, prepared, backupDirectory, _ => { });

        var restored = LocalSaveStorage.RestoreInPlace(replacement.JournalPath);

        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(restored.SafetyBackupPath!));
        Assert.Equal(BackupVerificationStatus.Verified,
            LocalSaveStorage.InspectBackup(Path.ChangeExtension(restored.SafetyBackupPath!, ".json")).Status);
        Assert.Equal(replacement.BackupPath, restored.BackupPath);
        Assert.Equal([sourcePath], Directory.GetFiles(saves));
    }

    [Fact]
    public void Restore_in_place_refuses_to_replace_a_save_changed_after_the_edit()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var sourcePath = Path.Combine(saves, "source.sav");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        File.WriteAllBytes(sourcePath, sourceBytes);
        var receipt = LocalSaveReplacement.ReplaceLocal(
            sourcePath, Prepare(sourceBytes), backupDirectory, _ => { });
        File.WriteAllBytes(sourcePath, "newer save"u8.ToArray());

        Assert.Throws<IOException>(() => LocalSaveStorage.RestoreInPlace(receipt.JournalPath));

        Assert.Equal("newer save"u8.ToArray(), File.ReadAllBytes(sourcePath));
        Assert.Equal(3, Directory.GetFiles(backupDirectory).Length);
    }

    [Fact]
    public void Restore_in_place_recreates_a_missing_slot_from_the_verified_backup()
    {
        using var directory = new TemporaryDirectory();
        var sourceBytes = ReadFixture();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var sourcePath = Path.Combine(saves, "source.sav");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        File.WriteAllBytes(sourcePath, sourceBytes);
        var replacement = LocalSaveReplacement.ReplaceLocal(
            sourcePath, Prepare(sourceBytes), backupDirectory, _ => { });
        File.Delete(sourcePath);

        var restored = LocalSaveStorage.RestoreInPlace(replacement.JournalPath);

        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Null(restored.SafetyBackupPath);
        Assert.Equal(replacement.BackupPath, restored.BackupPath);
        Assert.Equal([sourcePath], Directory.GetFiles(saves));
    }

    private static PreparedEdit Prepare(byte[] source) => XRayEditWriter.Prepare(
        source,
        new EditPlan(
            Sha256(source),
            money: 9_876,
            stackCounts: new Dictionary<uint, uint> { [0x1234] = 44 }));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ReadFixture() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "writer-stacks", "xray-stack-cop-source.sav"));

    private static void WriteJournal(string path, string backupPath, string sourceSha) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            version = 1,
            status = "verified",
            created_at = "2026-09-01T00:00:00+00:00",
            source_path = "synthetic-source.sav",
            source_sha256 = sourceSha,
            output_path = "synthetic-output.sav",
            output_sha256 = sourceSha,
            backup_path = backupPath,
            operation = new { mode = "export" },
        }));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"storage-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
