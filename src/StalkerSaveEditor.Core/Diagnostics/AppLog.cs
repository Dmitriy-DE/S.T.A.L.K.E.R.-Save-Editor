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
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "StalkerSaveEditor");

    public static string Logs => Path.Combine(DataDirectory, "logs");

    public static string Backups => Path.Combine(DataDirectory, "backups");

    public static string Drafts => Path.Combine(DataDirectory, "drafts");

    public static string ContentCache => Path.Combine(DataDirectory, "content");

    public static string Snapshots => Path.Combine(DataDirectory, "snapshots");

    public static string Profiles => Path.Combine(DataDirectory, "profiles");

    public static string ToolkitConfig => Path.Combine(DataDirectory, "toolkit-config");
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
            $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {Redact(message)}{(exception is null ? string.Empty : Environment.NewLine + Redact(exception.ToString()))}{Environment.NewLine}");
        // In the browser the log file is in memory only; the developer console is where it can be read.
        if (OperatingSystem.IsBrowser() && level != "INFO") Console.Error.Write(line);
        lock (Gate)
        {
            try
            {
                var directory = Directory;
                System.IO.Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, FileName);
                if (File.Exists(path) && new FileInfo(path).Length + System.Text.Encoding.UTF8.GetByteCount(line) > MaxBytes)
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
        text = WineUser().Replace(text, "drive_c/users/<user>");
        text = SteamId64().Replace(text, "<steamid>");
        return SteamUser().Replace(text, "userdata/<id>");
    }

    [GeneratedRegex(@"/(?:home|Users)/[^/\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex PosixHome();

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\\s]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WindowsHome();

    [GeneratedRegex(@"userdata[/\\]\d+", RegexOptions.CultureInvariant)]
    private static partial Regex SteamUser();

    [GeneratedRegex(@"drive_c[/\\]users[/\\][^/\\\s]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WineUser();

    [GeneratedRegex(@"\b7656119\d{10}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SteamId64();
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

    /// <summary>When the previous run's crash was recorded (UTC), or null.</summary>
    public static DateTime? PendingSinceUtc()
    {
        var path = Path.Combine(AppLog.Directory, CrashFileName);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
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
    private const int PartBytes = 256 * 1024;

    /// <summary>
    /// The last <paramref name="maximumBytes"/> of a text file, read from its end: a game or mod log can be gigabytes,
    /// and only its tail goes into the report. The first, possibly cut, line is dropped.
    /// </summary>
    internal static string ReadTail(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - maximumBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[(int)Math.Min(maximumBytes, stream.Length - start)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        if (start == 0) return text;
        var firstBreak = text.IndexOf('\n');
        return firstBreak >= 0 ? text[(firstBreak + 1)..] : text;
    }

    /// <param name="since">Only log lines written after this time (the daily report sends what is new).</param>
    public static byte[] Create(string? environmentReport = null, IEnumerable<string>? extraLogs = null, DateTime? since = null)
    {
        // The cap is in bytes of the final UTF-8 text, and every part counts against it: a log full of Cyrillic is
        // twice as large in bytes as in characters, and a huge crash text or environment report is cut like a log.
        var builder = new StringBuilder();
        var remaining = MaxBytes;
        void Add(string title, string text, int partLimit = MaxBytes)
        {
            if (text.Length == 0) return;
            var header = title.Length == 0 ? string.Empty : "--- " + title + " ---\n";
            var room = Math.Min(partLimit, remaining - Encoding.UTF8.GetByteCount(header) - 1);
            if (room <= 0) return;
            var kept = TailWithinBytes(text, room);
            builder.Append(header).Append(kept).Append('\n');
            remaining -= Encoding.UTF8.GetByteCount(header) + Encoding.UTF8.GetByteCount(kept) + 1;
        }

        Add(string.Empty, "S.T.A.L.K.E.R. Save Editor " + ApplicationVersion.Current + ", " +
            System.Runtime.InteropServices.RuntimeInformation.OSDescription + ", " +
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        if (CrashReporter.Pending() is { } crash) Add("crash", crash, PartBytes);
        if (!string.IsNullOrWhiteSpace(environmentReport)) Add("environment", AppLog.Redact(environmentReport), PartBytes);
        var directory = AppLog.Directory;
        foreach (var name in new[] { AppLog.FileName, AppLog.FileName + ".1", AppLog.FileName + ".2", AppLog.FileName + ".3" })
        {
            var path = Path.Combine(directory, name);
            if (remaining <= 0 || !File.Exists(path)) continue;
            try
            {
                Add(name, AppLog.Redact(since is { } after ? LinesAfter(File.ReadAllText(path), after) : File.ReadAllText(path)));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }

        // Other logs worth reading with ours, e.g. the experimental S.T.A.L.K.E.R. 2 mod's; last 256 KB each.
        foreach (var path in extraLogs ?? [])
        {
            if (remaining <= 0 || !File.Exists(path)) continue;
            try
            {
                Add(Path.GetFileName(path), AppLog.Redact(ReadTail(path, PartBytes)), PartBytes);
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

    /// <summary>The end of <paramref name="text"/> that fits in <paramref name="maximumBytes"/> of UTF-8, never splitting a character.</summary>
    internal static string TailWithinBytes(string text, int maximumBytes)
    {
        if (maximumBytes <= 0) return string.Empty;
        // Cheap bound first: a UTF-16 unit is at most 3 UTF-8 bytes.
        if (text.Length <= maximumBytes / 3 || Encoding.UTF8.GetByteCount(text) <= maximumBytes) return text;
        var start = text.Length;
        var bytes = 0;
        while (start > 0)
        {
            var status = Rune.DecodeLastFromUtf16(text.AsSpan(0, start), out var rune, out var consumed);
            var size = status == System.Buffers.OperationStatus.Done ? rune.Utf8SequenceLength : 3;
            if (bytes + size > maximumBytes) break;
            bytes += size;
            start -= consumed;
        }

        return text[start..];
    }

    /// <summary>The part of a log whose lines (each starts with its UTC time) are later than <paramref name="after"/>.</summary>
    internal static string LinesAfter(string log, DateTime after)
    {
        var start = 0;
        while (start < log.Length)
        {
            if (log.Length - start >= 24 &&
                DateTime.TryParseExact(log.AsSpan(start, 24), "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) &&
                time > after)
            {
                return log[start..];
            }

            var next = log.IndexOf('\n', start);
            if (next < 0) break;
            start = next + 1;
        }

        return string.Empty;
    }
}
