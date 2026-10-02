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

    /// <summary>
    /// Desktop-only services (Steam cloud, achievements, self-update), supplied by the host application. The UI
    /// library has no reference to Steam or the updater; a host that sets nothing (the web edition) gets the
    /// "not available" implementations.
    /// </summary>
    public static Func<ICloudServiceAdapter>? CreateCloudService { get; set; }

    public static Func<ISteamAchievementsAdapter>? CreateAchievementsService { get; set; }

    public static Func<IUpdateServiceAdapter>? CreateUpdateService { get; set; }

    /// <summary>
    /// The web edition keeps opened saves in memory, and parsing allocates the unpacked save on top, so an opened file
    /// gets a budget well below the format ceiling.
    /// </summary>
    public const long MaximumImportBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Copies an opened file into its own folder under <see cref="OpenedSavesDirectory"/> and returns its path there.
    /// Each import gets a fresh folder, so two saves with the same name (quicksave.sav from two games) never overwrite
    /// each other; the file keeps its name for display.
    /// </summary>
    public static async Task<string> ImportAsync(Stream source, string fileName, long maximumBytes = MaximumImportBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "save.sav";
        var directory = Path.Combine(OpenedSavesDirectory, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        try
        {
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                    throw new IOException($"The file is larger than {maximumBytes / (1024 * 1024)} MiB.");
                await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }
        }
        catch
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return path;
    }
}
