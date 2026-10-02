using System.Security.Cryptography;
using System.Text.Json;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamRemoteStorageCloudWriterTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-money");

    public static IEnumerable<object[]> PythonOracleVectors
    {
        get
        {
            using var manifest = ReadManifest();
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
            {
                var releaseId = vector.GetProperty("releaseId").GetString()!;
                yield return [
                    releaseId,
                    vector.GetProperty("source").GetString()!,
                    vector.GetProperty("sourceSha256").GetString()!,
                    vector.GetProperty("expected").GetString()!,
                    vector.GetProperty("money").GetUInt32(),
                    releaseId switch
                    {
                        "stalker-soc" => 4500,
                        "stalker-cs" => 20510,
                        "stalker-cop" => 41700,
                        "stalker-soc-ee" => 2427410,
                        "stalker-cs-ee" => 2427420,
                        "stalker-cop-ee" => 2427430,
                        _ => throw new InvalidDataException("Unknown Python release vector."),
                    },
                    releaseId switch
                    {
                        "stalker-soc" or "stalker-cs" => "_appdata_/savedgames/slot.sav",
                        "stalker-cop" => "_appdata_/savedgames/slot.scop",
                        "stalker-soc-ee" => "STALKER Shadow of Chornobyl - EE/STEAM/savedgames/slot.sav",
                        "stalker-cs-ee" => "STALKER Clear Sky - EE/STEAM/savedgames/slot.scs",
                        "stalker-cop-ee" => "STALKER Call of Prypiat - EE/STEAM/savedgames/slot.scop",
                        _ => throw new InvalidDataException("Unknown Python release vector."),
                    },
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PythonOracleVectors))]
    public async Task Writes_only_a_matching_trilogy_save_and_keeps_python_oracle_bytes(
        string releaseId,
        string sourceName,
        string sourceSha256,
        string expectedName,
        uint money,
        int appId,
        string remotePath)
    {
        var source = ReadFixture(sourceName);
        var expected = ReadFixture(expectedName);
        var prepared = XRayMoneyWriter.Prepare(source, new EditPlan(sourceSha256, money));
        Assert.Equal(expected, prepared.Data.ToArray());
        var worker = new FakeWorker(appId, remotePath, source);
        var writer = CreateWriter(worker);
        using var directory = new TemporaryDirectory();
        var backupDirectory = Path.Combine(directory.Path, "backups");

        var result = await writer.WriteAsync(
            appId,
            prepared,
            remotePath,
            backupDirectory,
            persistedTimeoutSeconds: 1);

        Assert.Equal(SteamRemoteStorageWriteStatus.Verified, result.Status);
        Assert.Equal(releaseId, result.ReleaseId);
        Assert.Equal(expected, worker.RemoteBytes);
        Assert.Equal(source, File.ReadAllBytes(result.BackupPath));
        Assert.Equal(expected, File.ReadAllBytes(result.RecoveryPath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), result.OutputSha256);
        Assert.Equal(1, worker.WriteCount);
        Assert.True(worker.ListCount >= 1);
    }

    [Fact]
    public async Task Rejects_S2_before_any_RemoteStorage_operation()
    {
        var worker = new FakeWorker(1643320, "Data/slot.sav", [1, 2]);
        var writer = CreateWriter(worker);
        var prepared = PrepareSocSave(out _);
        using var directory = new TemporaryDirectory();

        await Assert.ThrowsAsync<SteamRemoteStorageWriteException>(() => writer.WriteAsync(
            1643320,
            prepared,
            "Data/slot.sav",
            Path.Combine(directory.Path, "backups")));

        Assert.Equal(0, worker.ReadCount);
        Assert.Equal(0, worker.WriteCount);
    }

    [Fact]
    public async Task Rejects_wrong_game_path_and_cross_game_payload_before_cloud_reads()
    {
        var worker = new FakeWorker(4500, "_appdata_/savedgames/slot.sav", [1, 2]);
        var writer = CreateWriter(worker);
        var socPrepared = PrepareSocSave(out _);
        using var directory = new TemporaryDirectory();

        await Assert.ThrowsAsync<SteamRemoteStorageWriteException>(() => writer.WriteAsync(
            4500,
            socPrepared,
            "_appdata_/savedgames/../settings.ltx",
            Path.Combine(directory.Path, "backups")));
        await Assert.ThrowsAsync<SteamRemoteStorageWriteException>(() => writer.WriteAsync(
            4500,
            socPrepared,
            "_appdata_/savedgames/slot.dds",
            Path.Combine(directory.Path, "backups")));
        await Assert.ThrowsAsync<SteamRemoteStorageWriteException>(() => writer.WriteAsync(
            41700,
            socPrepared,
            "_appdata_/savedgames/slot.scop",
            Path.Combine(directory.Path, "backups")));

        Assert.Equal(0, worker.ReadCount);
        Assert.Equal(0, worker.WriteCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task Stale_cloud_source_aborts_before_backup_or_remote_write()
    {
        var prepared = PrepareSocSave(out var source);
        var worker = new FakeWorker(4500, "_appdata_/savedgames/slot.sav", "changed"u8.ToArray());
        var writer = CreateWriter(worker);
        using var directory = new TemporaryDirectory();

        await Assert.ThrowsAsync<SteamRemoteStorageWriteException>(() => writer.WriteAsync(
            4500,
            prepared,
            "_appdata_/savedgames/slot.sav",
            Path.Combine(directory.Path, "backups")));

        Assert.False(source.SequenceEqual(worker.RemoteBytes));
        Assert.Equal(0, worker.WriteCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task Uncertain_native_write_is_not_retried()
    {
        var prepared = PrepareSocSave(out var source);
        const string remotePath = "_appdata_/savedgames/slot.sav";
        var worker = new FakeWorker(4500, remotePath, source) { FailAfterWrite = true };
        var writer = CreateWriter(worker);
        using var directory = new TemporaryDirectory();

        var result = await writer.WriteAsync(
            4500,
            prepared,
            remotePath,
            Path.Combine(directory.Path, "backups"));

        Assert.Equal(SteamRemoteStorageWriteStatus.Uncertain, result.Status);
        Assert.Equal(1, worker.WriteCount);
        Assert.Contains("uncertain", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.BackupPath));
        Assert.True(File.Exists(result.RecoveryPath));
    }

    [Fact]
    public void Availability_requires_a_supported_release_and_native_library()
    {
        var worker = new FakeWorker(4500, "_appdata_/savedgames/slot.sav", [1]);
        var writer = new SteamRemoteStorageCloudWriter(static () => null, worker, TimeSpan.FromMilliseconds(1));

        Assert.False(writer.CheckAvailability(4500).CanWrite);
        Assert.False(writer.CheckAvailability(1643320).CanWrite);
        Assert.False(writer.CheckAvailability(999999).CanWrite);
    }

    private static SteamRemoteStorageCloudWriter CreateWriter(FakeWorker worker) =>
        new(static () => "synthetic-libsteam_api", worker, TimeSpan.FromMilliseconds(1));

    private static PreparedEdit PrepareSocSave(out byte[] source)
    {
        using var manifest = ReadManifest();
        var vector = manifest.RootElement.GetProperty("vectors").EnumerateArray()
            .Single(entry => entry.GetProperty("releaseId").GetString() == "stalker-soc");
        source = ReadFixture(vector.GetProperty("source").GetString()!);
        return XRayMoneyWriter.Prepare(source, new EditPlan(
            vector.GetProperty("sourceSha256").GetString()!,
            vector.GetProperty("money").GetUInt32()));
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "xray-money-vectors.json")));

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private sealed class FakeWorker(int appId, string remotePath, byte[] source) : ISteamWorkerProcessRunner
    {
        private byte[] _remoteBytes = source.ToArray();

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public int ListCount { get; private set; }

        public bool FailAfterWrite { get; init; }

        public byte[] RemoteBytes => _remoteBytes.ToArray();

        public Task<IReadOnlyList<SteamCloudFile>> ListAsync(
            int selectedAppId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(appId, selectedAppId);
            ListCount++;
            return Task.FromResult<IReadOnlyList<SteamCloudFile>>(
                [new SteamCloudFile(remotePath, _remoteBytes.Length, 1, true, true)]);
        }

        public Task<byte[]> ReadAsync(
            int selectedAppId,
            string fileName,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(appId, selectedAppId);
            Assert.Equal(remotePath, fileName);
            ReadCount++;
            return Task.FromResult(_remoteBytes.ToArray());
        }

        public Task WriteAsync(
            int selectedAppId,
            string fileName,
            ReadOnlyMemory<byte> data,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(appId, selectedAppId);
            Assert.Equal(remotePath, fileName);
            WriteCount++;
            _remoteBytes = data.ToArray();
            return FailAfterWrite
                ? Task.FromException(new IOException("fake native write result is uncertain"))
                : Task.CompletedTask;
        }

        public Task<JsonElement> RunNativeOperationAsync(
            int selectedAppId,
            string operation,
            string? apiName,
            bool? achieved,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromException<JsonElement>(new NotSupportedException());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cloud-trilogy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; } = string.Empty;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
