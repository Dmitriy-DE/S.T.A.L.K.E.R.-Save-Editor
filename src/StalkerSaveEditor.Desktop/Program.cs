using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--steam-native-worker"])
        {
            SteamNativeWorkerHost.RunAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Length > 0 && args[0] == "--screenshot")
        {
            string? outputPath = null;
            string? fixturePath = null;
            string tab = "overview";

            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--fixture" && i + 1 < args.Length)
                {
                    fixturePath = args[++i];
                }
                else if (args[i] == "--tab" && i + 1 < args.Length)
                {
                    tab = args[++i];
                }
                else if (outputPath is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    outputPath = args[i];
                }
            }

            if (outputPath is null || fixturePath is null)
            {
                throw new ArgumentException("Usage: --screenshot <png-path> --fixture <synthetic-save-path> [--tab <tab-name>]");
            }

            RenderScreenshot(outputPath, fixturePath, tab);
            return;
        }

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void RenderScreenshot(string outputPath, string fixturePath, string tab = "overview")
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var viewModel = new SaveLibraryViewModel(discoverLocalSaves: false);
        if (!viewModel.AddPreviewSave(fixturePath))
        {
            throw new InvalidDataException("The synthetic screenshot fixture was not recognized.");
        }

        viewModel.SelectedTab = tab;

        var window = new MainWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Avalonia headless renderer returned no frame.");

        var fullPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        frame.Save(fullPath);
        window.Close();
    }
}
