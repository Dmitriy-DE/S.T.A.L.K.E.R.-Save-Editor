using Avalonia;

namespace StalkerSaveEditor.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
