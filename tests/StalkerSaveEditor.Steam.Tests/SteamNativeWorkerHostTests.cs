using System.Text;
using System.Text.Json;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamNativeWorkerHostTests
{
    [Fact]
    public async Task List_uses_the_fake_native_storage_and_returns_metadata()
    {
        var storage = new FakeRemoteStorage();
        using var input = Request("{\"operation\":\"list\",\"appId\":1643320}");
        using var output = new MemoryStream();

        await SteamNativeWorkerHost.RunAsync(input, output, _ => storage);

        using var response = ReadHeader(output);
        Assert.Equal("files", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(1643320, storage.AppId);
        Assert.Equal("Data/slot.sav", response.RootElement
            .GetProperty("files")[0].GetProperty("name").GetString());
        Assert.True(storage.ListCalled);
    }

    [Fact]
    public async Task Read_returns_exact_bytes_from_fake_native_storage()
    {
        var storage = new FakeRemoteStorage { Data = [0, 1, 255] };
        using var input = Request(
            "{\"operation\":\"read\",\"appId\":1643320,\"fileName\":\"Data/slot.sav\"}");
        using var output = new MemoryStream();

        await SteamNativeWorkerHost.RunAsync(input, output, _ => storage);

        var bytes = output.ToArray();
        var newline = Array.IndexOf(bytes, (byte)'\n');
        using var header = JsonDocument.Parse(bytes.AsMemory(0, newline));
        Assert.Equal("data", header.RootElement.GetProperty("type").GetString());
        Assert.Equal(new byte[] { 0, 1, 255 }, bytes[(newline + 1)..]);
        Assert.Equal("Data/slot.sav", storage.ReadName);
    }

    [Fact]
    public async Task Worker_rejects_unknown_operations_without_creating_native_storage()
    {
        using var input = Request("{\"operation\":\"delete\",\"appId\":1643320}");
        using var output = new MemoryStream();
        var factoryCalled = false;

        await SteamNativeWorkerHost.RunAsync(input, output, _ =>
        {
            factoryCalled = true;
            return new FakeRemoteStorage();
        });

        using var response = ReadHeader(output);
        Assert.Equal("error", response.RootElement.GetProperty("type").GetString());
        Assert.False(factoryCalled);
    }

    [Fact]
    public async Task Write_uses_fake_native_storage_for_a_valid_trilogy_save()
    {
        using var sourceManifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "writer-money",
            "xray-money-vectors.json")));
        var vector = sourceManifest.RootElement.GetProperty("vectors").EnumerateArray()
            .Single(entry => entry.GetProperty("releaseId").GetString() == "stalker-soc");
        var source = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "writer-money",
            vector.GetProperty("expected").GetString()!));
        const string remotePath = "_appdata_/savedgames/slot.sav";
        var header = Encoding.UTF8.GetBytes(
            $"{{\"operation\":\"write\",\"appId\":4500,\"fileName\":\"{remotePath}\",\"size\":{source.Length}}}\n");
        using var input = new MemoryStream([.. header, .. source]);
        using var output = new MemoryStream();
        var storage = new FakeRemoteStorage();

        await SteamNativeWorkerHost.RunAsync(input, output, _ => storage);

        using var response = ReadHeader(output);
        Assert.Equal("ok", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(4500, storage.AppId);
        Assert.Equal(remotePath, storage.WriteName);
        Assert.Equal(source, storage.WrittenData);
    }

    [Fact]
    public async Task Write_rejects_a_non_save_path_before_calling_native_storage()
    {
        using var input = Request(
            "{\"operation\":\"write\",\"appId\":4500,\"fileName\":\"_appdata_/settings.ltx\",\"size\":1}\n1");
        using var output = new MemoryStream();
        var storage = new FakeRemoteStorage();

        await SteamNativeWorkerHost.RunAsync(input, output, _ => storage);

        using var response = ReadHeader(output);
        Assert.Equal("error", response.RootElement.GetProperty("type").GetString());
        Assert.Null(storage.WriteName);
    }

    [Fact]
    public async Task Write_rejects_S2_and_foreign_app_ids_before_initializing_native_storage()
    {
        using var input = Request(
            "{\"operation\":\"write\",\"appId\":1643320,\"fileName\":\"Data/slot.sav\",\"size\":1}\n1");
        using var output = new MemoryStream();
        var factoryCalled = false;

        await SteamNativeWorkerHost.RunAsync(input, output, _ =>
        {
            factoryCalled = true;
            return new FakeRemoteStorage();
        });

        using var response = ReadHeader(output);
        Assert.Equal("error", response.RootElement.GetProperty("type").GetString());
        Assert.False(factoryCalled);
    }

    [Fact]
    public async Task Game_session_starts_as_the_requested_app_and_stops_after_stdin_closes()
    {
        var storage = new FakeRemoteStorage();
        using var input = new SessionInputStream();
        using var output = new MemoryStream();

        var running = SteamNativeWorkerHost.RunGameSessionAsync(
            1643320,
            input,
            output,
            _ => storage,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.Zero);
        await storage.CallbacksStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        input.CloseInput();
        Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(2)));

        var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var ready = JsonDocument.Parse(lines[0]);
        using var closed = JsonDocument.Parse(lines[1]);
        Assert.Equal("ready", ready.RootElement.GetProperty("type").GetString());
        Assert.Equal("ok", closed.RootElement.GetProperty("type").GetString());
        Assert.Equal(1643320, storage.AppId);
        Assert.True(storage.CallbackCount >= 5);
    }

    private static MemoryStream Request(string json) =>
        new(Encoding.UTF8.GetBytes(json + "\n"));

    private static JsonDocument ReadHeader(MemoryStream output)
    {
        var bytes = output.ToArray();
        var newline = Array.IndexOf(bytes, (byte)'\n');
        return JsonDocument.Parse(bytes.AsMemory(0, newline));
    }

    private sealed class FakeRemoteStorage : ISteamRemoteStorage
    {
        public int AppId { get; private set; }

        public bool ListCalled { get; private set; }

        public string? ReadName { get; private set; }

        public string? WriteName { get; private set; }

        public byte[]? WrittenData { get; private set; }

        public byte[] Data { get; init; } = [4, 5, 6, 7];

        public int CallbackCount { get; private set; }

        public TaskCompletionSource CallbacksStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Initialize(int appId) => AppId = appId;

        public void RunCallbacks()
        {
            CallbackCount++;
            CallbacksStarted.TrySetResult();
        }

        public IReadOnlyList<SteamCloudFile> ListFiles()
        {
            ListCalled = true;
            return [new SteamCloudFile("Data/slot.sav", Data.Length, 12, true, true)];
        }

        public byte[] ReadFile(string fileName)
        {
            ReadName = fileName;
            return Data;
        }

        public void WriteFile(string fileName, byte[] data)
        {
            WriteName = fileName;
            WrittenData = data.ToArray();
        }

        public void Dispose() { }
    }

    private sealed class SessionInputStream : Stream
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CloseInput() => _closed.TrySetResult();

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _closed.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            CloseInput();
            base.Dispose(disposing);
        }
    }
}
