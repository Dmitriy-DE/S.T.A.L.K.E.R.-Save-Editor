using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Steam;
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
    public async Task Worker_rejects_write_operations_without_creating_native_storage()
    {
        using var input = Request("{\"operation\":\"write\",\"appId\":1643320}");
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

        public byte[] Data { get; init; } = [4, 5, 6, 7];

        public void Initialize(int appId) => AppId = appId;

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

        public void Dispose() { }
    }
}
