namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// What differs between the desktop host and the web host. In the browser the saves the user opens
/// are copied into the in-memory file system (<see cref="OpenedSavesDirectory"/>), every feature that
/// needs the local machine (Steam, the companion, updates, save discovery) is hidden, and a saved file
/// is handed back to the user as a download.
/// </summary>
public static class HostPlatform
{
    public static bool IsBrowser => OperatingSystem.IsBrowser();

    /// <summary>Browser: where opened saves live in the in-memory file system.</summary>
    public static string OpenedSavesDirectory => Path.Combine(Path.GetTempPath(), "saves");

    /// <summary>Web host: files imported before the interface started (a <c>?open=</c> link); opened on start.</summary>
    public static List<string> StartupFiles { get; } = [];

    /// <summary>Set by the web host: reads a file published next to the page (icons), null when missing.</summary>
    public static Func<string, Task<byte[]?>>? FetchAsset { get; set; }

    /// <summary>Set by the web host: offers a written file to the user as a download.</summary>
    public static Func<string, Task>? ExportFile { get; set; }

    /// <summary>Copies an opened file into <see cref="OpenedSavesDirectory"/> and returns its path there.</summary>
    public static async Task<string> ImportAsync(Stream source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(source);
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)) name = "save.sav";
        Directory.CreateDirectory(OpenedSavesDirectory);
        var path = Path.Combine(OpenedSavesDirectory, name);
        await using var target = File.Create(path);
        await source.CopyToAsync(target).ConfigureAwait(false);
        return path;
    }
}
