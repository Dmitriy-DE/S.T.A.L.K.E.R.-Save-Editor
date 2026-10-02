using System.Globalization;

namespace StalkerSaveEditor.Steam;

/// <summary>
/// The command lines <see cref="SteamWorkerProcessRunner"/> starts a child with. Every executable that can be that
/// child (the desktop application, the CLI) routes its arguments through here first, so a worker never opens a window.
/// </summary>
public static class SteamWorkerCommandLine
{
    public const int UsageExitCode = 2;

    /// <summary>True when the arguments address the Steam worker; <paramref name="exitCode"/> is then the process result.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        exitCode = 0;
        if (args.Length == 0 || !args[0].StartsWith("--steam-native-", StringComparison.Ordinal)) return false;
        exitCode = Select(args) is { } worker ? Run(worker) : Usage();
        return true;
    }

    private static Func<Task<int>>? Select(string[] args) => args switch
    {
        ["--steam-native-worker"] => SteamNativeWorkerHost.RunAsync,
        ["--steam-native-op", "session", "--app-id", var id] when AppId(id) is { } appId
            => () => SteamNativeWorkerHost.RunGameSessionAsync(appId),
        ["--steam-native-op", "achievements", "--app-id", var id] when AppId(id) is { } appId
            => () => SteamAchievementsWorkerHost.RunAchievementsAsync(appId),
        ["--steam-native-op", "achievement", "--app-id", var id, "--name", var name, "--achieved", "0" or "1"]
            when AppId(id) is { } appId
            => () => SteamAchievementsWorkerHost.RunAchievementAsync(appId, name, achieved: args[7] == "1"),
        _ => null,
    };

    private static int? AppId(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && appId > 0 ? appId : null;

    private static int Run(Func<Task<int>> worker)
    {
        try
        {
            return worker().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: --steam-native-worker | --steam-native-op <session|achievements|achievement> --app-id <positive-id> [--name <api-name> --achieved <0|1>]");
        return UsageExitCode;
    }
}
