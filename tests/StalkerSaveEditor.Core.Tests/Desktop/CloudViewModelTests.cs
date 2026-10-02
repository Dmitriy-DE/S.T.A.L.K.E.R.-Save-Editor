using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Host;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class CloudViewModelTests
{
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cloud-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch { }
        }
    }

    private sealed class MockCloudService : ICloudServiceAdapter
    {
        public bool IsSteamAvailable { get; set; } = true;
        public string? SteamStatusMessage { get; set; } = "Steam подключён и доступен.";

        public List<CloudFileModel> FilesToReturn { get; } = new();
        public byte[] DataToReturn { get; set; } = [1, 2, 3, 4];
        public CloudWriteResult WriteResultToReturn { get; set; } = new(CloudWriteStatus.Verified, "/tmp/backup.bak", null, "sha", "Успешно записано.");
        public int LastWriteAppId { get; private set; }
        public string? LastWriteRemotePath { get; private set; }
        public byte[]? LastWriteData { get; private set; }
        public int WriteCallCount { get; private set; }

        public Task<IReadOnlyList<CloudFileModel>> ListCloudFilesAsync(int appId, IEnumerable<string> localSavePaths, CancellationToken cancellationToken = default)
        {
            var matched = FilesToReturn.Where(f => f.AppId == appId).ToList();
            return Task.FromResult<IReadOnlyList<CloudFileModel>>(matched);
        }

        public Task<byte[]> ReadCloudFileAsync(int appId, string remotePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DataToReturn);
        }

        public Task<CloudWriteResult> WriteCloudFileAsync(int appId, string remotePath, byte[] data, string backupDirectory, CancellationToken cancellationToken = default)
        {
            WriteCallCount++;
            LastWriteAppId = appId;
            LastWriteRemotePath = remotePath;
            LastWriteData = data;
            return Task.FromResult(WriteResultToReturn);
        }
    }

    [Fact]
    public async Task Lists_cloud_saves_for_trilogy_and_s2()
    {
        var mock = new MockCloudService();
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "_appdata_/savedgames/cop_quicksave.sav", "cop_quicksave.sav", 1024, DateTime.UtcNow, null, CloudComparison.RemoteOnly));
        mock.FilesToReturn.Add(new CloudFileModel(1643320, "stalker2", "Stalker2/Saved/SaveGames/s2_quicksave.sav", "s2_quicksave.sav", 2048, DateTime.UtcNow, null, CloudComparison.Identical));

        var vm = new CloudViewModel(cloudService: mock);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.CloudSaves.Count);
        Assert.Contains(vm.CloudSaves, s => s.FileName == "cop_quicksave.sav" && s.ComparisonBadgeText == "Только в облаке");
        Assert.Contains(vm.CloudSaves, s => s.FileName == "s2_quicksave.sav" && s.ComparisonBadgeText == "Размер и время совпадают");
    }

    [Fact]
    public async Task Gates_cloud_write_without_explicit_confirmation()
    {
        using var tempDir = new TemporaryDirectory();
        var localSave = Path.Combine(tempDir.Path, "cop_quicksave.sav");
        File.WriteAllBytes(localSave, [10, 20, 30]);

        var mock = new MockCloudService();
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "_appdata_/savedgames/cop_quicksave.sav", "cop_quicksave.sav", 3, DateTime.UtcNow, localSave, CloudComparison.Identical));

        var vm = new CloudViewModel(
            cloudService: mock,
            backupDirectoryProvider: () => tempDir.Path);

        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();

        Assert.True(vm.CanRequestWrite);
        Assert.False(vm.ShowWriteConfirmDialog);
        Assert.False(vm.CanConfirmWrite);

        // Request write
        vm.RequestWriteCommand.Execute(null);
        Assert.True(vm.ShowWriteConfirmDialog);
        Assert.False(vm.CanConfirmWrite); // Checkbox still unchecked

        // Check the confirmation checkbox
        vm.WriteConfirmationChecked = true;
        Assert.True(vm.CanConfirmWrite);

        // Execute write
        await vm.ConfirmWriteAsync();
        Assert.False(vm.ShowWriteConfirmDialog);
        Assert.Equal(1, mock.WriteCallCount);
        Assert.Equal(41700, mock.LastWriteAppId);
        Assert.Equal(CloudWriteStatus.Verified, vm.LastWriteStatus);
    }

    [Fact]
    public async Task Handles_uncertain_cloud_write_status_without_retry()
    {
        using var tempDir = new TemporaryDirectory();
        var localSave = Path.Combine(tempDir.Path, "cs_save.sav");
        File.WriteAllBytes(localSave, [5, 6, 7]);

        var mock = new MockCloudService
        {
            WriteResultToReturn = new CloudWriteResult(
                CloudWriteStatus.Uncertain,
                Path.Combine(tempDir.Path, "safety.bak"),
                null,
                "sha",
                "Remote verification hash mismatch.")
        };
        mock.FilesToReturn.Add(new CloudFileModel(20510, "stalker-cs", "_appdata_/savedgames/cs_save.sav", "cs_save.sav", 3, DateTime.UtcNow, localSave, CloudComparison.LocalNewer));

        var vm = new CloudViewModel(
            cloudService: mock,
            backupDirectoryProvider: () => tempDir.Path);

        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();

        vm.RequestWriteCommand.Execute(null);
        vm.WriteConfirmationChecked = true;
        await vm.ConfirmWriteAsync();

        // Exactly 1 write attempt — no automatic retry allowed by safety rules
        Assert.Equal(1, mock.WriteCallCount);
        Assert.Equal(CloudWriteStatus.Uncertain, vm.LastWriteStatus);
        Assert.True(vm.IsWriteUncertain);
        Assert.Contains("не подтверждён", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(tempDir.Path, "safety.bak"), vm.LastBackupPath);
    }

    [Fact]
    public async Task Displays_clear_message_when_steam_is_unavailable()
    {
        var mock = new MockCloudService
        {
            IsSteamAvailable = false,
            SteamStatusMessage = "Steam не запущен или учётная запись не обнаружена."
        };

        var vm = new CloudViewModel(cloudService: mock);
        await vm.RefreshAsync();

        Assert.False(vm.IsSteamAvailable);
        Assert.Equal("Steam не запущен или учётная запись не обнаружена.", vm.SteamStatusMessage);
        Assert.False(vm.CanRequestWrite);
    }

    [Fact]
    public async Task Downloads_cloud_file_to_local_directory_with_safety_backup()
    {
        using var tempDir = new TemporaryDirectory();
        var saves = Directory.CreateDirectory(Path.Combine(tempDir.Path, "saves")).FullName;
        var backups = Path.Combine(tempDir.Path, "backups");
        var localSave = Path.Combine(saves, "existing.sav");
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xray-call-of-pripyat.sav"));
        File.WriteAllBytes(localSave, original);
        var cloudCopy = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-stacks", "xray-stack-cop-source.sav"));

        var mock = new MockCloudService { DataToReturn = cloudCopy };
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "_appdata_/savedgames/existing.sav", "existing.sav", cloudCopy.Length, DateTime.UtcNow, localSave, CloudComparison.RemoteNewer));

        string? downloadedPath = null;
        var vm = new CloudViewModel(
            cloudService: mock,
            backupDirectoryProvider: () => backups,
            onSaveDownloaded: path => downloadedPath = path);

        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();

        Assert.True(vm.CanDownload);
        await vm.DownloadSelectedAsync();

        Assert.Equal(localSave, downloadedPath);
        Assert.Equal(cloudCopy, File.ReadAllBytes(localSave));
        // The replaced local save is kept as a journaled backup.
        Assert.NotNull(vm.LastBackupPath);
        Assert.Equal(original, File.ReadAllBytes(vm.LastBackupPath!));
    }

    [Fact]
    public async Task A_cloud_save_never_replaces_a_local_save_of_another_game()
    {
        using var tempDir = new TemporaryDirectory();
        var localSoc = Path.Combine(tempDir.Path, "quicksave.sav");
        var soc = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-stacks", "xray-stack-soc-source.sav"));
        File.WriteAllBytes(localSoc, soc);
        var cs = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-stacks", "xray-stack-cs-source.sav"));
        var mock = new MockCloudService { DataToReturn = cs };
        // A wrong row (Clear Sky cloud file linked to a Shadow of Chernobyl local file) must not be trusted.
        mock.FilesToReturn.Add(new CloudFileModel(20510, "stalker-cs", "quicksave.sav", "quicksave.sav", cs.Length, DateTime.UtcNow, localSoc, CloudComparison.RemoteNewer));
        var vm = new CloudViewModel(cloudService: mock, backupDirectoryProvider: () => Path.Combine(tempDir.Path, "backups"));
        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();

        await vm.DownloadSelectedAsync();

        Assert.Equal(soc, File.ReadAllBytes(localSoc));

        // And a cloud file whose bytes are another game's save than its row says is refused as well.
        mock.FilesToReturn.Clear();
        mock.FilesToReturn.Add(new CloudFileModel(4500, "stalker-soc", "quicksave.sav", "quicksave.sav", cs.Length, DateTime.UtcNow, localSoc, CloudComparison.RemoteNewer));
        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();
        await vm.DownloadSelectedAsync();
        Assert.Equal(soc, File.ReadAllBytes(localSoc));
    }

    [Fact]
    public async Task Only_saves_of_the_same_game_with_a_unique_name_are_offered_as_the_local_side()
    {
        using var tempDir = new TemporaryDirectory();
        var soc = Path.Combine(Directory.CreateDirectory(Path.Combine(tempDir.Path, "soc")).FullName, "quicksave.sav");
        var csA = Path.Combine(Directory.CreateDirectory(Path.Combine(tempDir.Path, "cs-a")).FullName, "quicksave.sav");
        var csB = Path.Combine(Directory.CreateDirectory(Path.Combine(tempDir.Path, "cs-b")).FullName, "quicksave.sav");
        foreach (var path in new[] { soc, csA, csB }) File.WriteAllBytes(path, [1]);
        var seen = new Dictionary<int, string[]>();
        var mock = new RecordingListService(seen);
        var vm = new CloudViewModel(cloudService: mock, localSaveFilesProvider: () =>
        [
            new LocalSaveReference(soc, "stalker-soc"), new LocalSaveReference(csA, "stalker-cs"), new LocalSaveReference(csB, "stalker-cs"),
        ]);

        await vm.RefreshAsync();

        Assert.Equal([soc], seen[4500]);
        Assert.Equal([csA, csB], seen[20510]);
        Assert.Empty(seen[41700]);

        // The real adapter does not link an ambiguous name (two Clear Sky profiles) to either file.
        var cloudRoot = Path.Combine(tempDir.Path, "cloud");
        var cloudFile = Path.Combine(Directory.CreateDirectory(Path.Combine(cloudRoot, StalkerSaveEditor.Steam.SteamAutoCloudRootLocator.Stalker2DirectoryName)).FullName, "quicksave.sav");
        File.WriteAllBytes(cloudFile, [1]);
        var adapter = new CloudServiceAdapter(
            _ => throw new NotSupportedException(),
            () => throw new NotSupportedException(),
            _ => cloudRoot);
        var s2 = StalkerSaveEditor.Steam.SteamAutoCloudRootLocator.Stalker2AppId;
        Assert.Null(Assert.Single(await adapter.ListCloudFilesAsync(s2, [csA, csB])).LocalFilePath);
        Assert.Equal(csA, Assert.Single(await adapter.ListCloudFilesAsync(s2, [csA])).LocalFilePath);
    }

    [Fact]
    public async Task A_write_confirmation_belongs_to_the_row_and_bytes_it_was_requested_for()
    {
        using var tempDir = new TemporaryDirectory();
        var localA = Path.Combine(tempDir.Path, "a.scop");
        var localB = Path.Combine(tempDir.Path, "b.scop");
        File.WriteAllBytes(localA, [1, 2, 3]);
        File.WriteAllBytes(localB, [4, 5, 6]);
        var mock = new MockCloudService();
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "a.scop", "a.scop", 3, DateTime.UtcNow, localA, CloudComparison.Identical));
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "b.scop", "b.scop", 3, DateTime.UtcNow, localB, CloudComparison.Identical));
        var vm = new CloudViewModel(cloudService: mock, backupDirectoryProvider: () => tempDir.Path);
        await vm.RefreshAsync();

        // Selecting another row drops the pending confirmation.
        vm.SelectedCloudSave = vm.CloudSaves[0];
        vm.RequestWriteCommand.Execute(null);
        vm.WriteConfirmationChecked = true;
        vm.SelectedCloudSave = vm.CloudSaves[1];
        Assert.False(vm.ShowWriteConfirmDialog);
        Assert.False(vm.WriteConfirmationChecked);
        await vm.ConfirmWriteAsync();
        Assert.Equal(0, mock.WriteCallCount);

        // Local bytes that changed after the request are not written.
        vm.SelectedCloudSave = vm.CloudSaves[0];
        vm.RequestWriteCommand.Execute(null);
        vm.WriteConfirmationChecked = true;
        File.WriteAllBytes(localA, [7, 7, 7]);
        await vm.ConfirmWriteAsync();
        Assert.Equal(0, mock.WriteCallCount);
        Assert.Equal(CloudWriteStatus.Aborted, vm.LastWriteStatus);

        // The confirmed row and bytes are what reaches the transport.
        vm.SelectedCloudSave = vm.CloudSaves.First(row => row.FileName == "a.scop");
        vm.RequestWriteCommand.Execute(null);
        vm.WriteConfirmationChecked = true;
        await vm.ConfirmWriteAsync();
        Assert.Equal(1, mock.WriteCallCount);
        Assert.Equal("a.scop", mock.LastWriteRemotePath);
        Assert.Equal(new byte[] { 7, 7, 7 }, mock.LastWriteData);
    }

    private sealed class RecordingListService(Dictionary<int, string[]> seen) : ICloudServiceAdapter
    {
        public bool IsSteamAvailable => true;
        public string? SteamStatusMessage => null;
        public Task<IReadOnlyList<CloudFileModel>> ListCloudFilesAsync(int appId, IEnumerable<string> localSavePaths, CancellationToken cancellationToken = default)
        {
            seen[appId] = localSavePaths.ToArray();
            return Task.FromResult<IReadOnlyList<CloudFileModel>>([]);
        }
        public Task<byte[]> ReadCloudFileAsync(int appId, string remotePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudWriteResult> WriteCloudFileAsync(int appId, string remotePath, byte[] data, string backupDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_cloud_file_that_is_not_a_save_never_replaces_the_local_one()
    {
        using var tempDir = new TemporaryDirectory();
        var localSave = Path.Combine(tempDir.Path, "existing.sav");
        File.WriteAllBytes(localSave, [1, 1, 1]);
        var mock = new MockCloudService { DataToReturn = [9, 9, 9, 9] };
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "_appdata_/savedgames/existing.sav", "existing.sav", 4, DateTime.UtcNow, localSave, CloudComparison.RemoteNewer));
        var vm = new CloudViewModel(cloudService: mock, backupDirectoryProvider: () => Path.Combine(tempDir.Path, "backups"));

        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();
        await vm.DownloadSelectedAsync();

        Assert.Equal([1, 1, 1], File.ReadAllBytes(localSave));
        Assert.Contains("не сохранение", vm.StatusMessage, StringComparison.Ordinal);
    }
}
