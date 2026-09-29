using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Companion;

public sealed class CompanionProtocolClient
{
    private const string ProtocolVersion = "v1";
    private const string CommandFileName = "save_editor_cmd.txt";
    private const string TemporaryCommandFileName = "save_editor_cmd.tmp";
    private const string ReplyFileName = "save_editor_out.txt";
    private static readonly Encoding ProtocolEncoding = CreateProtocolEncoding();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SendGates = new(PathComparer);
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollInterval;
    private readonly Func<string> _createId;

    public CompanionProtocolClient(
        string gameDirectory,
        string fsgameFileName = "fsgame.ltx",
        TimeSpan? timeout = null)
        : this(
            new ResolvedAppDataRoot(CompanionAppDataRootResolver.Resolve(gameDirectory, fsgameFileName)),
            timeout ?? TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(50),
            static () => Guid.NewGuid().ToString("N"))
    {
    }

    internal CompanionProtocolClient(
        string gameDirectory,
        TimeSpan timeout,
        TimeSpan pollInterval,
        Func<string> createId)
        : this(
            new ResolvedAppDataRoot(CompanionAppDataRootResolver.Resolve(gameDirectory)),
            timeout,
            pollInterval,
            createId)
    {
    }

    private CompanionProtocolClient(
        ResolvedAppDataRoot appDataRoot,
        TimeSpan timeout,
        TimeSpan pollInterval,
        Func<string> createId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot.Path);
        ArgumentNullException.ThrowIfNull(createId);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Protocol timeout must be positive.");
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Polling interval must be positive.");
        }

        AppDataDirectory = Path.GetFullPath(appDataRoot.Path);
        _timeout = timeout;
        _pollInterval = pollInterval;
        _createId = createId;
    }

    public string AppDataDirectory { get; }

    public async Task<CompanionProtocolReply> SendAsync(
        string command,
        IReadOnlyList<string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var formattedCommand = FormatCommand(command, arguments ?? Array.Empty<string>());
        var gate = SendGates.GetOrAdd(AppDataDirectory, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var commandPath = Path.Combine(AppDataDirectory, CommandFileName);
            var temporaryPath = Path.Combine(AppDataDirectory, TemporaryCommandFileName);
            var replyPath = Path.Combine(AppDataDirectory, ReplyFileName);
            Directory.CreateDirectory(AppDataDirectory);
            if (File.Exists(commandPath) || File.Exists(temporaryPath))
            {
                throw new CompanionCommandPendingException(
                    "The game has not taken the previous companion command yet.");
            }

            var id = _createId();
            if (!CompanionProtocolId.IsValid(id))
            {
                throw new CompanionProtocolException("The generated companion request ID is not a valid token.");
            }

            var line = $"{ProtocolVersion} {id} {formattedCommand}\n";
            PublishCommand(temporaryPath, commandPath, line);
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < _timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(replyPath) && TryReadReplyText(replyPath, out var replyText))
                {
                    if (CompanionProtocolReply.TryGetId(replyText, out var replyId) &&
                        string.Equals(replyId, id, StringComparison.Ordinal))
                    {
                        var reply = CompanionProtocolReply.Parse(replyText);
                        return reply with { ReplyFileLastWriteTimeUtc = GetReplyLastWriteTimeUtc(replyPath) };
                    }
                }

                var remaining = _timeout - elapsed.Elapsed;
                await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            CancelCommandIfStillOwned(commandPath, id);
            throw new CompanionProtocolTimeoutException(id, _timeout);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string FormatCommand(string command, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Any(argument => argument is null || !IsSingleToken(argument)))
        {
            throw new ArgumentException("Companion command arguments must be non-empty single tokens.", nameof(arguments));
        }

        switch (command)
        {
            case "ping":
            case "info":
            case "heal":
            case "repair_equipped":
            case "list_inventory":
            case "mark":
            case "jump_last":
                RequireArgumentCount(command, arguments, 0, 0);
                break;
            case "give":
                RequireArgumentCount(command, arguments, 1, 2);
                if (arguments.Count == 2 &&
                    (!int.TryParse(arguments[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var count) ||
                     count is < 1 or > 100))
                {
                    throw new ArgumentException("The give count must be between 1 and 100.", nameof(arguments));
                }

                break;
            case "money":
                RequireArgumentCount(command, arguments, 1, 1);
                if (!int.TryParse(arguments[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                {
                    throw new ArgumentException("The money delta must be a signed 32-bit integer.", nameof(arguments));
                }

                break;
            case "teleport":
                RequireArgumentCount(command, arguments, 3, 3);
                foreach (var coordinate in arguments)
                {
                    if (!float.TryParse(coordinate, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                        !float.IsFinite(value))
                    {
                        throw new ArgumentException("Teleport coordinates must be finite invariant-culture numbers.", nameof(arguments));
                    }
                }

                break;
            case "weather":
                RequireArgumentCount(command, arguments, 0, 2);
                if (arguments.Count > 0 && !IsSingleToken(arguments[0]) ||
                    arguments.Count == 2 && !string.Equals(arguments[1], "now", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Weather accepts an optional section and the literal 'now'.", nameof(arguments));
                }

                break;
            case "god":
            case "noclip":
                RequireArgumentCount(command, arguments, 1, 1);
                if (arguments[0] is not ("on" or "off"))
                {
                    throw new ArgumentException($"{command} accepts only 'on' or 'off'.", nameof(arguments));
                }

                break;
            case "timespeed":
                RequireArgumentCount(command, arguments, 1, 1);
                if (!float.TryParse(arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ||
                    !float.IsFinite(speed) || speed is < 0 or > 100)
                {
                    throw new ArgumentException("Time speed must be a number from 0 to 100.", nameof(arguments));
                }

                break;
            case "quicksave":
                RequireArgumentCount(command, arguments, 0, 1);
                break;
            case "hotkeys":
                RequireArgumentCount(command, arguments, 1, 1);
                if (arguments[0] is not ("on" or "off"))
                {
                    throw new ArgumentException("Hotkeys accepts only 'on' or 'off'.", nameof(arguments));
                }

                break;
            default:
                throw new ArgumentException($"'{command}' is not a command in companion protocol v1.", nameof(command));
        }

        return arguments.Count == 0 ? command : $"{command} {string.Join(' ', arguments)}";
    }

    private static void RequireArgumentCount(string command, IReadOnlyCollection<string> arguments, int minimum, int maximum)
    {
        if (arguments.Count < minimum || arguments.Count > maximum)
        {
            throw new ArgumentException(
                $"Command '{command}' expects {minimum}{(minimum == maximum ? string.Empty : $" to {maximum}")} argument(s).",
                nameof(arguments));
        }
    }

    private static bool IsSingleToken(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));

    private static DateTimeOffset? GetReplyLastWriteTimeUtc(string replyPath)
    {
        var replyFile = new FileInfo(replyPath);
        return replyFile.Exists
            ? new DateTimeOffset(replyFile.LastWriteTimeUtc, TimeSpan.Zero)
            : null;
    }

    internal static FileStream OpenReplyReadStream(string replyPath) =>
        new(replyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static bool TryReadReplyText(string replyPath, out string replyText)
    {
        try
        {
            using var stream = OpenReplyReadStream(replyPath);
            using var reader = new StreamReader(stream, ProtocolEncoding);
            replyText = reader.ReadToEnd();
            return true;
        }
        catch (IOException)
        {
            // The game removes and renames the output file while rotating replies.
            replyText = string.Empty;
            return false;
        }
    }

    private static void PublishCommand(string temporaryPath, string commandPath, string line)
    {
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 256,
                FileOptions.WriteThrough))
            {
                var bytes = ProtocolEncoding.GetBytes(line);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, commandPath, overwrite: false);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static void CancelCommandIfStillOwned(string commandPath, string id)
    {
        if (!File.Exists(commandPath))
        {
            return;
        }

        try
        {
            var command = File.ReadAllText(commandPath, ProtocolEncoding).TrimEnd('\r', '\n');
            var fields = command.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 &&
                string.Equals(fields[0], ProtocolVersion, StringComparison.Ordinal) &&
                string.Equals(fields[1], id, StringComparison.Ordinal))
            {
                File.Delete(commandPath);
            }
        }
        catch (IOException)
        {
            // A command that the game has opened cannot be canceled safely.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the pending file when its state cannot be verified.
        }
    }

    private static Encoding CreateProtocolEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ResolvedAppDataRoot(string Path);
}

public enum CompanionReplyStatus
{
    Ok,
    Error,
    Unsupported,
}

public sealed record CompanionProtocolReply(string Id, CompanionReplyStatus Status, string Text)
{
    public DateTimeOffset? ReplyFileLastWriteTimeUtc { get; init; }

    public string WireStatus => Status switch
    {
        CompanionReplyStatus.Ok => "ok",
        CompanionReplyStatus.Error => "error",
        CompanionReplyStatus.Unsupported => "unsupported",
        _ => throw new ArgumentOutOfRangeException(nameof(Status)),
    };

    public static CompanionProtocolReply Parse(string reply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reply);
        var line = reply.TrimEnd('\r', '\n');
        if (line.Contains('\r', StringComparison.Ordinal) || line.Contains('\n', StringComparison.Ordinal))
        {
            throw new CompanionProtocolException("A companion reply must contain exactly one line.");
        }

        var fields = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3 || !string.Equals(fields[0], "v1", StringComparison.Ordinal) ||
            !CompanionProtocolId.IsValid(fields[1]))
        {
            throw new CompanionProtocolException("The companion reply does not match protocol v1.");
        }

        var status = fields[2] switch
        {
            "ok" => CompanionReplyStatus.Ok,
            "error" => CompanionReplyStatus.Error,
            "unsupported" => CompanionReplyStatus.Unsupported,
            _ => throw new CompanionProtocolException($"Unknown companion reply status: {fields[2]}"),
        };
        return new CompanionProtocolReply(fields[1], status, fields.Length == 4 ? fields[3] : string.Empty);
    }

    internal static bool TryGetId(string reply, out string id)
    {
        var fields = reply.TrimStart().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length >= 2)
        {
            id = fields[1];
            return true;
        }

        id = string.Empty;
        return false;
    }
}

public class CompanionProtocolException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public sealed class CompanionCommandPendingException(string message)
    : CompanionProtocolException(message);

public sealed class CompanionProtocolTimeoutException(string id, TimeSpan timeout)
    : TimeoutException($"No reply for companion command {id} arrived within {timeout}.")
{
    public string Id { get; } = id;

    public TimeSpan Timeout { get; } = timeout;
}

internal static partial class CompanionProtocolId
{
    internal static bool IsValid(string value) => TokenRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
