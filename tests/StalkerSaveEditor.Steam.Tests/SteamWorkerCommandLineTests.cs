using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamWorkerCommandLineTests
{
    [Theory]
    [InlineData]
    [InlineData("--screenshot", "out.png")]
    [InlineData("info", "--steam-native-op")]
    public void Other_command_lines_are_left_to_the_caller(params string[] args) =>
        Assert.False(SteamWorkerCommandLine.TryRun(args, out _));

    // The desktop executable opened a second window for these: anything addressed to the worker ends here.
    [Theory]
    [InlineData("--steam-native-op")]
    [InlineData("--steam-native-op", "achievements")]
    [InlineData("--steam-native-op", "achievements", "--app-id", "0")]
    [InlineData("--steam-native-op", "achievements", "--app-id", "x")]
    [InlineData("--steam-native-op", "achievement", "--app-id", "4500", "--name", "A", "--achieved", "2")]
    [InlineData("--steam-native-op", "unknown", "--app-id", "4500")]
    [InlineData("--steam-native-worker", "extra")]
    [InlineData("--steam-native-future")]
    public void A_malformed_worker_command_line_is_refused_and_never_falls_through(params string[] args)
    {
        Assert.True(SteamWorkerCommandLine.TryRun(args, out var exitCode));
        Assert.Equal(SteamWorkerCommandLine.UsageExitCode, exitCode);
    }
}
