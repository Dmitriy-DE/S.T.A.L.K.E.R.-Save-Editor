using System.Text.Json;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Hotkeys;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class CompanionServiceTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "companion-installer");

    private static readonly string ModSourceRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../mods/companion"));

    [Fact]
    public async Task Reports_not_installed_when_game_is_not_found()
    {
        using var temporary = new TemporaryDirectory();
        var service = new CompanionService(
            new CompanionInstaller(ModSourceRoot),
            CompanionGame.CallOfPripyat,
            Path.Combine(temporary.Path, "missing-game"));

        var status = await service.GetStatusAsync();

        Assert.Equal(CompanionRuntimeState.NotInstalled, status.State);
        Assert.False(status.Installation.GameFound);
        Assert.Null(status.LastPingUtc);
    }

    [Fact]
    public async Task Reports_active_only_after_a_matching_fresh_ping_reply()
    {
        using var game = SyntheticGame.Create();
        var installer = new CompanionInstaller(ModSourceRoot);
        installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);
        await using var service = new CompanionService(
            installer,
            CompanionGame.CallOfPripyat,
            game.GameDirectory,
            protocolTimeout: TimeSpan.FromSeconds(30));

        var statusTask = service.GetStatusAsync();
        var command = await WaitForCommand(game.AppDataRoot);
        Assert.Equal("ping", command.Command);
        File.Delete(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt"));
        File.WriteAllText(
            Path.Combine(game.AppDataRoot, "save_editor_out.txt"),
            $"v1 {command.Id} ok pong {installer.BundledModBuild}\n");

        var status = await statusTask;

        Assert.Equal(CompanionRuntimeState.Active, status.State);
        Assert.Equal(installer.BundledModBuild, status.GameModBuild);
        Assert.NotNull(status.LastPingUtc);
        Assert.NotNull(status.PingLatency);
        Assert.Equal(
            new DateTimeOffset(
                File.GetLastWriteTimeUtc(Path.Combine(game.AppDataRoot, "save_editor_out.txt")),
                TimeSpan.Zero),
            status.LastPingUtc);
    }

    [Theory]
    [InlineData("pong")]
    [InlineData("pong 2000.01.01")]
    public async Task Reports_outdated_when_the_game_runs_another_mod_build(string reply)
    {
        using var game = SyntheticGame.Create();
        var installer = new CompanionInstaller(ModSourceRoot);
        installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);
        Assert.NotNull(installer.BundledModBuild);
        await using var service = new CompanionService(
            installer,
            CompanionGame.CallOfPripyat,
            game.GameDirectory,
            protocolTimeout: TimeSpan.FromSeconds(30));

        var statusTask = service.GetStatusAsync();
        var command = await WaitForCommand(game.AppDataRoot);
        File.Delete(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt"));
        File.WriteAllText(Path.Combine(game.AppDataRoot, "save_editor_out.txt"), $"v1 {command.Id} ok {reply}\n");

        var status = await statusTask;

        Assert.Equal(CompanionRuntimeState.Outdated, status.State);
        Assert.Contains("reinstall", status.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_outdated_when_installed_mod_rejects_protocol_ping()
    {
        using var game = SyntheticGame.Create();
        var installer = new CompanionInstaller(ModSourceRoot);
        installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);
        await using var service = new CompanionService(
            installer,
            CompanionGame.CallOfPripyat,
            game.GameDirectory,
            protocolTimeout: TimeSpan.FromSeconds(30));

        var statusTask = service.GetStatusAsync();
        var command = await WaitForCommand(game.AppDataRoot);
        File.Delete(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt"));
        File.WriteAllText(
            Path.Combine(game.AppDataRoot, "save_editor_out.txt"),
            $"v1 {command.Id} unsupported unknown command\n");

        var status = await statusTask;

        Assert.Equal(CompanionRuntimeState.Outdated, status.State);
    }

    [Fact]
    public async Task Routes_only_the_typed_companion_actions_through_protocol_v1()
    {
        using var game = SyntheticGame.Create();
        var installer = new CompanionInstaller(ModSourceRoot);
        installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);
        await using var service = new CompanionService(
            installer,
            CompanionGame.CallOfPripyat,
            game.GameDirectory,
            protocolTimeout: TimeSpan.FromSeconds(30));

        var mark = await ReplyToAction(service.MarkAsync(), game.AppDataRoot, "mark");
        Assert.Equal(CompanionReplyStatus.Ok, mark.Status);
        var jump = await ReplyToAction(service.JumpLastAsync(), game.AppDataRoot, "jump_last");
        Assert.Equal(CompanionReplyStatus.Ok, jump.Status);
        var quickSave = await ReplyToAction(service.QuickSaveAsync("manual_slot"), game.AppDataRoot, "quicksave manual_slot");
        Assert.Equal(CompanionReplyStatus.Ok, quickSave.Status);
        var hotkeys = await ReplyToAction(service.SetHotkeyPollingAsync(true), game.AppDataRoot, "hotkeys on");
        Assert.Equal(CompanionReplyStatus.Ok, hotkeys.Status);
    }

    [Fact]
    public void Exposes_default_layout_and_rejects_a_conflicting_replacement()
    {
        using var temporary = new TemporaryDirectory();
        var service = new CompanionService(
            new CompanionInstaller(ModSourceRoot),
            CompanionGame.CallOfPripyat,
            Path.Combine(temporary.Path, "missing-game"));

        Assert.Equal(5, service.HotkeyLayout.Bindings.Count);
        Assert.Throws<HotkeyLayoutException>(() => service.SetHotkeyLayout(
            HotkeyLayout.Parse("heal=Ctrl+H\nmark=Ctrl+H\n")));
        service.SetHotkeyLayout(HotkeyLayout.Parse("heal=Ctrl+Alt+H\n"));
        Assert.Single(service.HotkeyLayout.Bindings);
    }

    private static async Task<(string Id, string Command)> WaitForCommand(string directory)
    {
        var path = Path.Combine(directory, "save_editor_cmd.txt");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(File.Exists(path), "The service did not publish a ping command.");
        var fields = File.ReadAllText(path).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("v1", fields[0]);
        return (fields[1], string.Join(' ', fields.Skip(2)));
    }

    private static async Task<CompanionProtocolReply> ReplyToAction(
        Task<CompanionProtocolReply> pendingReply,
        string appDataRoot,
        string expectedCommand)
    {
        var command = await WaitForCommand(appDataRoot);
        Assert.Equal(expectedCommand, command.Command);
        File.Delete(Path.Combine(appDataRoot, "save_editor_cmd.txt"));
        File.WriteAllText(
            Path.Combine(appDataRoot, "save_editor_out.txt"),
            $"v1 {command.Id} ok accepted\n");
        return await pendingReply;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("companion-service-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class SyntheticGame : IDisposable
    {
        private readonly string _root;

        private SyntheticGame(string root, string gameDirectory, string appDataRoot)
        {
            _root = root;
            GameDirectory = gameDirectory;
            AppDataRoot = appDataRoot;
        }

        public string GameDirectory { get; }

        public string AppDataRoot { get; }

        public static SyntheticGame Create()
        {
            var root = Directory.CreateTempSubdirectory("companion-service-game-").FullName;
            var gameDirectory = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
            var resources = Directory.CreateDirectory(Path.Combine(gameDirectory, "resources")).FullName;
            var patches = Directory.CreateDirectory(Path.Combine(gameDirectory, "patches")).FullName;
            var appDataRoot = Directory.CreateDirectory(Path.Combine(gameDirectory, "_appdata_")).FullName;
            File.WriteAllText(
                Path.Combine(gameDirectory, "fsgame.ltx"),
                "$app_data_root$ = true| false| $fs_root$| _appdata_\\\n" +
                "$game_data$ = true| true| $fs_root$| gamedata\\\n" +
                "$game_config$ = true| false| $game_data$| configs\\\n" +
                "$arch_dir_resources$ = false| false| $fs_root$| resources\\\n" +
                "$game_arch_mp$ = false| true| $fs_root$| patches\\\n");
            var manifestPath = Path.Combine(FixtureDirectory, "manifest.json");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var games = manifest.RootElement.GetProperty("games").GetProperty("cop");
            File.Copy(Path.Combine(FixtureDirectory, games.GetProperty("archive").GetString()!),
                Path.Combine(resources, "configs.db"));
            File.Copy(Path.Combine(FixtureDirectory, games.GetProperty("patchArchive02").GetString()!),
                Path.Combine(patches, "xpatch_02.db"));
            return new SyntheticGame(root, gameDirectory, appDataRoot);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
