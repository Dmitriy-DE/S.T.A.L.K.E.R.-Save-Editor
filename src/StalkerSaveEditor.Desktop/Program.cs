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

        if (args.Length > 0 && args[0] == "--screenshot-companion")
        {
            var outPath = args.Length > 1 ? args[1] : "companion.png";
            RenderCompanionScreenshot(outPath);
            return;
        }

        if (args.Length > 0 && args[0] == "--screenshot-wizard")
        {
            var outPath = args.Length > 1 ? args[1] : "first-launch-wizard.png";
            RenderWizardScreenshot(outPath);
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

        ViewModels.SaveLibraryViewModel.InteractiveApp = true;
        Core.Diagnostics.CrashReporter.Install();
        Core.Diagnostics.AppLog.Info("start " + Core.ApplicationVersion.Current);
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
        if (Environment.GetEnvironmentVariable("SE_DEBUG_LAYOUT") == "1")
            foreach (var v in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
                if (v.Bounds.Width > 1000) Console.WriteLine($"{v.GetType().Name} {v.Bounds} depth");
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Avalonia headless renderer returned no frame.");

        var fullPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        frame.Save(fullPath);
        window.Close();
    }

    private static void RenderWizardScreenshot(string outputPath)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var viewModel = new SaveLibraryViewModel(discoverLocalSaves: false);
        var window = new MainWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        if (Environment.GetEnvironmentVariable("SE_DEBUG_LAYOUT") == "1")
            foreach (var v in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
                if (v.Bounds.Width > 1000) Console.WriteLine($"{v.GetType().Name} {v.Bounds} depth");
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Avalonia headless renderer returned no frame.");

        var fullPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        frame.Save(fullPath);
        window.Close();
    }
}
