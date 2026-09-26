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

        if (args is ["version"])
        {
            Console.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        Console.Error.WriteLine("Usage: StalkerSaveEditor.Cli version");
        return 2;
    }
}
