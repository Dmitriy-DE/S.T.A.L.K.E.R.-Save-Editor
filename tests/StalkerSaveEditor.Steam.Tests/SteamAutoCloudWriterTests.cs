using System.Text.Json;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamAutoCloudWriterTests
{
    private const string CloudName = "Stalker2/Saved/STEAM/SaveGames/Data/slot.sav";
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cloud-transaction");
    private static readonly string GoldenPath = Path.Combine(AppContext.BaseDirectory, "golden", "cloud-transaction", "cloud-transaction.json");

    [Fact]
    public void Finds_the_existing_Windows_local_appdata_folder()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "Stalker2"));

        var root = SteamAutoCloudRootLocator.FindRoot(
            SteamAutoCloudRootLocator.Stalker2AppId,
            isWindows: true,
            directory.Path,
            []);

        Assert.Equal(directory.Path, root);
    }

    [Fact]
    public void Finds_both_Proton_WinAppDataLocal_spellings_and_rejects_other_games()
    {
        using var directory = new TemporaryDirectory();
        var library = Path.Combine(directory.Path, "library");
        var user = Path.Combine(
            library,
            "steamapps",
            "compatdata",
            SteamAutoCloudRootLocator.Stalker2AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "pfx",
            "drive_c",
            "users",
            "PlayerProfile");
        var legacy = Path.Combine(user, "Local Settings", "Application Data");
        var modern = Path.Combine(user, "AppData", "Local");
        Directory.CreateDirectory(Path.Combine(legacy, "Stalker2"));

        Assert.Equal(legacy, SteamAutoCloudRootLocator.FindRoot(
            SteamAutoCloudRootLocator.Stalker2AppId,
            isWindows: false,
            windowsLocalAppData: null,
            [library]));

        Directory.Delete(legacy, recursive: true);
        Directory.CreateDirectory(Path.Combine(modern, "Stalker2"));
        Assert.Equal(modern, SteamAutoCloudRootLocator.FindRoot(
            SteamAutoCloudRootLocator.Stalker2AppId,
            isWindows: false,
            windowsLocalAppData: null,
            [library]));
        Assert.Null(SteamAutoCloudRootLocator.FindRoot(4500, false, null, [library]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../slot.sav")]
    [InlineData("Stalker2/../slot.sav")]
    [InlineData("Stalker2//slot.sav")]
    [InlineData("OtherGame/Saved/slot.sav")]
    [InlineData("Stalker2/C:/slot.sav")]
    public void Refuses_unsafe_or_non_S2_AutoCloud_names(string name)
    {
        using var directory = new TemporaryDirectory();

        Assert.Throws<ArgumentException>(() => SteamAutoCloudRootLocator.ResolveLocalPath(directory.Path, name));
    }

    [Fact]
    public async Task Writes_only_the_local_AutoCloud_file_during_a_child_game_session_and_verifies_web_sha()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var expected = ReadFixture(root.GetProperty("expected_file").GetString()!);
        var prepared = Prepare(root, source);
        using var directory = new TemporaryDirectory();
        var localRoot = Path.Combine(directory.Path, "WinAppDataLocal");
        var target = SteamAutoCloudRootLocator.ResolveLocalPath(localRoot, CloudName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, source);
        var web = new FakeCloudWebReader(source);
        var sessions = new FakeGameSessionRunner(() => web.SetRemote(File.ReadAllBytes(target)));
        var writer = CreateWriter(web, localRoot, sessions);
        var backupDirectory = Path.Combine(directory.Path, "backups");

        var result = await writer.WriteAsync(
            SteamAutoCloudRootLocator.Stalker2AppId,
            prepared,
            CloudName,
            backupDirectory,
            persistedTimeoutSeconds: 1);

        Assert.Equal(SteamAutoCloudWriteStatus.Verified, result.Status);
        Assert.Equal(expected, File.ReadAllBytes(target));
        Assert.Equal(expected, web.RemoteBytes);
        Assert.Equal(1, sessions.StartCount);
        Assert.Equal(1, sessions.EndCount);
        Assert.Equal(source, File.ReadAllBytes(result.BackupPath));
        Assert.Equal(expected, File.ReadAllBytes(result.RecoveryPath));
        Assert.Equal(root.GetProperty("expected_sha256").GetString(), result.OutputSha256);
        Assert.Empty(Directory.GetFiles(Path.Combine(directory.Path, "WinAppDataLocal"), "*.editor-part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Rejects_a_cloud_change_after_session_start_before_replacing_the_local_file()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var prepared = Prepare(root, source);
        using var directory = new TemporaryDirectory();
        var localRoot = Path.Combine(directory.Path, "local");
        var target = SteamAutoCloudRootLocator.ResolveLocalPath(localRoot, CloudName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, source);
        var web = new FakeCloudWebReader(source)
        {
            OnRead = read => read == 2 ? "changed cloud source"u8.ToArray() : null,
        };
        var sessions = new FakeGameSessionRunner(() => web.SetRemote(File.ReadAllBytes(target)));
        var writer = CreateWriter(web, localRoot, sessions);

        var error = await Assert.ThrowsAsync<SteamAutoCloudWriteException>(() => writer.WriteAsync(
            SteamAutoCloudRootLocator.Stalker2AppId,
            prepared,
            CloudName,
            Path.Combine(directory.Path, "backups"),
            persistedTimeoutSeconds: 1));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source, File.ReadAllBytes(target));
        Assert.Equal(1, sessions.StartCount);
        Assert.Equal(1, sessions.EndCount);
    }

    [Fact]
    public async Task Unsupported_app_is_rejected_before_web_reads_or_session_start()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var prepared = Prepare(root, source);
        using var directory = new TemporaryDirectory();
        var web = new FakeCloudWebReader(source);
        var sessions = new FakeGameSessionRunner(static () => { });
        var writer = CreateWriter(web, directory.Path, sessions);

        await Assert.ThrowsAsync<SteamAutoCloudWriteException>(() => writer.WriteAsync(
            4500,
            prepared,
            CloudName,
            Path.Combine(directory.Path, "backups")));

        Assert.Equal(0, web.ReadCount);
        Assert.Equal(0, sessions.StartCount);
    }

    [Fact]
    public async Task Backup_directory_inside_the_Steam_synced_root_is_rejected_before_cloud_io()
    {
        using var golden = ReadGolden();
        var root = golden.RootElement;
        var source = ReadFixture(root.GetProperty("source_file").GetString()!);
        var prepared = Prepare(root, source);
        using var directory = new TemporaryDirectory();
        var localRoot = Path.Combine(directory.Path, "local");
        var target = SteamAutoCloudRootLocator.ResolveLocalPath(localRoot, CloudName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, source);
        var web = new FakeCloudWebReader(source);
        var sessions = new FakeGameSessionRunner(static () => { });
        var writer = CreateWriter(web, localRoot, sessions);

        await Assert.ThrowsAsync<SteamAutoCloudWriteException>(() => writer.WriteAsync(
            SteamAutoCloudRootLocator.Stalker2AppId,
            prepared,
            CloudName,
            Path.Combine(localRoot, "SaveEditorBackups")));

        Assert.Equal(0, web.ReadCount);
        Assert.Equal(0, sessions.StartCount);
        Assert.Equal(source, File.ReadAllBytes(target));
    }

    private static SteamAutoCloudWriter CreateWriter(
        FakeCloudWebReader web,
        string localRoot,
        FakeGameSessionRunner sessions) =>
        new(
            web,
            _ => localRoot,
            sessions,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

    private static PreparedEdit Prepare(JsonElement golden, byte[] source)
    {
        var plan = new EditPlan(
            golden.GetProperty("source_sha256").GetString()!,
            golden.GetProperty("money").GetUInt32());
        var prepared = Stalker2MoneyWriter.Prepare(source, plan);
        Assert.Equal(ReadFixture(golden.GetProperty("expected_raw_file").GetString()!),
            Stalker2SaveReader.FromBytes(prepared.Data.Span).Raw.ToArray());
        return prepared;
    }

    private static JsonDocument ReadGolden() => JsonDocument.Parse(File.ReadAllBytes(GoldenPath));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    private sealed class FakeCloudWebReader(byte[] initialBytes) : ISteamCloudWebReader
    {
        private byte[] _remoteBytes = initialBytes.ToArray();

        public int ReadCount { get; private set; }

        public byte[] RemoteBytes => _remoteBytes.ToArray();

        public Func<int, byte[]?>? OnRead { get; init; }

        public Task<byte[]> ReadFreshFileAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(CloudName, remotePath);
            ReadCount++;
            var result = OnRead?.Invoke(ReadCount) ?? _remoteBytes;
            return Task.FromResult(result.ToArray());
        }

        public void SetRemote(byte[] bytes) => _remoteBytes = bytes.ToArray();
    }

    private sealed class FakeGameSessionRunner(Action onEnd) : ISteamGameSessionRunner
    {
        public int StartCount { get; private set; }

        public int EndCount { get; private set; }

        public Task<ISteamGameSession> StartSessionAsync(
            int appId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(SteamAutoCloudRootLocator.Stalker2AppId, appId);
            Assert.True(timeout > TimeSpan.Zero);
            StartCount++;
            return Task.FromResult<ISteamGameSession>(new FakeGameSession(() =>
            {
                EndCount++;
                onEnd();
            }));
        }
    }

    private sealed class FakeGameSession(Action onEnd) : ISteamGameSession
    {
        private int _ended;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                onEnd();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"steam-autocloud-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
