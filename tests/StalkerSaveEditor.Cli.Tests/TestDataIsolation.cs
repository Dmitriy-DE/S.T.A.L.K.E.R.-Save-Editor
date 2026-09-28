using System.Runtime.CompilerServices;

namespace StalkerSaveEditor.Cli.Tests;

internal static class TestDataIsolation
{
    /// <summary>Logs, drafts, caches and settings of the code under test go to a temporary folder, never the user's data folder.</summary>
    [ModuleInitializer]
    internal static void UseTemporaryDataFolder()
    {
        if (Environment.GetEnvironmentVariable("STALKER_SAVE_EDITOR_DATA") is { Length: > 0 }) return;
        var folder = Path.Combine(Path.GetTempPath(), "se-test-data-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("STALKER_SAVE_EDITOR_DATA", folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        };
    }
}
