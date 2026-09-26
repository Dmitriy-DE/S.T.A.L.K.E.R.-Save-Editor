using StalkerSaveEditor.Core;

namespace StalkerSaveEditor.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["version"])
        {
            Console.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        Console.Error.WriteLine("Usage: StalkerSaveEditor.Cli version");
        return 2;
    }
}
