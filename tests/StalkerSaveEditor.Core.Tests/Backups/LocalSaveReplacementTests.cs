using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Backups;

public sealed class LocalSaveReplacementTests
{
    [Fact]
    public void Creates_sibling_backup_and_recovery_before_verified_replacement()
    {
        using var directory = new TemporaryDirectory();
        var source = ReadFixture();
        var sourcePath = Path.Combine(directory.Path, "slot.sav");
        File.WriteAllBytes(sourcePath, source);
        var prepared = Prepare(source);

        var receipt = LocalSaveReplacement.ReplaceLocal(
            sourcePath,
            prepared,
            readBack =>
            {
                var parsed = XRayTrilogyReader.FromBytes(readBack.Span);
                Assert.Equal(9_876u, parsed.Money);
                Assert.Equal((ushort?)44, Assert.Single(parsed.Inventory).Count);
            });

        Assert.Equal(directory.Path, Path.GetDirectoryName(receipt.BackupPath));
        Assert.Equal(directory.Path, Path.GetDirectoryName(receipt.RecoveryPath));
        Assert.Equal(directory.Path, Path.GetDirectoryName(receipt.JournalPath));
        Assert.Equal(source, File.ReadAllBytes(receipt.BackupPath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(receipt.RecoveryPath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(sourcePath));
        Assert.Equal(prepared.OutputSha256, receipt.OutputSha256);

        using var journal = JsonDocument.Parse(File.ReadAllBytes(receipt.JournalPath));
        Assert.Equal("verified", journal.RootElement.GetProperty("status").GetString());
        Assert.Equal(Sha256(source), journal.RootElement.GetProperty("source_sha256").GetString());
        Assert.Equal(prepared.OutputSha256, journal.RootElement.GetProperty("output_sha256").GetString());
        Assert.Equal(receipt.RecoveryPath, journal.RootElement.GetProperty("recovery_path").GetString());
    }

    [Fact]
    public void Backup_creation_failure_never_replaces_the_source()
    {
        using var directory = new TemporaryDirectory();
        var source = ReadFixture();
        var sourcePath = Path.Combine(directory.Path, "slot.sav");
        File.WriteAllBytes(sourcePath, source);
        var prepared = Prepare(source);

        Assert.Throws<IOException>(() => LocalSaveReplacement.ReplaceLocal(
            sourcePath,
            prepared,
            _ => throw new InvalidOperationException("Read-back must not run."),
            new BackupFailingFileSystem()));

        Assert.Equal(source, File.ReadAllBytes(sourcePath));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void Stale_source_is_rejected_before_creating_artifacts()
    {
        using var directory = new TemporaryDirectory();
        var source = ReadFixture();
        var changed = ReadFixture();
        changed[^1] ^= 0x01;
        var sourcePath = Path.Combine(directory.Path, "slot.sav");
        File.WriteAllBytes(sourcePath, changed);
        var prepared = Prepare(source);

        Assert.Throws<IOException>(() => LocalSaveReplacement.ReplaceLocal(
            sourcePath,
            prepared,
            _ => throw new InvalidOperationException("Read-back must not run.")));

        Assert.Equal(changed, File.ReadAllBytes(sourcePath));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void Failed_field_verification_keeps_backup_and_recovery_and_leaves_journal_prepared()
    {
        using var directory = new TemporaryDirectory();
        var source = ReadFixture();
        var sourcePath = Path.Combine(directory.Path, "slot.sav");
        File.WriteAllBytes(sourcePath, source);
        var prepared = Prepare(source);

        var exception = Assert.Throws<LocalSaveReplacementException>(() => LocalSaveReplacement.ReplaceLocal(
            sourcePath,
            prepared,
            _ => throw new InvalidDataException("Synthetic read-back rejection.")));

        Assert.True(File.Exists(exception.BackupPath));
        Assert.True(File.Exists(exception.RecoveryPath));
        Assert.Equal(source, File.ReadAllBytes(exception.BackupPath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(exception.RecoveryPath));
        Assert.Equal(prepared.Data.ToArray(), File.ReadAllBytes(sourcePath));
        using var journal = JsonDocument.Parse(File.ReadAllBytes(exception.JournalPath));
        Assert.Equal("prepared", journal.RootElement.GetProperty("status").GetString());
    }

    private static PreparedEdit Prepare(byte[] source) => XRayEditWriter.Prepare(
        source,
        new EditPlan(
            Sha256(source),
            money: 9_876,
            stackCounts: new Dictionary<ushort, uint> { [0x1234] = 44 }));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ReadFixture() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-stacks",
        "xray-stack-cop-source.sav"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"save-editor-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class BackupFailingFileSystem : ILocalSaveFileSystem
    {
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void WriteNew(string path, byte[] data) => throw new IOException("Synthetic backup failure.");

        public void Replace(string sourcePath, string destinationPath) =>
            throw new InvalidOperationException("Replacement must not be reached.");

        public void DeleteIfExists(string path) => File.Delete(path);
    }
}
