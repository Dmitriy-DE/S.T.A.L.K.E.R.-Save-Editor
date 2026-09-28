using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class CloudServiceTests
{
    [Fact]
    public async Task Lists_and_reads_cloud_files_through_shared_facade()
    {
        var service = CreateService();

        var files = await service.ListFilesAsync(4500);
        var bytes = await service.ReadFileAsync(4500, "save.sav");

        Assert.Equal("save.sav", Assert.Single(files).Name);
        Assert.Equal([1, 2, 3], bytes);
    }

    [Fact]
    public async Task Stalker2_cloud_reads_use_the_web_adapter()
    {
        var web = new FakeWebReader();
        var service = new CloudService(web);

        var files = await service.ListFilesAsync(SteamAutoCloudRootLocator.Stalker2AppId);
        var bytes = await service.ReadFileAsync(SteamAutoCloudRootLocator.Stalker2AppId, "slot.sav");

        Assert.Equal("slot.sav", Assert.Single(files).Name);
        Assert.Equal([4, 5], bytes);
        Assert.Equal(1, web.ListCalls);
        Assert.Equal(1, web.ReadCalls);
    }

    [Theory]
    [InlineData(true, CloudOperationStatus.Verified)]
    [InlineData(false, CloudOperationStatus.Uncertain)]
    public async Task Writes_are_explicit_and_uncertain_results_are_not_retried(
        bool verified,
        CloudOperationStatus expectedStatus)
    {
        var calls = 0;
        var service = CreateService(
            writeRemote: (_, _, _, _, _) =>
            {
                calls++;
                return Task.FromResult(new SteamRemoteStorageWriteResult(
                    verified ? SteamRemoteStorageWriteStatus.Verified : SteamRemoteStorageWriteStatus.Uncertain,
                    4500,
                    "soc",
                    "save.sav",
                    "backup",
                    "recovery",
                    "sha",
                    verified ? null : "cloud state uncertain"));
            });

        var result = await service.WriteTrilogyAsync(4500, Prepared(), "save.sav", "backups");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Maps_pre_write_cancellation_to_aborted()
    {
        var service = CreateService(writeAuto: (_, _, _, _, _) =>
            Task.FromCanceled<SteamAutoCloudWriteResult>(new CancellationToken(true)));

        var result = await service.WriteStalker2Async(Prepared(), "slot.sav", "backups");

        Assert.Equal(CloudOperationStatus.Aborted, result.Status);
    }

    [Fact]
    public async Task Writes_stalker2_through_autocloud_and_preserves_backup_receipt()
    {
        var calls = 0;
        var service = CreateService(writeAuto: (appId, _, path, backup, _) =>
        {
            Assert.Equal(SteamAutoCloudRootLocator.Stalker2AppId, appId);
            Assert.Equal("slot.sav", path);
            Assert.Equal("backup-root", backup);
            calls++;
            return Task.FromResult(new SteamAutoCloudWriteResult(
                SteamAutoCloudWriteStatus.Verified, path, "backup", "recovery", "sha", null));
        });

        var result = await service.WriteStalker2Async(Prepared(), "slot.sav", "backup-root");

        Assert.Equal(CloudOperationStatus.Verified, result.Status);
        Assert.Equal("backup", result.BackupPath);
        Assert.Equal("recovery", result.RecoveryPath);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Achievement_read_set_and_clear_are_forwarded_as_explicit_operations()
    {
        var changes = new List<bool>();
        var service = CreateService(
            listAchievements: (_, _) => Task.FromResult<IReadOnlyList<SteamAchievement>>(
                [new SteamAchievement("ACH_ZONE", "Zone", "Entered", true, 10, false)]),
            setAchievement: (_, name, achieved, _) =>
            {
                Assert.Equal("ACH_ZONE", name);
                changes.Add(achieved);
                return Task.FromResult(new SteamAchievement(name, "Zone", "Entered", achieved, 10, false));
            });

        Assert.Equal("ACH_ZONE", Assert.Single(await service.ListAchievementsAsync(4500)).ApiName);
        Assert.True((await service.SetAchievementAsync(4500, "ACH_ZONE", true)).Achieved);
        Assert.False((await service.SetAchievementAsync(4500, "ACH_ZONE", false)).Achieved);
        Assert.Equal([true, false], changes);
    }

    private static CloudService CreateService(
        Func<int, PreparedEdit, string, string, CancellationToken, Task<SteamAutoCloudWriteResult>>? writeAuto = null,
        Func<int, PreparedEdit, string, string, CancellationToken, Task<SteamRemoteStorageWriteResult>>? writeRemote = null,
        Func<int, CancellationToken, Task<IReadOnlyList<SteamAchievement>>>? listAchievements = null,
        Func<int, string, bool, CancellationToken, Task<SteamAchievement>>? setAchievement = null) =>
        new(
            (_, _) => Task.FromResult<IReadOnlyList<SteamCloudFile>>([new SteamCloudFile("save.sav", 3, 1, true, true)]),
            (_, _, _) => Task.FromResult<byte[]>([1, 2, 3]),
            writeAuto ?? ((_, _, _, _, _) => Task.FromResult(new SteamAutoCloudWriteResult(
                SteamAutoCloudWriteStatus.Verified, "slot.sav", "backup", "recovery", "sha", null))),
            writeRemote ?? ((_, _, _, _, _) => Task.FromResult(new SteamRemoteStorageWriteResult(
                SteamRemoteStorageWriteStatus.Verified, 4500, "soc", "save.sav", "backup", "recovery", "sha", null))),
            listAchievements ?? ((_, _) => Task.FromResult<IReadOnlyList<SteamAchievement>>([])),
            setAchievement ?? ((_, name, achieved, _) => Task.FromResult(new SteamAchievement(name, name, "", achieved, 0, false))));

    private static PreparedEdit Prepared()
    {
        var source = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "writer-s2-money", "s2-money-source.sav"));
        var parsed = Stalker2SaveReader.FromBytes(source);
        var sourceSha = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        return EditService.PrepareEdit(source, new EditPlan(sourceSha, parsed.Money), "stalker2");
    }

    private sealed class FakeWebReader : ISteamCloudWebReader
    {
        public int ListCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public Task<IReadOnlyList<SteamCloudFile>> ListCloudFilesAsync(int appId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(SteamAutoCloudRootLocator.Stalker2AppId, appId);
            ListCalls++;
            return Task.FromResult<IReadOnlyList<SteamCloudFile>>([new SteamCloudFile("slot.sav", 2, 1, true, true)]);
        }

        public Task<byte[]> ReadFreshFileAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            Assert.Equal("slot.sav", remotePath);
            ReadCalls++;
            return Task.FromResult<byte[]>([4, 5]);
        }
    }
}
