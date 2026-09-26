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
    public async Task Timeout_kills_the_child_process_tree()
    {
        var process = new FakeChildProcess(new BlockingReadStream());
        var runner = new SteamWorkerProcessRunner(new FakeProcessFactory(process));

        await Assert.ThrowsAsync<TimeoutException>(
            () => runner.ListAsync(1643320, TimeSpan.FromMilliseconds(30), CancellationToken.None));

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
