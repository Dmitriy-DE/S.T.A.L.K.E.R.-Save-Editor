using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Steam;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamAchievementsWorkerHostTests
{
    [Fact]
    public async Task List_uses_fake_user_stats_and_serializes_all_achievement_fields()
    {
        var stats = new FakeUserStats();
        using var output = new MemoryStream();

        var exitCode = await SteamAchievementsWorkerHost.RunAsync(
            4500,
            "achievements",
            null,
            null,
            output,
            _ => stats);

        using var response = ReadResponse(output);
        Assert.Equal(0, exitCode);
        Assert.Equal("Achievements", response.RootElement.GetProperty("type").GetString());
        var item = Assert.Single(response.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("ACH_STALKER", item.GetProperty("apiName").GetString());
        Assert.Equal("First Step", item.GetProperty("name").GetString());
        Assert.Equal("Enter the Zone", item.GetProperty("description").GetString());
        Assert.True(item.GetProperty("achieved").GetBoolean());
        Assert.Equal(42u, item.GetProperty("unlockTime").GetUInt32());
        Assert.False(item.GetProperty("hidden").GetBoolean());
        Assert.Equal(4500, stats.AppId);
        Assert.Equal(1, stats.ListCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Set_passes_an_explicit_unlock_or_clear_to_fake_stats(bool achieved)
    {
        var stats = new FakeUserStats();
        using var output = new MemoryStream();

        var exitCode = await SteamAchievementsWorkerHost.RunAsync(
            4500,
            "achievement",
            "ACH_STALKER",
            achieved,
            output,
            _ => stats);

        using var response = ReadResponse(output);
        Assert.Equal(0, exitCode);
        Assert.Equal("Achievement", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(achieved, response.RootElement.GetProperty("item").GetProperty("achieved").GetBoolean());
        Assert.Equal("ACH_STALKER", stats.SetName);
        Assert.Equal(achieved, stats.SetAchieved);
    }

    [Fact]
    public async Task Rejects_unknown_app_ids_and_operations_before_initializing_stats()
    {
        var factoryCalls = 0;
        using var output = new MemoryStream();

        var exitCode = await SteamAchievementsWorkerHost.RunAsync(
            999999,
            "achievements",
            null,
            null,
            output,
            _ =>
            {
                factoryCalls++;
                return new FakeUserStats();
            });

        using var response = ReadResponse(output);
        Assert.NotEqual(0, exitCode);
        Assert.Equal("Error", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task Unknown_achievement_name_returns_an_error_without_set_calls()
    {
        var stats = new FakeUserStats();
        using var output = new MemoryStream();

        var exitCode = await SteamAchievementsWorkerHost.RunAsync(
            4500,
            "achievement",
            "ACH_UNKNOWN",
            true,
            output,
            _ => stats);

        using var response = ReadResponse(output);
        Assert.NotEqual(0, exitCode);
        Assert.Equal("Error", response.RootElement.GetProperty("type").GetString());
        Assert.Null(stats.SetName);
    }

    private static JsonDocument ReadResponse(MemoryStream output) =>
        JsonDocument.Parse(output.ToArray());

    private sealed class FakeUserStats : ISteamUserStats
    {
        public int AppId { get; private set; }

        public int ListCount { get; private set; }

        public string? SetName { get; private set; }

        public bool? SetAchieved { get; private set; }

        public void Initialize(int appId) => AppId = appId;

        public IReadOnlyList<SteamAchievement> List()
        {
            ListCount++;
            return [new SteamAchievement("ACH_STALKER", "First Step", "Enter the Zone", true, 42, false)];
        }

        public SteamAchievement Set(string apiName, bool achieved)
        {
            if (apiName != "ACH_STALKER")
            {
                throw new InvalidOperationException("unknown achievement");
            }

            SetName = apiName;
            SetAchieved = achieved;
            return new SteamAchievement(apiName, "First Step", "Enter the Zone", achieved, achieved ? 43u : 0u, false);
        }

        public void Dispose() { }
    }
}
