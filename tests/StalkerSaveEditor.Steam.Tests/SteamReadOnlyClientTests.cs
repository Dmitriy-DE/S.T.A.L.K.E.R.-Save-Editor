using System.Text.Json;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamReadOnlyClientTests
{
    [Fact]
    public async Task List_uses_the_selected_app_and_timeout()
    {
        var runner = new FakeRunner();
        var client = new SteamReadOnlyClient(1643320, TimeSpan.FromSeconds(3), runner);

        var files = await client.ListAsync();

        Assert.Equal(1643320, runner.AppId);
        Assert.Equal(TimeSpan.FromSeconds(3), runner.Timeout);
        Assert.Equal([new SteamCloudFile("Data/slot.sav", 4, 12, true, true)], files);
    }

    [Fact]
    public async Task Read_returns_the_selected_cloud_bytes()
    {
        var runner = new FakeRunner { Data = [1, 2, 255] };
        var client = new SteamReadOnlyClient(1643320, runner: runner);

        var data = await client.ReadAsync("Data/slot.sav");

        Assert.Equal("Data/slot.sav", runner.FileName);
        Assert.Equal(new byte[] { 1, 2, 255 }, data);
    }

    [Fact]
    public void Rejects_a_nonpositive_app_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteamReadOnlyClient(0));
    }

    [Fact]
    public void Rejects_a_nonpositive_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SteamReadOnlyClient(1643320, TimeSpan.Zero));
    }

    [Fact]
    public async Task Read_rejects_an_empty_filename()
    {
        var client = new SteamReadOnlyClient(1643320, runner: new FakeRunner());

        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(" "));
    }

    private sealed class FakeRunner : ISteamWorkerProcessRunner
    {
        public int AppId { get; private set; }

        public TimeSpan Timeout { get; private set; }

        public string? FileName { get; private set; }

        public byte[] Data { get; init; } = [];

        public Task<IReadOnlyList<SteamCloudFile>> ListAsync(
            int appId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            AppId = appId;
            Timeout = timeout;
            return Task.FromResult<IReadOnlyList<SteamCloudFile>>(
                [new SteamCloudFile("Data/slot.sav", 4, 12, true, true)]);
        }

        public Task<byte[]> ReadAsync(
            int appId,
            string fileName,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            AppId = appId;
            Timeout = timeout;
            FileName = fileName;
            return Task.FromResult(Data);
        }

        public Task WriteAsync(
            int appId,
            string fileName,
            ReadOnlyMemory<byte> data,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<JsonElement> RunNativeOperationAsync(
            int appId,
            string operation,
            string? apiName,
            bool? achieved,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromException<JsonElement>(new NotSupportedException());
    }
}
