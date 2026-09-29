using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class CrashLogDiscoveryTests
{
    [Fact]
    public void Discovers_and_orders_game_folder_and_appdata_logs_by_write_time()
    {
        using var fixture = new TemporaryDirectory();
        var game = Path.Combine(fixture.Path, "Call of Pripyat");
        var installLogs = Path.Combine(game, "logs");
        var appDataLogs = Path.Combine(game, "_appdata_", "logs");
        Directory.CreateDirectory(installLogs);
        Directory.CreateDirectory(appDataLogs);
        var older = Path.Combine(installLogs, "xray_old.log");
        var newer = Path.Combine(appDataLogs, "xray_new.log");
        File.WriteAllText(older, "old log");
        File.WriteAllText(newer, "new log");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc));
        var installation = new GameDoctorInstallation(GameTarget.CallOfPripyat, game, GameInstallSource.Steam, "11450453");

        var logs = CrashLogDiscovery.DiscoverRecentLogs(
            [installation],
            new SaveDirectoryDiscoveryOptions { HomeDirectory = fixture.Path, SteamRoots = [] });

        Assert.Equal(2, logs.Count);
        Assert.Equal(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(newer)), logs[0].Path);
        Assert.Equal(GameTarget.CallOfPripyat, logs[0].Game);
        Assert.Equal("11450453", logs[0].BuildId);
        Assert.Equal(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(older)), logs[1].Path);
    }

    [Fact]
    public void Discovers_proton_logs_from_existing_save_locator_candidates()
    {
        using var fixture = new TemporaryDirectory();
        var home = Path.Combine(fixture.Path, "home");
        var steam = Path.Combine(fixture.Path, "steam");
        var releaseRoot = Path.Combine(steam, "steamapps", "compatdata", "41700", "pfx", "drive_c", "users", "ProtonProfile", "Documents", "Stalker-COP");
        var saves = Path.Combine(releaseRoot, "savedgames");
        var logsDirectory = Path.Combine(releaseRoot, "logs");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(saves);
        Directory.CreateDirectory(logsDirectory);
        var logPath = Path.Combine(logsDirectory, "xray_steam.log");
        File.WriteAllText(logPath, "[LUA] SCRIPT ERROR\nattempt to call a nil value");

        var logs = CrashLogDiscovery.DiscoverRecentLogs(
            installations: [],
            new SaveDirectoryDiscoveryOptions
            {
                Platform = SaveDiscoveryPlatform.Linux,
                HomeDirectory = home,
                SteamRoots = [steam],
            });

        var discovered = Assert.Single(logs);
        Assert.Equal(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(logPath)), discovered.Path);
        Assert.Equal(GameTarget.CallOfPripyat, discovered.Game);
        Assert.Null(discovered.BuildId);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("sse-crash-log-discovery-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
