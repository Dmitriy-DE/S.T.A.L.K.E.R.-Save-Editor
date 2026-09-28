using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Diagnostics;

/// <summary>Where the application keeps its own data (never the game's folders).</summary>
public static class AppPaths
{
    /// <summary>Root for backups, drafts, logs and caches; overridable for tests and portable mode.</summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("STALKER_SAVE_EDITOR_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StalkerSaveEditor");

    public static string Logs => Path.Combine(DataDirectory, "logs");

    public static string Backups => Path.Combine(DataDirectory, "backups");

    public static string Drafts => Path.Combine(DataDirectory, "drafts");

    public static string ContentCache => Path.Combine(DataDirectory, "content");
}

/// <summary>
/// Small rotating file log (port of the Python oracle's diagnostics logging): one file capped in
/// size with a few rotations, UTF-8, thread-safe. Nothing here ever throws into the caller.
/// </summary>
public static partial class AppLog
{
    public const string FileName = "save-editor.log";
    private const long MaxBytes = 1024 * 1024;
    private const int Rotations = 3;
    private static readonly Lock Gate = new();
    private static string? _directory;

    public static string Directory => _directory ?? AppPaths.Logs;

    /// <summary>Points the log at a directory (tests, portable mode). Default: <see cref="AppPaths.Logs"/>.</summary>
    public static void Configure(string directory)
    {
        lock (Gate)
        {
            _directory = directory;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}");
        // In the browser the log file is in memory only; the developer console is where it can be read.
        if (OperatingSystem.IsBrowser() && level != "INFO") Console.Error.Write(line);
        lock (Gate)
        {
            try
            {
                var directory = Directory;
                System.IO.Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, FileName);
                if (File.Exists(path) && new FileInfo(path).Length + line.Length > MaxBytes)
                {
                    Rotate(path);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the application.
            }
        }
    }

    private static void Rotate(string path)
    {
        for (var index = Rotations - 1; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            if (File.Exists(source)) File.Move(source, $"{path}.{index + 1}", overwrite: true);
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    /// <summary>Replaces the user's home directory and user names in paths with &lt;home&gt;.</summary>
    public static string Redact(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/', '\\');
        if (home.Length > 1) text = text.Replace(home, "<home>", StringComparison.OrdinalIgnoreCase);
        text = PosixHome().Replace(text, "<home>");
        text = WindowsHome().Replace(text, "<home>");
        return SteamUser().Replace(text, "userdata/<id>");
    }

    [GeneratedRegex(@"/(?:home|Users)/[^/\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex PosixHome();

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\\s]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WindowsHome();

    [GeneratedRegex(@"userdata[/\\]\d+", RegexOptions.CultureInvariant)]
    private static partial Regex SteamUser();
}

/// <summary>Records unhandled errors to the log and to <c>last-crash.txt</c> so the next start can offer a report.</summary>
public static class CrashReporter
{
    public const string CrashFileName = "last-crash.txt";
    private const int MaxCrashChars = 64 * 1024;
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Record("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    public static void Record(string context, Exception? exception)
    {
        AppLog.Error(context, exception);
        try
        {
            System.IO.Directory.CreateDirectory(AppLog.Directory);
            var text = AppLog.Redact(string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.UtcNow:O} {context}{Environment.NewLine}{exception}"));
            File.WriteAllText(Path.Combine(AppLog.Directory, CrashFileName), text.Length > MaxCrashChars ? text[..MaxCrashChars] : text);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The crash left by the previous run, if any.</summary>
    public static string? Pending()
    {
        var path = Path.Combine(AppLog.Directory, CrashFileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Dismiss()
    {
        try
        {
            File.Delete(Path.Combine(AppLog.Directory, CrashFileName));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A redacted, size-capped gzip of the logs, the last crash and an environment report for a bug report.</summary>
public static class DiagnosticsBundle
{
    private const int MaxBytes = 2 * 1024 * 1024;

    public static byte[] Create(string? environmentReport = null)
    {
        var builder = new StringBuilder();
        builder.Append("S.T.A.L.K.E.R. Save Editor ").Append(ApplicationVersion.Current).Append(", ")
            .Append(System.Runtime.InteropServices.RuntimeInformation.OSDescription).Append(", ")
            .Append(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture).Append('\n');
        if (CrashReporter.Pending() is { } crash) builder.Append("--- crash ---\n").Append(crash).Append('\n');
        if (!string.IsNullOrWhiteSpace(environmentReport)) builder.Append("--- environment ---\n").Append(AppLog.Redact(environmentReport)).Append('\n');
        var directory = AppLog.Directory;
        foreach (var name in new[] { AppLog.FileName, AppLog.FileName + ".1", AppLog.FileName + ".2", AppLog.FileName + ".3" })
        {
            var path = Path.Combine(directory, name);
            if (builder.Length >= MaxBytes || !File.Exists(path)) continue;
            try
            {
                var text = AppLog.Redact(File.ReadAllText(path));
                var room = MaxBytes - builder.Length;
                builder.Append("--- ").Append(name).Append(" ---\n").Append(text.Length > room ? text[^room..] : text).Append('\n');
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    /// <summary>Writes the bundle to <paramref name="destination"/> (a .txt.gz the user can attach to an issue).</summary>
    public static string Export(string destination, string? environmentReport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (System.IO.Directory.Exists(destination)) throw new IOException("The export path is a directory.");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllBytes(destination, Create(environmentReport));
        return destination;
    }
}
