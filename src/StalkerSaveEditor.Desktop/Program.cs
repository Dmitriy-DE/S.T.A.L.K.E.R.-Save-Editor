using Avalonia;
using Avalonia.Controls;
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

        if (args.Length > 0 && args[0] == "--screenshot-companion")
        {
            var outPath = args.Length > 1 ? args[1] : "companion.png";
            RenderCompanionScreenshot(outPath);
            return;
        }

        if (args.Length > 0 && args[0] == "--screenshot")
        {
            if (args.Length != 4 || args[2] != "--fixture")
            {
                throw new ArgumentException("Usage: --screenshot <png-path> --fixture <synthetic-save-path>");
            }

            RenderScreenshot(args[1], args[3]);
            return;
        }

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void RenderScreenshot(string outputPath, string fixturePath)
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

    private static void RenderCompanionScreenshot(string outputPath)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var viewModel = new CompanionViewModel();
        var view = new Views.CompanionView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1000,
            Height = 700,
            Background = Styles.StalkerTheme.BrushBgBase,
            Content = view,
        };
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
