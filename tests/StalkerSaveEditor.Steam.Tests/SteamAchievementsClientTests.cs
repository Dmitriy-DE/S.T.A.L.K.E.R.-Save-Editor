using System.Text.Json;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamAchievementsClientTests
{
    [Fact]
    public async Task Lists_achievement_metadata_and_state_from_the_child_response()
    {
        var worker = new FakeWorker("""
            {"type":"Achievements","items":[{"apiName":"ACH_STALKER","name":"First Step","description":"Enter the Zone","achieved":true,"unlockTime":42,"hidden":false}]}
            """);
        var client = CreateClient(worker);

        var result = await client.ListAsync(4500);

        var achievement = Assert.Single(result);
        Assert.Equal("ACH_STALKER", achievement.ApiName);
        Assert.Equal("First Step", achievement.Name);
        Assert.Equal("Enter the Zone", achievement.Description);
        Assert.True(achievement.Achieved);
        Assert.Equal(42u, achievement.UnlockTime);
        Assert.False(achievement.Hidden);
        Assert.Equal("achievements", worker.Operation);
        Assert.Null(worker.ApiName);
        Assert.Null(worker.Achieved);
        Assert.Equal(1, worker.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changes_an_achievement_once_only_after_an_explicit_call(bool achieved)
    {
        var item = $"{{\"type\":\"Achievement\",\"item\":{{\"apiName\":\"ACH_STALKER\",\"name\":\"First Step\",\"description\":\"Enter the Zone\",\"achieved\":{achieved.ToString().ToLowerInvariant()},\"unlockTime\":42,\"hidden\":false}}}}";
        var worker = new FakeWorker(item);
        var client = CreateClient(worker);

        var result = await client.SetAsync(4500, "ACH_STALKER", achieved);

        Assert.Equal(achieved, result.Achieved);
        Assert.Equal("achievement", worker.Operation);
        Assert.Equal("ACH_STALKER", worker.ApiName);
        Assert.Equal(achieved, worker.Achieved);
        Assert.Equal(1, worker.CallCount);
    }

    [Fact]
    public async Task Refuses_unknown_app_and_empty_api_name_before_starting_the_child()
    {
        var worker = new FakeWorker("{}");
        var client = CreateClient(worker);

        await Assert.ThrowsAsync<SteamAchievementsException>(() => client.ListAsync(999999));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetAsync(4500, " ", true));

        Assert.Equal(0, worker.CallCount);
    }

    [Fact]
    public async Task Surfaces_child_errors_without_repeating_the_operation()
    {
        var worker = new FakeWorker("{\"type\":\"Error\",\"message\":\"stats unavailable\"}");
        var client = CreateClient(worker);

        var error = await Assert.ThrowsAsync<SteamAchievementsException>(() => client.ListAsync(4500));

        Assert.Contains("stats unavailable", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, worker.CallCount);
    }

    private static SteamAchievementsClient CreateClient(FakeWorker worker) =>
        new(static () => "fake-libsteam_api", worker, TimeSpan.FromSeconds(30));

    private sealed class FakeWorker(string responseJson) : ISteamWorkerProcessRunner
    {
        public int CallCount { get; private set; }

        public string? Operation { get; private set; }

        public string? ApiName { get; private set; }

        public bool? Achieved { get; private set; }

        public Task<IReadOnlyList<SteamCloudFile>> ListAsync(
            int appId,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SteamCloudFile>>([]);

        public Task<byte[]> ReadAsync(
            int appId,
            string fileName,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(Array.Empty<byte>());

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
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Operation = operation;
            ApiName = apiName;
            Achieved = achieved;
            using var document = JsonDocument.Parse(responseJson);
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
