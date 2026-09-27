using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Steam;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamWorkerProcessRunnerTests
{
    [Fact]
    public async Task List_sends_a_worker_request_and_parses_file_metadata()
    {
        var output = Encoding.UTF8.GetBytes(
            "{\"type\":\"files\",\"files\":[{\"name\":\"Data/slot.sav\",\"size\":4,\"timestamp\":12,\"isPersisted\":true,\"exists\":true}]}\n");
        var process = new FakeChildProcess(new MemoryStream(output));
        var factory = new FakeProcessFactory(process);
        var runner = new SteamWorkerProcessRunner(factory);

        var files = await runner.ListAsync(1643320, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal("Data/slot.sav", Assert.Single(files).Name);
        Assert.Contains("--steam-native-worker", factory.StartInfo!.ArgumentList);
        using var request = JsonDocument.Parse(process.Input.ToArray());
        Assert.Equal("list", request.RootElement.GetProperty("operation").GetString());
        Assert.Equal(1643320, request.RootElement.GetProperty("appId").GetInt32());
    }

    [Fact]
    public async Task Read_returns_the_exact_binary_payload_from_the_child()
    {
        var header = Encoding.UTF8.GetBytes("{\"type\":\"data\",\"size\":3}\n");
        var process = new FakeChildProcess(new MemoryStream([.. header, 0, 1, 255]));
        var runner = new SteamWorkerProcessRunner(new FakeProcessFactory(process));

        var data = await runner.ReadAsync(
            1643320,
            "Data/slot.sav",
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal(new byte[] { 0, 1, 255 }, data);
        using var request = JsonDocument.Parse(process.Input.ToArray());
        Assert.Equal("read", request.RootElement.GetProperty("operation").GetString());
        Assert.Equal("Data/slot.sav", request.RootElement.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Write_sends_one_binary_payload_to_the_worker()
    {
        var process = new FakeChildProcess(new MemoryStream(Encoding.UTF8.GetBytes("{\"type\":\"ok\"}\n")));
        var factory = new FakeProcessFactory(process);
        var runner = new SteamWorkerProcessRunner(factory);
        var payload = new byte[] { 0, 1, 255, 4 };

        await runner.WriteAsync(
            4500,
            "_appdata_/savedgames/slot.sav",
            payload,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        var input = process.Input.ToArray();
        var newlineIndex = Array.IndexOf(input, (byte)'\n');
        using var request = JsonDocument.Parse(input.AsMemory(0, newlineIndex));
        Assert.Equal("write", request.RootElement.GetProperty("operation").GetString());
        Assert.Equal(4500, request.RootElement.GetProperty("appId").GetInt32());
        Assert.Equal("_appdata_/savedgames/slot.sav", request.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(payload.Length, request.RootElement.GetProperty("size").GetInt32());
        Assert.Equal(payload, input[(newlineIndex + 1)..]);
        Assert.False(process.Killed);
    }

    [Fact]
    public async Task Achievement_list_runs_as_a_separate_native_operation_with_selected_app_id()
    {
        var process = new FakeChildProcess(new MemoryStream(
            Encoding.UTF8.GetBytes("{\"type\":\"Achievements\",\"items\":[]}\n")));
        var factory = new FakeProcessFactory(process);
        var runner = new SteamWorkerProcessRunner(factory);

        var response = await runner.RunNativeOperationAsync(
            4500,
            "achievements",
            null,
            null,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal("Achievements", response.GetProperty("type").GetString());
        Assert.Contains("--steam-native-op", factory.StartInfo!.ArgumentList);
        Assert.Contains("achievements", factory.StartInfo.ArgumentList);
        Assert.Contains("--app-id", factory.StartInfo.ArgumentList);
        Assert.Equal("4500", factory.StartInfo.Environment["SteamAppId"]);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public async Task Achievement_change_passes_the_exact_name_and_explicit_state(bool achieved, string state)
    {
        var process = new FakeChildProcess(new MemoryStream(
            Encoding.UTF8.GetBytes("{\"type\":\"Achievement\",\"item\":{}}\n")));
        var factory = new FakeProcessFactory(process);
        var runner = new SteamWorkerProcessRunner(factory);

        await runner.RunNativeOperationAsync(
            4500,
            "achievement",
            "ACH_STALKER",
            achieved,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        var args = factory.StartInfo!.ArgumentList;
        Assert.Equal(new[]
        {
            "--steam-native-op", "achievement", "--app-id", "4500", "--name", "ACH_STALKER", "--achieved", state,
        }, args.TakeLast(8));
    }

    [Fact]
    public async Task Rejects_unlisted_native_operations_before_starting_a_child()
    {
        var factory = new FakeProcessFactory(new FakeChildProcess(Stream.Null));
        var runner = new SteamWorkerProcessRunner(factory);

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunNativeOperationAsync(
            4500,
            "write",
            "file-name",
            true,
            TimeSpan.FromSeconds(30),
            CancellationToken.None));

        Assert.Null(factory.StartInfo);
    }

    [Fact]
    public async Task Achievement_operation_timeout_kills_the_child_process_tree()
    {
        var process = new FakeChildProcess(new BlockingReadStream());
        var runner = new SteamWorkerProcessRunner(new FakeProcessFactory(process));

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunNativeOperationAsync(
            4500,
            "achievement",
            "ACH_STALKER",
            true,
            TimeSpan.FromMilliseconds(30),
            CancellationToken.None));

        Assert.True(process.Killed);
    }

    [Fact]
    public async Task Timeout_kills_the_child_process_tree()
    {
        var process = new FakeChildProcess(new BlockingReadStream());
        var runner = new SteamWorkerProcessRunner(new FakeProcessFactory(process));

        await Assert.ThrowsAsync<TimeoutException>(
            () => runner.ListAsync(1643320, TimeSpan.FromMilliseconds(30), CancellationToken.None));

        Assert.True(process.Killed);
    }

    [Fact]
    public async Task Game_session_uses_the_session_operation_and_closes_stdin_to_end_it()
    {
        var output = Encoding.UTF8.GetBytes("{\"type\":\"ready\"}\n{\"type\":\"ok\"}\n");
        var process = new FakeChildProcess(new MemoryStream(output));
        var factory = new FakeProcessFactory(process);
        var runner = new SteamWorkerProcessRunner(factory);

        var session = await runner.StartSessionAsync(1643320, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Contains("--steam-native-op", factory.StartInfo!.ArgumentList);
        Assert.Contains("session", factory.StartInfo.ArgumentList);
        var appIdIndex = factory.StartInfo.ArgumentList.IndexOf("--app-id");
        Assert.Equal("1643320", factory.StartInfo.ArgumentList[appIdIndex + 1]);
        Assert.Equal("1643320", factory.StartInfo.Environment["SteamAppId"]);

        await session.DisposeAsync();

        Assert.False(process.Input.CanWrite);
        Assert.False(process.Killed);
    }

    [Fact]
    public async Task Game_session_startup_timeout_kills_the_child_process_tree()
    {
        var process = new FakeChildProcess(new BlockingReadStream());
        var runner = new SteamWorkerProcessRunner(new FakeProcessFactory(process));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runner.StartSessionAsync(1643320, TimeSpan.FromMilliseconds(30), CancellationToken.None));

        Assert.True(process.Killed);
    }

    private sealed class FakeProcessFactory(FakeChildProcess process) : ISteamWorkerProcessFactory
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public ISteamWorkerChildProcess Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            process.Start();
            return process;
        }
    }

    private sealed class FakeChildProcess(Stream output) : ISteamWorkerChildProcess
    {
        public MemoryStream Input { get; } = new();

        public Stream StandardInput => Input;

        public Stream StandardOutput { get; } = output;

        public Stream StandardError { get; } = new MemoryStream();

        public bool Killed { get; private set; }

        public void Start() { }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void KillTree() => Killed = true;

        public void Dispose()
        {
            StandardInput.Dispose();
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
