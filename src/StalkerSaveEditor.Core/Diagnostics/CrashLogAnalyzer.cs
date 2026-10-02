using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Core.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<CrashLogKind>))]
public enum CrashLogKind
{
    Unknown,
    LuaError,
    FatalError,
    EngineError,
}

/// <summary>Facts extracted from one user-selected game log. No match means no fix is recommended.</summary>
public sealed record CrashLogAnalysis(
    string? Game,
    CrashLogKind Kind,
    string Summary,
    string? File,
    int? Line,
    DateTimeOffset? FileLastWriteTimeUtc,
    string? Exception,
    string? KnownIssueId,
    IReadOnlyList<string> Evidence)
{
    /// <summary>The documented crash this log matches, when one does.</summary>
    public CrashSignature? KnownIssue { get; init; }
}

/// <summary>
/// Parses explicit log text without treating generic engine messages as known bugs. KnownIssueId is set only
/// when the log contains a message quoted by <see cref="CrashSignatureCatalog"/>.
/// </summary>
public static partial class CrashLogAnalyzer
{
    /// <summary>
    /// Analyzes a log file or a game minidump (<c>*.mdmp</c>), told apart by content. Only the last 256 KiB of a log
    /// are read; a dump larger than 64 MiB is refused.
    /// </summary>
    public static CrashLogAnalysis AnalyzeFile(string path, string? game = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        using var stream = info.OpenRead();
        Span<byte> head = stackalloc byte[32];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (CrashDumpReader.IsMinidump(head[..read]))
        {
            const long maximumDump = 64L * 1024 * 1024;
            if (stream.Length > maximumDump) throw new InvalidDataException("The dump file is too large to inspect.");
            var dump = new byte[stream.Length];
            stream.Position = 0;
            stream.ReadExactly(dump);
            return Analyze(CrashDumpReader.ToLogText(dump), game, modified);
        }

        const int tail = 256 * 1024;
        stream.Position = Math.Max(0, stream.Length - tail);
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
        return Analyze(reader.ReadToEnd(), game, modified);
    }

    public static CrashLogAnalysis Analyze(string text, string? game = null, DateTimeOffset? fileLastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var fatal = FatalMarkerRegex().IsMatch(text);
        var lua = LuaMarkerRegex().IsMatch(text);
        var engine = EngineMarkerRegex().IsMatch(text);
        var kind = fatal ? CrashLogKind.FatalError
            : lua ? CrashLogKind.LuaError
            : engine ? CrashLogKind.EngineError
            : CrashLogKind.Unknown;

        var expression = FindValue(lines, "Expression");
        var description = FindValue(lines, "Description");
        var exception = FindValue(lines, "Exception") ?? FindValue(lines, "Function");
        var file = FindValue(lines, "File");
        var line = ParseLine(FindValue(lines, "Line"));
        if (file is null || line is null)
        {
            var frame = ScriptFrameRegex().Match(text);
            if (frame.Success)
            {
                file ??= frame.Groups["file"].Value.Trim();
                line ??= ParseLine(frame.Groups["line"].Value);
            }
        }

        var summary = kind switch
        {
            // "fatal error" / "<no expression>" are the engine's placeholders: the Arguments line then holds the message.
            CrashLogKind.FatalError => FirstNonEmpty(
                Meaningful(expression), Meaningful(description), FindValue(lines, "Arguments"), expression, description, "Fatal error marker found."),
            CrashLogKind.LuaError => FindLuaSummary(lines),
            CrashLogKind.EngineError => FirstNonEmpty(FindEngineSummary(lines), "Engine error marker found."),
            _ => "No recognized crash marker was found.",
        };

        var evidence = lines
            .Where(lineText => FatalFieldRegex().IsMatch(lineText) || LuaMarkerRegex().IsMatch(lineText) || EngineMarkerRegex().IsMatch(lineText))
            .Select(lineText => lineText.Trim())
            .Where(lineText => lineText.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToArray();

        var known = CrashSignatureCatalog.Match(text, game);
        return new CrashLogAnalysis(
            string.IsNullOrWhiteSpace(game) ? null : game.Trim(),
            kind,
            summary,
            string.IsNullOrWhiteSpace(file) ? null : file.Trim(),
            line,
            fileLastWriteTimeUtc,
            exception,
            KnownIssueId: known?.Id,
            Array.AsReadOnly(evidence))
        {
            KnownIssue = known,
        };
    }

    private static string? FindValue(IEnumerable<string> lines, string field)
    {
        var pattern = new Regex($@"^\s*(?:\[error\]\s*)?{Regex.Escape(field)}\s*:\s*(.*?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return lines.Select(line => pattern.Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
    }

    private static string? Meaningful(string? value) =>
        value is null || value.Trim() is "fatal error" or "<no expression>" or "" ? null : value;

    private static int? ParseLine(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FindLuaSummary(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("[LUA][ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.TrimStart('!', ' ');
            }

            if (trimmed.Length == 0 || LuaMarkerRegex().IsMatch(trimmed) || trimmed.StartsWith("stack traceback", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.Contains("attempt to", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("script error", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("nil value", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        return "Lua error marker found.";
    }

    private static string? FindEngineSummary(IEnumerable<string> lines) => lines
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Unhandled Exception", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("DEVICE_REMOVED", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(?im)^\s*(?:FATAL ERROR|\[error\]\s*(?:Expression|Function|File|Line|Description)\s*:)", RegexOptions.CultureInvariant)]
    private static partial Regex FatalMarkerRegex();

    [GeneratedRegex(@"(?im)(?:\[LUA\]\s*SCRIPT\s*ERROR|\[LUA\]\s*\[ERROR\]|LUA\s+error\s*:|lua\s+error\s*:)", RegexOptions.CultureInvariant)]
    private static partial Regex LuaMarkerRegex();

    [GeneratedRegex(@"(?im)(?:EXCEPTION_ACCESS_VIOLATION|Unhandled Exception|DEVICE_REMOVED)", RegexOptions.CultureInvariant)]
    private static partial Regex EngineMarkerRegex();

    [GeneratedRegex(@"(?im)^\s*\[error\]\s*(?:Expression|Function|File|Line|Description|Arguments?(?: \d)?)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex FatalFieldRegex();

    [GeneratedRegex(@"(?im)(?<file>(?:[A-Za-z]:)?[^\r\n:]*?\.script):(?<line>\d+)(?::|\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptFrameRegex();
}
