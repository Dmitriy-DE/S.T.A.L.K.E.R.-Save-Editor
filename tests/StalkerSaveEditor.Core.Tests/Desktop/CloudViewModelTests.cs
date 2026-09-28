using System.IO;
using StalkerSaveEditor.Core.Tests;
using StalkerSaveEditor.Desktop.Services;
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
        Assert.Contains(vm.CloudSaves, s => s.FileName == "s2_quicksave.sav" && s.ComparisonBadgeText == "Совпадает");
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
        Assert.Contains("Uncertain", vm.StatusMessage);
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
        var localSave = Path.Combine(tempDir.Path, "existing.sav");
        File.WriteAllBytes(localSave, [1, 1, 1]);

        var mock = new MockCloudService
        {
            DataToReturn = [9, 9, 9, 9]
        };
        mock.FilesToReturn.Add(new CloudFileModel(41700, "stalker-cop", "_appdata_/savedgames/existing.sav", "existing.sav", 4, DateTime.UtcNow, localSave, CloudComparison.RemoteNewer));

        string? downloadedPath = null;
        var vm = new CloudViewModel(
            cloudService: mock,
            backupDirectoryProvider: () => tempDir.Path,
            onSaveDownloaded: path => downloadedPath = path);

        await vm.RefreshAsync();
        vm.SelectedCloudSave = vm.CloudSaves.First();

        Assert.True(vm.CanDownload);
        await vm.DownloadSelectedAsync();

        Assert.Equal(localSave, downloadedPath);
        Assert.Equal([9, 9, 9, 9], File.ReadAllBytes(localSave));
        // Safety backup was created before overwriting
        Assert.NotNull(vm.LastBackupPath);
        Assert.True(File.Exists(vm.LastBackupPath));
    }
}
