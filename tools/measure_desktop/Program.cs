using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using StalkerSaveEditor.Desktop;
using StalkerSaveEditor.Desktop.ViewModels;

internal static class Program
{
    private const int OpenCount = 20;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MeasureDesktop <temporary-root> <fixture.sav> [fixture.sav ...] | --launch-only");
            return 2;
        }

        var temporaryRoot = Path.GetFullPath(args[0]);
        var launchOnly = args.Length == 2 && args[1] == "--launch-only";
        string[] fixtures = launchOnly ? [] : args[1..].Select(static path => Path.GetFullPath(path)).ToArray();
        if (!launchOnly && fixtures.Length == 0)
        {
            Console.Error.WriteLine("At least one synthetic save fixture is required.");
            return 2;
        }

        var result = RunScenario(temporaryRoot, fixtures, launchOnly);
        var managedAfterDesktopCloseBytes = CollectManagedBytes();
        using var process = Process.GetCurrentProcess();
        var workingSetAfterDesktopCloseBytes = process.WorkingSet64;

        Console.WriteLine($"desktop_startup_ms={result.StartupMilliseconds:F2}");
        Console.WriteLine($"scenario_opens={result.OpenCount}");
        Console.WriteLine($"fixture_formats={fixtures.Length}");
        Console.WriteLine($"managed_idle_after_gc_bytes={result.IdleManagedBytes}");
        Console.WriteLine($"managed_after_first_close_bytes={result.ManagedAfterFirstCloseBytes}");
        Console.WriteLine($"managed_after_20_closes_bytes={result.ManagedAfter20ClosesBytes}");
        Console.WriteLine($"managed_growth_first_to_20_closes_bytes={result.ManagedAfter20ClosesBytes - result.ManagedAfterFirstCloseBytes}");
        Console.WriteLine($"managed_after_desktop_close_after_gc_bytes={managedAfterDesktopCloseBytes}");
        Console.WriteLine($"managed_retained_after_desktop_close_bytes={managedAfterDesktopCloseBytes - result.IdleManagedBytes}");
        Console.WriteLine($"working_set_idle_bytes={result.IdleWorkingSetBytes}");
        Console.WriteLine($"sampled_peak_working_set_bytes={result.SampledPeakWorkingSetBytes}");
        Console.WriteLine($"working_set_after_desktop_close_bytes={workingSetAfterDesktopCloseBytes}");
        return 0;
    }

    private static ScenarioResult RunScenario(string temporaryRoot, string[] fixtures, bool launchOnly)
    {
        var startupTimer = Stopwatch.StartNew();
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var idleViewModel = CreateViewModel(temporaryRoot);
        var idleWindow = new MainWindow(idleViewModel);
        idleWindow.Show();
        Render(idleWindow);
        startupTimer.Stop();

        using var process = Process.GetCurrentProcess();
        var idleManagedBytes = CollectManagedBytes();
        var idleWorkingSetBytes = process.WorkingSet64;
        var sampledPeakWorkingSetBytes = idleWorkingSetBytes;
        var managedAfterFirstCloseBytes = idleManagedBytes;
        var managedAfter20ClosesBytes = idleManagedBytes;

        if (!launchOnly)
        {
            var cycleViewModel = CreateViewModel(temporaryRoot);
            for (var index = 0; index < OpenCount; index++)
            {
                var fixture = fixtures[index % fixtures.Length];
                if (!cycleViewModel.AddPreviewSave(fixture) || cycleViewModel.SelectedSave is null)
                {
                    throw new InvalidDataException($"Desktop did not recognize synthetic fixture {Path.GetFileName(fixture)}.");
                }

                sampledPeakWorkingSetBytes = Math.Max(sampledPeakWorkingSetBytes, process.WorkingSet64);
                cycleViewModel.SelectedSave = null;
                cycleViewModel.Saves.Clear();
                Dispatcher.UIThread.RunJobs();

                var managedAfterCloseBytes = CollectManagedBytes();
                if (index == 0)
                {
                    managedAfterFirstCloseBytes = managedAfterCloseBytes;
                }

                managedAfter20ClosesBytes = managedAfterCloseBytes;
            }

            var displayViewModel = CreateViewModel(temporaryRoot);
            if (!displayViewModel.AddPreviewSave(fixtures[^1]))
            {
                throw new InvalidDataException("Desktop could not render the final synthetic save.");
            }

            var displayWindow = new MainWindow(displayViewModel);
            displayWindow.Show();
            Render(displayWindow);
            sampledPeakWorkingSetBytes = Math.Max(sampledPeakWorkingSetBytes, process.WorkingSet64);
            displayWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        idleWindow.Close();
        Dispatcher.UIThread.RunJobs();
        return new ScenarioResult(
            startupTimer.Elapsed.TotalMilliseconds,
            launchOnly ? 0 : OpenCount,
            idleManagedBytes,
            managedAfterFirstCloseBytes,
            managedAfter20ClosesBytes,
            idleWorkingSetBytes,
            sampledPeakWorkingSetBytes);
    }

    private static SaveLibraryViewModel CreateViewModel(string temporaryRoot) => new(
        discoverLocalSaves: false,
        saveDirectoriesProvider: static () => [],
        backupDirectoryProvider: () => Path.Combine(temporaryRoot, "backups"),
        draftsDirectory: Path.Combine(temporaryRoot, "drafts"));

    private static long CollectManagedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static void Render(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Avalonia headless renderer returned no frame.");
    }

    private sealed record ScenarioResult(
        double StartupMilliseconds,
        int OpenCount,
        long IdleManagedBytes,
        long ManagedAfterFirstCloseBytes,
        long ManagedAfter20ClosesBytes,
        long IdleWorkingSetBytes,
        long SampledPeakWorkingSetBytes);
}
