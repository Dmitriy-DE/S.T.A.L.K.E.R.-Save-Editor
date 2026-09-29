using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Starts UI-triggered background work without awaiting it. Any failure is logged instead of surfacing later as an
/// unobserved task exception from the finalizer thread (the 1.0.1 "Call from invalid thread" crash report).
/// </summary>
public static class BackgroundTask
{
    public static void Run(Task task, string what)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.ContinueWith(
            finished => AppLog.Error($"background {what} failed", finished.Exception?.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void Run(ValueTask task, string what) => Run(task.AsTask(), what);
}
