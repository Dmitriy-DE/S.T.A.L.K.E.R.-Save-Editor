using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Desktop;

internal static class Program
{
    /// <summary>This executable started as the X11 hotkey helper (through the dotnet host when run from a build folder).</summary>
    private static System.Diagnostics.ProcessStartInfo? HelperStartInfo()
    {
        if (Environment.ProcessPath is not { Length: > 0 } executable) return null;
        var info = new System.Diagnostics.ProcessStartInfo(executable);
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(entry)) return null;
            info.ArgumentList.Add(entry);
        }

        info.ArgumentList.Add(StalkerSaveEditor.Core.Hotkeys.X11HotkeyHelper.Argument);
        return info;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args is [StalkerSaveEditor.Core.Hotkeys.X11HotkeyHelper.Argument])
        {
            // Before anything touches the UI toolkit: this process only talks to its own X display.
            Environment.Exit(StalkerSaveEditor.Core.Hotkeys.X11HotkeyHelper.Run(Console.In, Console.Out));
            return;
        }

        if (args is ["--steam-native-worker"])
        {
            SteamNativeWorkerHost.RunAsync().GetAwaiter().GetResult();
            return;
        }

        // Steam and self-update exist only in the installed application; the UI library asks the host for them.
        StalkerSaveEditor.Host.DesktopHost.Register();
        if (OperatingSystem.IsLinux() && HelperStartInfo() is { } helper)
        {
            StalkerSaveEditor.Core.Hotkeys.X11HotkeyHelper.UseHelperProcess(() => HelperStartInfo() ?? helper);
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

        if (args.Length >= 2 && args[0] == "--measure-ui")
        {
            var seconds = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : 10;
            var language = args.Length > 3 ? args[3] : null;
            Environment.Exit(MeasureUi(args[1], seconds, language));
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
        Core.Diagnostics.AppLog.Info($"start {Core.ApplicationVersion.Current} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, .NET {Environment.Version}, UI culture {System.Globalization.CultureInfo.CurrentUICulture.Name}");
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

    /// <summary>
    /// ST-3: opens the app headless with one save selected (optionally after a language switch), walks the
    /// main tabs, and reports the longest gap between 16 ms UI-thread ticks. Exit code 1 above 100 ms.
    /// </summary>
    private static int MeasureUi(string savePath, int seconds, string? language)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        if (language is not null) Services.I18nService.Instance.SetLanguage(language);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = clock.Elapsed;
        var worst = TimeSpan.Zero;
        var worstAt = string.Empty;
        var phase = "window start";
        var perPhase = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) =>
        {
            var now = clock.Elapsed;
            var gap = now - last;
            if (!perPhase.TryGetValue(phase, out var max) || gap > max) perPhase[phase] = gap;
            if (gap > worst && phase != "window start")
            {
                worst = gap;
                worstAt = phase;
            }
            last = now;
        });
        timer.Start();

        var viewModel = new SaveLibraryViewModel(discoverLocalSaves: false);
        var window = new MainWindow(viewModel);
        window.Show();
        Task opening = Task.CompletedTask;
        var steps = new (string Name, Action Run)[]
        {
            ("open save", () => opening = viewModel.AddPreviewSaveAsync(savePath)),
            ("inventory tab", () => viewModel.SelectedTab = AppTabs.Inventory),
            ("stashes tab", () => viewModel.SelectedTab = AppTabs.Stashes),
            ("overview tab", () => viewModel.SelectedTab = AppTabs.Overview),
            ("inventory tab again", () => viewModel.SelectedTab = AppTabs.Inventory),
        };
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var index = 0;
        var stepper = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) =>
        {
            if (index >= steps.Length || !opening.IsCompleted) return;
            phase = steps[index].Name;
            steps[index++].Run();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        });
        stepper.Start();
        var render = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AvaloniaHeadlessPlatform.ForceRenderTimerTick());
        render.Start();
        Dispatcher.UIThread.MainLoop(done.Token);
        window.Close();

        Console.WriteLine($"UI measure: {Path.GetFileName(savePath)}; language {language ?? "default"}; {seconds}s");
        foreach (var (name, gap) in perPhase) Console.WriteLine($"  {name}: {gap.TotalMilliseconds:0} ms");
        Console.WriteLine($"Longest UI-thread gap after the window is up: {worst.TotalMilliseconds:0} ms (during: {worstAt})");
        return worst.TotalMilliseconds > 100 ? 1 : 0;
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
