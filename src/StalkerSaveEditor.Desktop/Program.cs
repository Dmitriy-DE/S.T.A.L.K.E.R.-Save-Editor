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

        if (args.Length > 0 && args[0] == "--test-i18n")
        {
            var result = Services.I18nCompletenessChecker.Validate();
            Console.WriteLine($"i18n Validation: {(result.Success ? "PASSED" : "FAILED")}");
            Console.WriteLine($"Master messages count: {result.TotalMessages}");
            Console.WriteLine($"Checked locales: {result.CheckedLocales}");
            foreach (var (lang, count) in result.TranslatedCounts)
            {
                Console.WriteLine($"  {lang}: {count}/{result.TotalMessages} translated");
            }
            if (!result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Errors ({result.Errors.Count}):");
                foreach (var err in result.Errors.Take(20))
                {
                    Console.WriteLine($"  - {err}");
                }
                Console.ResetColor();
                Environment.Exit(1);
            }
            Environment.Exit(0);
        }

        if (args.Length > 0 && args[0] == "--test-audio")
        {
            var result = Services.GameAudioValidator.Validate();
            Console.WriteLine($"Audio Validation: {(result.Success ? "PASSED" : "FAILED")}");
            Console.WriteLine($"Verified sounds: {result.VerifiedSounds}");
            if (!result.Success)
            {
                foreach (var err in result.Errors)
                {
                    Console.WriteLine($"  Error: {err}");
                }
                Environment.Exit(1);
            }
            Environment.Exit(0);
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
}
