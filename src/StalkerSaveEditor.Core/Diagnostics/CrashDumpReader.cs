using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Diagnostics;

/// <summary>
/// Reads what a game minidump (<c>xray_*.mdmp</c>) says about a crash. The game overwrites its log on the next start,
/// the dumps stay: the engine's own error text (Expression, Function, Description, Arguments) is still in the dumped
/// memory, and the exception record names the module that failed. The result is log-shaped text for
/// <see cref="CrashLogAnalyzer"/>; nothing is disassembled or symbolised.
/// </summary>
public static partial class CrashDumpReader
{
    private const uint Signature = 0x504D444D; // "MDMP"
    private const int ModuleListStream = 4;
    private const int ExceptionStream = 6;
    private const int ModuleRecordSize = 108;
    private const int MaximumModules = 4096;
    private const uint Breakpoint = 0x80000003;
    private const uint AccessViolation = 0xC0000005;

    public static bool IsMinidump(ReadOnlySpan<byte> data) =>
        data.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(data) == Signature;

    /// <summary>Log-shaped text of the dump; throws <see cref="InvalidDataException"/> when the bytes are not a minidump.</summary>
    public static string ToLogText(ReadOnlySpan<byte> data)
    {
        if (!IsMinidump(data)) throw new InvalidDataException("Not a minidump file.");
        var text = new StringBuilder();
        var fields = ErrorFields(data);
        if (fields.Count > 0)
        {
            text.Append("FATAL ERROR\n");
            foreach (var field in fields) text.Append("[error]").Append(field).Append('\n');
        }

        if (ReadException(data) is { } exception)
        {
            var where = exception.Module is null
                ? "0x" + exception.Address.ToString("x", CultureInfo.InvariantCulture)
                : exception.Module + "+0x" + exception.Offset.ToString("x", CultureInfo.InvariantCulture);
            text.Append(exception.Code switch
            {
                AccessViolation => $"Unhandled Exception: EXCEPTION_ACCESS_VIOLATION in {where}\n",
                // The engine reports its own fatal errors by stopping on a breakpoint; the message is in the fields above.
                Breakpoint when fields.Count > 0 => $"Stopped by the engine's error handler in {where}\n",
                Breakpoint => $"Unhandled Exception: the engine stopped itself in {where}; its message is not in the dump\n",
                _ => $"Unhandled Exception: code 0x{exception.Code.ToString("x8", CultureInfo.InvariantCulture)} in {where}\n",
            });
        }

        return text.ToString();
    }

    private static (uint Code, ulong Address, string? Module, ulong Offset)? ReadException(ReadOnlySpan<byte> data)
    {
        if (FindStream(data, ExceptionStream) is not { } stream || stream.Length < 40) return null;
        var record = data.Slice(stream.Offset, stream.Length);
        var code = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
        var address = BinaryPrimitives.ReadUInt64LittleEndian(record[24..]);
        foreach (var module in Modules(data))
        {
            if (address >= module.Base && address - module.Base < module.Size)
                return (code, address, module.Name, address - module.Base);
        }

        return (code, address, null, 0);
    }

    private static List<(ulong Base, uint Size, string Name)> Modules(ReadOnlySpan<byte> data)
    {
        var modules = new List<(ulong, uint, string)>();
        if (FindStream(data, ModuleListStream) is not { } stream || stream.Length < 4) return modules;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[stream.Offset..]);
        if (count > MaximumModules || 4 + (long)count * ModuleRecordSize > stream.Length) return modules;
        for (var index = 0; index < count; index++)
        {
            var record = data.Slice(stream.Offset + 4 + index * ModuleRecordSize, ModuleRecordSize);
            var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            if (nameOffset > data.Length - 4) continue;
            var nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)nameOffset..]);
            if (nameBytes > 2048 || nameOffset + 4 + nameBytes > data.Length) continue;
            var path = Encoding.Unicode.GetString(data.Slice((int)nameOffset + 4, (int)nameBytes));
            var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
            modules.Add((BinaryPrimitives.ReadUInt64LittleEndian(record), BinaryPrimitives.ReadUInt32LittleEndian(record[8..]), name));
        }

        return modules;
    }

    private static (int Offset, int Length)? FindStream(ReadOnlySpan<byte> data, int type)
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var directory = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if (count > 1024 || directory > data.Length || (long)directory + count * 12 > data.Length) return null;
        for (var index = 0; index < count; index++)
        {
            var entry = data.Slice((int)directory + index * 12, 12);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != type) continue;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            return offset <= data.Length && length <= data.Length - offset ? ((int)offset, (int)length) : null;
        }

        return null;
    }

    /// <summary>The engine's error report lines found as printable text in the dumped memory, each field once.</summary>
    private static List<string> ErrorFields(ReadOnlySpan<byte> data)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;
        for (var index = 0; index <= data.Length; index++)
        {
            var printable = index < data.Length && data[index] is >= 0x20 and < 0x7F;
            if (printable)
            {
                if (start < 0) start = index;
                continue;
            }

            if (start >= 0 && index - start is >= 12 and <= 1024)
            {
                var match = ErrorFieldRegex().Match(Encoding.ASCII.GetString(data[start..index]));
                if (match.Success)
                {
                    var name = match.Groups["name"].Value;
                    found.TryAdd(name, $"{name,-14}: {match.Groups["value"].Value.Trim()}");
                }
            }

            start = -1;
        }

        string[] order = ["Expression", "Function", "File", "Line", "Description", "Arguments", "Argument 0", "Argument 1"];
        return order.Where(found.ContainsKey).Select(name => found[name]).ToList();
    }

    [GeneratedRegex(@"(?<name>Expression|Function|File|Line|Description|Arguments|Argument [01])\s*:\s(?<value>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorFieldRegex();
}
