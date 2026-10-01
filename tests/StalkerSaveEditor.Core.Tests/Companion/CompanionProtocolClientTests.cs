using System.Text.Json;
using StalkerSaveEditor.Core.Companion;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class CompanionProtocolClientTests
{
    private static readonly string GoldenDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "golden",
        "companion");

    [Fact]
    public void Parses_all_protocol_golden_replies()
    {
        foreach (var path in Directory.EnumerateFiles(GoldenDirectory, "*.json"))
        {
            using var golden = JsonDocument.Parse(File.ReadAllBytes(path));
            var command = golden.RootElement.GetProperty("command").GetString()!;
            var expected = golden.RootElement.GetProperty("reply").GetString()!;
            var commandId = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

            var parsed = CompanionProtocolReply.Parse(expected);

            Assert.Equal(commandId, parsed.Id);
            Assert.Equal(expected.Split(' ')[2], parsed.WireStatus);
            Assert.Equal(expected.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(3) ?? string.Empty, parsed.Text);
        }
    }

    [Fact]
    public async Task Publishes_the_new_hotkey_protocol_commands_from_golden_examples()
    {
        foreach (var fixtureName in new[] { "mark", "jump_last", "quicksave", "hotkeys_on", "hotkeys_off" })
        {
            using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
            var client = game.CreateClient("b6request", timeout: ReplyDrivenTimeout);
            using var golden = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(GoldenDirectory, $"{fixtureName}.json")));
            var commandWords = golden.RootElement.GetProperty("command").GetString()!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var expectedReply = golden.RootElement.GetProperty("reply").GetString()!;
            var command = commandWords[2];
            var arguments = commandWords.Skip(3).ToArray();
            var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");
            var replyPath = Path.Combine(game.AppDataRoot, "save_editor_out.txt");

            var send = client.SendAsync(command, arguments);
            await WaitForFile(commandPath);
            Assert.Equal($"v1 b6request {string.Join(' ', commandWords.Skip(2))}", File.ReadAllText(commandPath).Trim());
            File.Delete(commandPath);
            PublishReply(replyPath, expectedReply.Replace(
                $"v1 {commandWords[1]} ",
                "v1 b6request ",
                StringComparison.Ordinal));

            var reply = await send;

            Assert.Equal("b6request", reply.Id);
        }
    }

    [Fact]
    public void Resolves_app_data_root_from_fsgame_aliases_without_guessing()
    {
        using var game = SyntheticGame.Create(
            "$app_data_root$ = true| false| $fs_root$| _appdata_\\\n" +
            "$user_data$ = true| false| $app_data_root$| profile\\\n");

        var appDataRoot = CompanionAppDataRootResolver.Resolve(game.GameDirectory);

        Assert.Equal(Path.Combine(game.GameDirectory, "_appdata_"), appDataRoot);
    }

    [Fact]
    public void Uses_the_requested_soc_fsgame_override()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| common\\\n");
        File.WriteAllText(
            Path.Combine(game.GameDirectory, "fsgame_soc.ltx"),
            "$app_data_root$ = true| false| $fs_root$| soc-data\\\n");

        var appDataRoot = CompanionAppDataRootResolver.Resolve(game.GameDirectory, "fsgame_soc.ltx");

        Assert.Equal(Path.Combine(game.GameDirectory, "soc-data"), appDataRoot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("$app_data_root$ = true| false| $missing_root$| appdata\\\n")]
    [InlineData("$app_data_root$ = true| false| $second_root$| appdata\\\n$second_root$ = true| false| $app_data_root$| loop\\\n")]
    [InlineData("$app_data_root$ = true| false| $fs_root$| one\\\n$app_data_root$ = true| false| $fs_root$| two\\\n")]
    public void Rejects_missing_ambiguous_or_cyclic_fsgame_definitions(string fsgame)
    {
        using var game = SyntheticGame.Create(fsgame);

        Assert.Throws<CompanionProtocolException>(() => CompanionAppDataRootResolver.Resolve(game.GameDirectory));
    }

    [Fact]
    public async Task Writes_atomically_and_waits_for_the_reply_with_the_same_id()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var client = game.CreateClient("request-1", timeout: ReplyDrivenTimeout);
        var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");
        var temporaryPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.tmp");
        var replyPath = Path.Combine(game.AppDataRoot, "save_editor_out.txt");
        File.WriteAllText(replyPath, "v1 stale-id ok stale\n");

        var send = client.SendAsync("ping");
        await WaitForFile(commandPath);
        Assert.False(File.Exists(temporaryPath));
        Assert.Equal("v1 request-1 ping", File.ReadAllText(commandPath).Trim());
        await Task.Delay(50);
        Assert.False(send.IsCompleted);

        File.Delete(commandPath);
        PublishReply(replyPath, "v1 request-1 ok pong\n");
        var reply = await send;

        Assert.Equal("request-1", reply.Id);
        Assert.Equal(CompanionReplyStatus.Ok, reply.Status);
        Assert.Equal("pong", reply.Text);
    }

    [Fact]
    public void Reply_reader_allows_the_game_to_rotate_the_reply_file()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var replyPath = Path.Combine(game.AppDataRoot, "save_editor_out.txt");
        var temporaryPath = Path.Combine(game.AppDataRoot, "save_editor_out.tmp");
        File.WriteAllText(replyPath, "v1 old-request ok stale\n");

        using var oldReply = CompanionProtocolClient.OpenReplyReadStream(replyPath);
        File.WriteAllText(temporaryPath, "v1 new-request ok pong\n");
        File.Delete(replyPath);
        File.Move(temporaryPath, replyPath);

        using var reader = new StreamReader(oldReply);
        Assert.Equal("v1 old-request ok stale\n", reader.ReadToEnd());
        Assert.Equal("v1 new-request ok pong\n", File.ReadAllText(replyPath));
    }

    [Fact]
    public async Task Serializes_commands_and_does_not_publish_the_next_until_the_first_is_consumed()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var client = game.CreateClientSequence("request-1", "request-2", timeout: ReplyDrivenTimeout);
        var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");
        var replyPath = Path.Combine(game.AppDataRoot, "save_editor_out.txt");

        var first = client.SendAsync("ping");
        await WaitForFile(commandPath);
        var second = client.SendAsync("info");
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
        Assert.Equal("v1 request-1 ping", File.ReadAllText(commandPath).Trim());

        File.Delete(commandPath);
        PublishReply(replyPath, "v1 request-1 ok pong\n");
        await first;
        await WaitForFile(commandPath);
        Assert.Equal("v1 request-2 info", File.ReadAllText(commandPath).Trim());
        File.Delete(commandPath);
        PublishReply(replyPath, "v1 request-2 ok level=zaton x=1 y=2 z=3 money=4\n");

        var secondReply = await second;

        Assert.Equal("request-2", secondReply.Id);
    }

    [Fact]
    public async Task Rejects_unknown_commands_and_preexisting_pending_commands_before_writing()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var client = game.CreateClient("request-1", timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync("fly"));
        Assert.False(File.Exists(Path.Combine(game.AppDataRoot, "save_editor_cmd.tmp")));

        var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");
        File.WriteAllText(commandPath, "v1 foreign ping\n");
        await Assert.ThrowsAsync<CompanionCommandPendingException>(() => client.SendAsync("ping"));
        Assert.Equal("v1 foreign ping\n", File.ReadAllText(commandPath));
        Assert.False(File.Exists(Path.Combine(game.AppDataRoot, "save_editor_cmd.tmp")));
    }

    [Fact]
    public async Task A_cancelled_request_withdraws_its_own_command_but_never_a_foreign_one()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");
        var client = game.CreateClient("request-1", timeout: TimeSpan.FromSeconds(30));

        using (var cancellation = new CancellationTokenSource())
        {
            var pending = client.SendAsync("info", cancellationToken: cancellation.Token);
            while (!File.Exists(commandPath)) await Task.Delay(10);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.False(File.Exists(commandPath));

        // The next request is not blocked by a leftover command.
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = game.CreateClient("request-2", timeout: TimeSpan.FromSeconds(30)).SendAsync("info", cancellationToken: cancellation.Token);
            while (!File.Exists(commandPath)) await Task.Delay(10);
            // Another writer replaced the command meanwhile: it must survive our cleanup.
            File.WriteAllText(commandPath, "v1 foreign ping\n");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.Equal("v1 foreign ping\n", File.ReadAllText(commandPath));
    }

    [Fact]
    public async Task An_oversized_reply_file_is_ignored_instead_of_being_loaded()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        Directory.CreateDirectory(game.AppDataRoot);
        File.WriteAllBytes(Path.Combine(game.AppDataRoot, "save_editor_out.txt"), new byte[CompanionProtocolClient.MaximumReplyBytes + 1]);
        var client = game.CreateClient("request-1", timeout: TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<CompanionProtocolTimeoutException>(() => client.SendAsync("info"));
        Assert.False(File.Exists(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt")));
    }

    [Theory]
    [InlineData("give", "medkit|101")]
    [InlineData("money", "1.5")]
    [InlineData("teleport", "NaN|0|0")]
    [InlineData("weather", "rain|later")]
    [InlineData("mark", "unexpected")]
    [InlineData("hotkeys", "maybe")]
    [InlineData("god", "yes")]
    [InlineData("noclip", "on|fast")]
    [InlineData("timespeed", "101")]
    [InlineData("timespeed", "-1")]
    public async Task Rejects_arguments_outside_the_protocol_table_before_writing(string command, string arguments)
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var client = game.CreateClient("request-1", timeout: TimeSpan.FromMilliseconds(100));
        var commandArguments = arguments.Split('|', StringSplitOptions.None);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(command, commandArguments));

        Assert.False(File.Exists(Path.Combine(game.AppDataRoot, "save_editor_cmd.tmp")));
        Assert.False(File.Exists(Path.Combine(game.AppDataRoot, "save_editor_cmd.txt")));
    }

    [Fact]
    public async Task Timeout_removes_only_the_command_still_owned_by_this_request()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        var client = game.CreateClient("request-1", timeout: TimeSpan.FromMilliseconds(70));
        var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");

        await Assert.ThrowsAsync<CompanionProtocolTimeoutException>(() => client.SendAsync("ping"));

        Assert.False(File.Exists(commandPath));
    }

    [Fact]
    public async Task Does_not_remove_a_replaced_command_after_timeout()
    {
        using var game = SyntheticGame.Create("$app_data_root$ = true| false| $fs_root$| user-data\\\n");
        // Long enough that a stalled runner cannot expire the request before the test replaces the command.
        var client = game.CreateClient("request-1", timeout: TimeSpan.FromSeconds(1));
        var commandPath = Path.Combine(game.AppDataRoot, "save_editor_cmd.txt");
        var send = client.SendAsync("ping");
        await WaitForFile(commandPath);
        File.WriteAllText(commandPath, "v1 foreign info\n");

        await Assert.ThrowsAsync<CompanionProtocolTimeoutException>(() => send);

        Assert.Equal("v1 foreign info\n", File.ReadAllText(commandPath));
    }

    // Happy-path tests finish as soon as the reply file appears; the timeout only guards against a hang,
    // so it is generous to survive slow CI runners. Timeout behaviour has its own short-timeout tests.
    private static readonly TimeSpan ReplyDrivenTimeout = TimeSpan.FromSeconds(30);

    private static async Task WaitForFile(string path)
    {
        var timeoutAt = DateTime.UtcNow + ReplyDrivenTimeout;
        while (!File.Exists(path) && DateTime.UtcNow < timeoutAt)
        {
            await Task.Delay(10);
        }

        Assert.True(File.Exists(path), $"Timed out waiting for file: {Path.GetFileName(path)}");
    }

    private static void PublishReply(string replyPath, string text)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(replyPath)!, "save_editor_out.tmp");
        File.WriteAllText(temporaryPath, text);
        File.Delete(replyPath);
        File.Move(temporaryPath, replyPath);
    }

    private sealed class SyntheticGame : IDisposable
    {
        private readonly string _root;

        private SyntheticGame(string root, string gameDirectory)
        {
            _root = root;
            GameDirectory = gameDirectory;
            AppDataRoot = Path.Combine(gameDirectory, "user-data");
            Directory.CreateDirectory(AppDataRoot);
        }

        public string GameDirectory { get; }

        public string AppDataRoot { get; }

        public static SyntheticGame Create(string fsgame)
        {
            var root = Path.Combine(Path.GetTempPath(), $"companion-protocol-{Guid.NewGuid():N}");
            var gameDirectory = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
            File.WriteAllText(Path.Combine(gameDirectory, "fsgame.ltx"), fsgame);
            return new SyntheticGame(root, gameDirectory);
        }

        public CompanionProtocolClient CreateClient(string id, TimeSpan timeout) =>
            new(GameDirectory, timeout, TimeSpan.FromMilliseconds(10), () => id);

        public CompanionProtocolClient CreateClientSequence(string firstId, string secondId, TimeSpan timeout)
        {
            var ids = new Queue<string>([firstId, secondId]);
            return new(GameDirectory, timeout, TimeSpan.FromMilliseconds(10), ids.Dequeue);
        }

        public void Dispose()
        {
            // Windows can hold a just-closed file for a moment; a leftover temporary folder is harmless.
            for (var attempt = 0; attempt < 10 && Directory.Exists(_root); attempt++)
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException) when (attempt < 9)
                {
                    Thread.Sleep(50);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
