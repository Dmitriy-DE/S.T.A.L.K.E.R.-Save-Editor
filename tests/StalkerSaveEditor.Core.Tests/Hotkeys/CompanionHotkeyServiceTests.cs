using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Hotkeys;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Hotkeys;

public sealed class CompanionHotkeyServiceTests
{
    [Fact]
    public void Default_layout_matches_the_requested_shortcuts()
    {
        var layout = HotkeyLayout.Default;

        Assert.Equal(
            [
                (CompanionHotkeyAction.Heal, "Ctrl+H"),
                (CompanionHotkeyAction.RepairEquipped, "Ctrl+R"),
                (CompanionHotkeyAction.Mark, "Ctrl+M"),
                (CompanionHotkeyAction.JumpLast, "Ctrl+J"),
                (CompanionHotkeyAction.QuickSave, "Ctrl+S"),
            ],
            layout.Bindings.Select(binding => (binding.Action, binding.Gesture.ToString())));
    }

    [Fact]
    public void Parses_layout_and_rejects_duplicate_shortcuts()
    {
        var layout = HotkeyLayout.Parse("heal = Ctrl+Alt+H\nrepair_equipped=Shift+R\n");

        Assert.Equal("Ctrl+Alt+H", layout.Bindings[0].Gesture.ToString());
        Assert.Throws<HotkeyLayoutException>(() => HotkeyLayout.Parse("heal=Ctrl+H\nmark=Ctrl+H\n"));
        Assert.Throws<HotkeyLayoutException>(() => HotkeyLayout.Parse("heal=Ctrl+Ctrl+H\n"));
        Assert.Throws<HotkeyLayoutException>(() => HotkeyLayout.Parse("unknown_action=Ctrl+H\n"));
        Assert.Throws<HotkeyLayoutException>(() => HotkeyLayout.Parse("heal=Ctrl+H\nheal=Alt+H\n"));
    }

    [Fact]
    public void Reports_that_wayland_without_xwayland_cannot_register_hotkeys()
    {
        var reason = HotkeyPlatformAvailability.GetLinuxX11UnavailableReason("wayland", null);

        Assert.Contains("Wayland", reason, StringComparison.Ordinal);
        Assert.Contains("XWayland", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registers_layout_and_routes_commands_through_the_protocol_client()
    {
        using var game = SyntheticGame.Create();
        var backend = new FakeHotkeyBackend();
        var client = new CompanionProtocolClient(game.GameDirectory, timeout: TimeSpan.FromSeconds(2));
        await using var service = new CompanionHotkeyService(client, backend);
        var completed = new TaskCompletionSource<CompanionHotkeyCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CommandCompleted += (_, result) => completed.TrySetResult(result);

        var start = service.StartAsync();
        var command = await WaitForCommand(game.AppDataRoot);
        Assert.Equal("hotkeys on", command.Command);
        await ReplyTo(game.AppDataRoot, command.Id, "ok", "hotkeys=on");
        await start;

        Assert.True(service.IsActive);
        Assert.Equal(5, backend.Bindings.Count);
        backend.Raise(CompanionHotkeyAction.Heal);

        var healCommand = await WaitForCommand(game.AppDataRoot);
        Assert.Equal("heal", healCommand.Command);
        backend.Raise(CompanionHotkeyAction.Mark);
        await Task.Delay(100);
        Assert.Equal("v1 " + healCommand.Id + " heal", File.ReadAllText(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt")).Trim());
        await ReplyTo(game.AppDataRoot, healCommand.Id, "ok", "healed");
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(CompanionHotkeyAction.Heal, result.Action);
        Assert.True(result.Succeeded);
        await Task.Delay(100);
        Assert.False(File.Exists(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt")));

        var stop = service.StopAsync();
        var offCommand = await WaitForCommand(game.AppDataRoot);
        Assert.Equal("hotkeys off", offCommand.Command);
        await ReplyTo(game.AppDataRoot, offCommand.Id, "ok", "hotkeys=off");
        await stop;

        Assert.False(service.IsActive);
        Assert.True(backend.Stopped);
    }

    private static async Task<(string Id, string Command)> WaitForCommand(string appDataRoot)
    {
        var path = Path.Combine(appDataRoot, "save_editor_cmd.txt");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(File.Exists(path), "The companion command was not published.");
        var fields = File.ReadAllText(path).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(fields.Length >= 3);
        return (fields[1], string.Join(' ', fields.Skip(2)));
    }

    private static async Task ReplyTo(string appDataRoot, string id, string status, string text)
    {
        File.Delete(Path.Combine(appDataRoot, "save_editor_cmd.txt"));
        File.WriteAllText(Path.Combine(appDataRoot, "save_editor_out.txt"), $"v1 {id} {status} {text}\n");
        await Task.Delay(20);
    }

    private sealed class FakeHotkeyBackend : IGlobalHotkeyBackend
    {
        private Action<CompanionHotkeyBinding>? _onPressed;

        public IReadOnlyList<CompanionHotkeyBinding> Bindings { get; private set; } = Array.Empty<CompanionHotkeyBinding>();

        public bool Stopped { get; private set; }

        public Task StartAsync(
            IReadOnlyList<CompanionHotkeyBinding> bindings,
            Action<CompanionHotkeyBinding> onPressed,
            CancellationToken cancellationToken)
        {
            Bindings = bindings;
            _onPressed = onPressed;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public void Raise(CompanionHotkeyAction action) =>
            _onPressed!(Bindings.Single(binding => binding.Action == action));
    }

    private sealed class SyntheticGame : IDisposable
    {
        private readonly string _root;

        private SyntheticGame(string root, string gameDirectory)
        {
            _root = root;
            GameDirectory = gameDirectory;
            AppDataRoot = Path.Combine(gameDirectory, "user_data");
            Directory.CreateDirectory(AppDataRoot);
        }

        public string GameDirectory { get; }

        public string AppDataRoot { get; }

        public static SyntheticGame Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"companion-hotkeys-{Guid.NewGuid():N}");
            var gameDirectory = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
            File.WriteAllText(Path.Combine(gameDirectory, "fsgame.ltx"), "$app_data_root$ = true| false| $fs_root$| user_data\\\n");
            return new SyntheticGame(root, gameDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
