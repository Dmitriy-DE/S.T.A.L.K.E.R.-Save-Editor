using Xunit;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Updater;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class UpdatesViewModelTests
{
    private sealed class FakeUpdateAdapter : IUpdateServiceAdapter
    {
        public string CurrentVersion { get; set; } = "1.0.0";
        public UpdateCheckResult CheckResult { get; set; } = new(UpdateState.Current);
        public string DownloadResultPath { get; set; } = "/tmp/fake-update.zip";
        public UpdateInstallResult InstallResult { get; set; } = new(UpdateInstallState.Succeeded, 0, "Restarting...");
        public bool ThrowOnCheck { get; set; }
        public bool ThrowOnDownload { get; set; }
        public bool ThrowOnInstall { get; set; }

        public int CheckCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }
        public int InstallCallCount { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            if (ThrowOnCheck) throw new HttpRequestException("Network failure");
            return Task.FromResult(CheckResult);
        }

        public Task<string> DownloadAsync(
            UpdateArtifact artifact,
            IProgress<UpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCallCount++;
            if (ThrowOnDownload) throw new IOException("Disk full");
            progress?.Report(new UpdateProgress("download", "Done", 100, 100));
            return Task.FromResult(DownloadResultPath);
        }

        public Task<UpdateInstallResult> InstallAsync(
            string downloadedPath,
            UpdateArtifact artifact,
            IProgress<UpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            if (ThrowOnInstall) throw new InvalidOperationException("Process failed");
            progress?.Report(new UpdateProgress("install", "Installed", 1, 1));
            return Task.FromResult(InstallResult);
        }
    }

    [Fact]
    public void InitialState_ReflectsAdapterVersionAndDefaults()
    {
        var adapter = new FakeUpdateAdapter { CurrentVersion = "1.2.0" };
        var vm = new UpdatesViewModel(adapter);

        Assert.Equal("1.2.0", vm.CurrentVersion);
        Assert.False(vm.IsChecking);
        Assert.False(vm.IsDownloading);
        Assert.False(vm.IsInstalling);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsUpdateAvailable);
        Assert.False(vm.ShowNotificationBanner);
        Assert.True(vm.CanCheck);
        Assert.False(vm.CanDownload);
        Assert.False(vm.CanInstall);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrent_SetsCurrentStateAndNoBanner()
    {
        var adapter = new FakeUpdateAdapter
        {
            CurrentVersion = "1.2.0",
            CheckResult = new UpdateCheckResult(UpdateState.Current),
        };
        var vm = new UpdatesViewModel(adapter);

        await vm.CheckAsync(silent: false);

        Assert.Equal(UpdateState.Current, vm.State);
        Assert.False(vm.IsUpdateAvailable);
        Assert.False(vm.ShowNotificationBanner);
        Assert.Contains("актуальная", vm.StateBadgeText);
        Assert.False(vm.CanDownload);
        Assert.Equal(1, adapter.CheckCallCount);
    }

    [Fact]
    public async Task CheckAsync_WhenUpdateAvailable_SetsAvailableStateAndBanner()
    {
        var artifact = new UpdateArtifact("linux", "x64", "package", "update-2.0.0.tar.gz", 1024, "abc", "https://example.com/update");
        var manifest = new UpdateManifest(1, "stable", "2.0.0", "sha123", "2026-09-28", new Dictionary<string, UpdateArtifact>());
        var adapter = new FakeUpdateAdapter
        {
            CurrentVersion = "1.0.0",
            CheckResult = new UpdateCheckResult(UpdateState.Available, manifest, artifact),
        };
        var vm = new UpdatesViewModel(adapter);

        await vm.CheckAsync(silent: false);

        Assert.Equal(UpdateState.Available, vm.State);
        Assert.True(vm.IsUpdateAvailable);
        Assert.True(vm.ShowNotificationBanner);
        Assert.Equal("2.0.0", vm.LatestVersion);
        Assert.Contains("2.0.0", vm.NotificationBannerText);
        Assert.Same(artifact, vm.AvailableArtifact);
        Assert.True(vm.CanDownload);
    }

    [Fact]
    public async Task CheckAsync_WhenError_SetsErrorMessageIfNotSilent()
    {
        var adapter = new FakeUpdateAdapter
        {
            CheckResult = new UpdateCheckResult(UpdateState.Invalid, Error: "Manifest parse error"),
        };
        var vm = new UpdatesViewModel(adapter);

        await vm.CheckAsync(silent: false);

        Assert.Equal(UpdateState.Invalid, vm.State);
        Assert.True(vm.HasError);
        Assert.Equal("Manifest parse error", vm.ErrorMessage);
        Assert.False(vm.ShowNotificationBanner);
    }

    [Fact]
    public async Task CheckAsync_WhenSilentAndThrows_DoesNotSetErrorMessage()
    {
        var adapter = new FakeUpdateAdapter { ThrowOnCheck = true };
        var vm = new UpdatesViewModel(adapter);

        await vm.CheckAsync(silent: true);

        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void DismissBanner_HidesNotificationBanner()
    {
        var vm = new UpdatesViewModel(new FakeUpdateAdapter());
        vm.ShowNotificationBanner = true;

        vm.DismissBannerCommand.Execute(null);

        Assert.False(vm.ShowNotificationBanner);
    }

    [Fact]
    public async Task DownloadAsync_UpdatesProgressAndSetsFilePath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var artifact = new UpdateArtifact("linux", "x64", "package", "update.zip", 100, "hash", "https://example.com");
            var manifest = new UpdateManifest(1, "stable", "1.1.0", "sha", "now", new Dictionary<string, UpdateArtifact>());
            var adapter = new FakeUpdateAdapter
            {
                CheckResult = new UpdateCheckResult(UpdateState.Available, manifest, artifact),
                DownloadResultPath = tempFile,
            };
            var vm = new UpdatesViewModel(adapter);

            await vm.CheckAsync(silent: false);
            Assert.True(vm.CanDownload);

            await vm.DownloadAsync();

            Assert.Equal(1, adapter.DownloadCallCount);
            Assert.Equal(tempFile, vm.DownloadedFilePath);
            Assert.True(vm.IsDownloaded);
            Assert.True(vm.CanInstall);
            Assert.Equal(100, vm.ProgressPercentage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenSucceeded_SetsStatusMessage()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var artifact = new UpdateArtifact("linux", "x64", "package", "update.zip", 100, "hash", "https://example.com");
            var manifest = new UpdateManifest(1, "stable", "1.1.0", "sha", "now", new Dictionary<string, UpdateArtifact>());
            var adapter = new FakeUpdateAdapter
            {
                CheckResult = new UpdateCheckResult(UpdateState.Available, manifest, artifact),
                DownloadResultPath = tempFile,
                InstallResult = new UpdateInstallResult(UpdateInstallState.Succeeded, 0, "Update applied"),
            };
            var vm = new UpdatesViewModel(adapter);

            await vm.CheckAsync(silent: false);
            await vm.DownloadAsync();
            Assert.True(vm.CanInstall);

            await vm.InstallAsync();

            Assert.Equal(1, adapter.InstallCallCount);
            Assert.Contains("Update applied", vm.StatusMessage);
            Assert.False(vm.HasError);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
