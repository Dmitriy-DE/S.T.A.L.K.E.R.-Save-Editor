using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamCloudWriteTransactionTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "cloud-transaction");

    private static readonly string GoldenPath = Path.Combine(
        AppContext.BaseDirectory,
        "golden",
        "cloud-transaction",
        "cloud-transaction.json");

    [Fact]
    public async Task Verified_write_matches_python_oracle_and_keeps_backup_and_recovery()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var expected = ReadFixture(root.GetProperty("expected_file").GetString()!);
        var prepared = Prepare(root, source);
        using var directory = new TemporaryDirectory();
        var worker = new FakeCloudTransport(source);

        var receipt = await SteamCloudWriteTransaction.UploadAsync(
            worker,
            prepared,
            root.GetProperty("remote_path").GetString()!,
            directory.Path,
            root.GetProperty("persisted_timeout").GetInt32());

        var oracle = root.GetProperty("cases").GetProperty("verified");
        Assert.Equal(oracle.GetProperty("status").GetString(), receipt.Status.ToString().ToLowerInvariant());
        Assert.Equal(root.GetProperty("remote_path").GetString(), receipt.RemotePath);
        Assert.Equal(root.GetProperty("expected_sha256").GetString(), receipt.OutputSha256);
        Assert.Equal(source, File.ReadAllBytes(receipt.BackupPath));
        Assert.Equal(expected, File.ReadAllBytes(receipt.RecoveryPath));
        Assert.Equal(oracle.GetProperty("reads").EnumerateArray().Select(value => value.GetString()), worker.ReadCalls);
        Assert.Equal(oracle.GetProperty("writes").GetInt32(), worker.WriteCalls.Count);
        Assert.Equal(root.GetProperty("remote_path").GetString(), worker.WriteCalls[0].Path);
        Assert.Equal(expected, worker.WriteCalls[0].Data);
        Assert.Equal(oracle.GetProperty("syncs").GetInt32(), worker.SyncCalls);
        var wait = Assert.Single(worker.WaitCalls);
        Assert.Equal(root.GetProperty("remote_path").GetString(), wait.Path);
        Assert.Equal(expected.Length, wait.Size);
        Assert.Equal(root.GetProperty("persisted_timeout").GetInt32(), wait.Timeout);
    }

    [Fact]
    public async Task A_local_save_goes_up_unchanged_against_the_hash_of_the_current_cloud_copy()
    {
        using var directory = new TemporaryDirectory();
        byte[] cloud = [1, 2, 3, 4];
        byte[] local = [5, 6, 7, 8, 9];
        var worker = new FakeCloudTransport(cloud);

        var receipt = await SteamCloudWriteTransaction.UploadAsync(
            worker,
            PreparedEdit.Replacing(Convert.ToHexString(SHA256.HashData(cloud)).ToLowerInvariant(), local),
            "savedgames/slot.sav",
            directory.Path);

        Assert.Equal(local, Assert.Single(worker.WriteCalls).Data);
        Assert.Equal(cloud, File.ReadAllBytes(receipt.BackupPath));

        var changed = new FakeCloudTransport([4, 3, 2, 1]);
        await Assert.ThrowsAsync<CloudTransactionException>(() => SteamCloudWriteTransaction.UploadAsync(
            changed,
            PreparedEdit.Replacing(Convert.ToHexString(SHA256.HashData(cloud)).ToLowerInvariant(), local),
            "savedgames/slot.sav",
            directory.Path));
        Assert.Empty(changed.WriteCalls);
    }

    [Fact]
    public async Task Stale_source_aborts_before_creating_artifacts_or_writing()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var worker = new FakeCloudTransport("changed source"u8.ToArray());
        using var directory = new TemporaryDirectory();

        var error = await Assert.ThrowsAsync<CloudTransactionException>(() =>
            SteamCloudWriteTransaction.UploadAsync(
                worker,
                Prepare(root, source),
                root.GetProperty("remote_path").GetString()!,
                directory.Path));

        var oracle = root.GetProperty("cases").GetProperty("stale_source");
        Assert.True(oracle.GetProperty("error_contains").GetBoolean());
        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(worker.WriteCalls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task Uncertain_write_is_reported_without_retry_or_follow_up_sync()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var expected = ReadFixture(root.GetProperty("expected_file").GetString()!);
        var worker = new FakeCloudTransport(source, failAfterWrite: true);
        using var directory = new TemporaryDirectory();

        var receipt = await SteamCloudWriteTransaction.UploadAsync(
            worker,
            Prepare(root, source),
            root.GetProperty("remote_path").GetString()!,
            directory.Path);

        var oracle = root.GetProperty("cases").GetProperty("uncertain_write");
        Assert.Equal(oracle.GetProperty("status").GetString(), receipt.Status.ToString().ToLowerInvariant());
        Assert.True(oracle.GetProperty("reason_contains").GetBoolean());
        Assert.Contains("WriteFile", receipt.Reason);
        Assert.Equal(oracle.GetProperty("writes").GetInt32(), worker.WriteCalls.Count);
        Assert.Equal(oracle.GetProperty("syncs").GetInt32(), worker.SyncCalls);
        Assert.Equal(expected, File.ReadAllBytes(receipt.RecoveryPath));
    }

    [Fact]
    public async Task Read_only_transport_is_rejected_before_cloud_io()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var worker = new FakeCloudTransport(source, canWrite: false);
        using var directory = new TemporaryDirectory();

        await Assert.ThrowsAsync<CloudTransactionException>(() =>
            SteamCloudWriteTransaction.UploadAsync(
                worker,
                Prepare(root, source),
                root.GetProperty("remote_path").GetString()!,
                directory.Path));

        Assert.Empty(worker.ReadCalls);
        Assert.Empty(worker.WriteCalls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    private static PreparedEdit Prepare(JsonElement golden, byte[] source)
    {
        var plan = new EditPlan(
            golden.GetProperty("source_sha256").GetString()!,
            golden.GetProperty("money").GetUInt32());
        var prepared = Stalker2MoneyWriter.Prepare(source, plan);
        var expectedRaw = ReadFixture(golden.GetProperty("expected_raw_file").GetString()!);
        Assert.Equal(expectedRaw, Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.ToArray());
        return prepared;
    }

    private static JsonDocument ReadGolden() => JsonDocument.Parse(File.ReadAllBytes(GoldenPath));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private sealed class FakeCloudTransport(
        byte[] remoteBytes,
        bool canWrite = true,
        bool failAfterWrite = false) : ICloudWriteTransport
    {
        private byte[] _remoteBytes = remoteBytes.ToArray();

        public CloudWriteCapability WriteCapability { get; } = new(canWrite, "fake transport");

        public List<string> ReadCalls { get; } = [];

        public List<(string Path, byte[] Data)> WriteCalls { get; } = [];

        public List<(string Path, int Size, int Timeout)> WaitCalls { get; } = [];

        public int SyncCalls { get; private set; }

        public ValueTask<byte[]> ReadFileAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls.Add(remotePath);
            return ValueTask.FromResult(_remoteBytes.ToArray());
        }

        public ValueTask WriteFileAsync(
            string remotePath,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = data.ToArray();
            WriteCalls.Add((remotePath, bytes));
            _remoteBytes = bytes;
            return failAfterWrite
                ? ValueTask.FromException(new IOException("fake write failed after send"))
                : ValueTask.CompletedTask;
        }

        public ValueTask SyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> WaitForPersistedAsync(
            string remotePath,
            int expectedSize,
            int timeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitCalls.Add((remotePath, expectedSize, timeoutSeconds));
            return ValueTask.FromResult(true);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cloud-write-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
