using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Tests;

/// <summary>
/// Points the process-wide log at a private folder for one test and, when the test ends, points it back before the
/// folder is deleted. Other tests run in parallel and log too: without the reset they kept writing into a folder
/// that was being removed ("Directory not empty" in CI).
/// </summary>
internal sealed class TemporaryAppLog : IDisposable
{
    public TemporaryAppLog()
    {
        Directory = Path.Combine(Path.GetTempPath(), "se-logs-" + Guid.NewGuid().ToString("N"));
        AppLog.Configure(Directory);
    }

    public string Directory { get; }

    public void Dispose()
    {
        AppLog.Configure(null);
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
    }
}
