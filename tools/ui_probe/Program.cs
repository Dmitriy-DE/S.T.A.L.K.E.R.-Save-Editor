using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using StalkerSaveEditor.Desktop;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;

namespace StalkerSaveEditor.Tools.UiProbe;

/// <summary>
/// Developer checks of the interface without a window. They lived in the application's executable; the package now
/// ships only the checks CI runs against it (--screenshot, --test-i18n, --test-audio).
///   measure SAVE [SECONDS] [LANGUAGE]  longest gap between interface-thread ticks while a save is opened and the tabs are walked
///   companion PNG                      the Companion screen
///   wizard PNG                         the first-launch screen
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        StalkerSaveEditor.Host.DesktopHost.Register();
        GameAudioService.Silent = true;
        switch (args)
        {
            case ["measure", var save, ..]:
                var seconds = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : 10;
                return MeasureUi(save, seconds, args.Length > 3 ? args[3] : null);
            case ["companion", var png]:
                RenderCompanionScreenshot(png);
                return 0;
            case ["wizard", var png]:
                RenderWizardScreenshot(png);
                return 0;
            default:
                Console.Error.WriteLine("Usage: UiProbe measure SAVE [SECONDS] [LANGUAGE] | companion PNG | wizard PNG");
                return 2;
        }
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
        if (language is not null) I18nService.Instance.SetLanguage(language);

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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            steps[index++].Run();
            var set = watch.ElapsedMilliseconds;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            var layout = watch.ElapsedMilliseconds;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (Environment.GetEnvironmentVariable("SE_DEBUG_LAYOUT") == "1")
                Console.WriteLine($"  [{phase}] change {set} ms, layout {layout - set} ms, render {watch.ElapsedMilliseconds - layout} ms");
        });
        stepper.Start();
        var render = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AvaloniaHeadlessPlatform.ForceRenderTimerTick());
        render.Start();
        Dispatcher.UIThread.MainLoop(done.Token);
        var rows = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ListBoxItem>().Count();
        var controls = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).Count();
        window.Close();

        Console.WriteLine($"UI measure: {Path.GetFileName(savePath)}; language {language ?? "default"}; {seconds}s");
        foreach (var (name, gap) in perPhase) Console.WriteLine($"  {name}: {gap.TotalMilliseconds:0} ms");
        Console.WriteLine($"Controls on screen at the end: {controls}, list rows built: {rows} of {viewModel.FilteredInventory.Count} shown items");
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
        var view = new CompanionView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1000,
            Height = 700,
            Background = StalkerTheme.BrushBgBase,
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
