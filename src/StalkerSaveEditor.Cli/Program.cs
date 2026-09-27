using StalkerSaveEditor.Core;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--steam-native-worker"])
        {
            return SteamNativeWorkerHost.RunAsync().GetAwaiter().GetResult();
        }

        if (args.Length >= 2 && args[0] == "--steam-native-op" && args[1] == "session")
        {
            var appId = ReadAppId(args[2..]);
            return appId is null
                ? 2
                : SteamNativeWorkerHost.RunGameSessionAsync(appId.Value).GetAwaiter().GetResult();
        }

        if (args is ["version"])
        {
            Console.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        Console.Error.WriteLine("Usage: StalkerSaveEditor.Cli version");
        return 2;
    }

    private static int? ReadAppId(string[] args)
    {
        if (args.Length != 2 || args[0] != "--app-id" ||
            !int.TryParse(args[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var appId) ||
            appId <= 0)
        {
            Console.Error.WriteLine("Usage: StalkerSaveEditor.Cli --steam-native-op session --app-id <positive-id>");
            return null;
        }

        return appId;
    }
}
