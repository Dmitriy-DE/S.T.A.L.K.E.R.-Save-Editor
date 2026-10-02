using System.Buffers.Binary;
using System.Text;
using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class CrashDumpReaderTests
{
    [Fact]
    public void Engine_error_text_and_the_failing_module_are_read_from_a_minidump()
    {
        var dump = BuildDump(0x80000003, 0x10001B944,
            "Expression    : fatal error", "Function      : CInifile::r_section", "Line          : 443",
            "Description   : <no expression>", "Arguments     : Can't open section 'doc_5'");

        var analysis = CrashLogAnalyzer.Analyze(CrashDumpReader.ToLogText(dump), "cs");

        Assert.Equal(CrashLogKind.FatalError, analysis.Kind);
        Assert.Equal("Can't open section 'doc_5'", analysis.Summary);
        Assert.Equal(443, analysis.Line);
        Assert.Equal("CInifile::r_section", analysis.Exception);
        Assert.Contains("xrCore.dll+0x1b944", CrashDumpReader.ToLogText(dump), StringComparison.Ordinal);
    }

    [Fact]
    public void An_access_violation_without_engine_text_names_the_module_and_offset()
    {
        var text = CrashDumpReader.ToLogText(BuildDump(0xC0000005, 0x1000879A4));

        Assert.Contains("EXCEPTION_ACCESS_VIOLATION in xrCore.dll+0x879a4", text, StringComparison.Ordinal);
        Assert.Equal(CrashLogKind.EngineError, CrashLogAnalyzer.Analyze(text).Kind);
    }

    [Fact]
    public void A_file_is_told_apart_by_content_and_a_log_is_still_read_as_a_log()
    {
        var folder = Directory.CreateTempSubdirectory("crash-dump-").FullName;
        try
        {
            var dumpPath = Path.Combine(folder, "xray_user.log"); // the name does not decide
            File.WriteAllBytes(dumpPath, BuildDump(0x80000003, 0x10001B944, "Description   : XML node not found"));
            var logPath = Path.Combine(folder, "plain.mdmp");
            File.WriteAllText(logPath, "FATAL ERROR\n[error]Expression    : something\n");

            Assert.Equal("XML node not found", CrashLogAnalyzer.AnalyzeFile(dumpPath).Summary);
            Assert.Equal("something", CrashLogAnalyzer.AnalyzeFile(logPath).Summary);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'M', (byte)'D', (byte)'M', (byte)'P' })]
    public void Bytes_that_are_not_a_minidump_are_refused(byte[] data)
    {
        Assert.False(CrashDumpReader.IsMinidump(data));
        Assert.Throws<InvalidDataException>(() => CrashDumpReader.ToLogText(data));
    }

    [Fact]
    public void A_truncated_stream_directory_gives_no_exception_line_instead_of_failing()
    {
        var dump = BuildDump(0xC0000005, 0x1000879A4);
        BinaryPrimitives.WriteUInt32LittleEndian(dump.AsSpan(12), 0x7FFFFFF0); // directory beyond the file

        Assert.Equal(string.Empty, CrashDumpReader.ToLogText(dump));
    }

    /// <summary>A minimal minidump: header, two streams (modules, exception), one module xrCore.dll at 0x100000000, text.</summary>
    private static byte[] BuildDump(uint code, ulong address, params string[] memoryText)
    {
        var name = Encoding.Unicode.GetBytes(@"S:\game\bin\xrCore.dll");
        const int header = 32, directory = 24, modules = 4 + 108, exception = 168;
        var nameOffset = header + directory + modules + exception;
        var textOffset = nameOffset + 4 + name.Length + 8;
        var text = Encoding.ASCII.GetBytes(string.Concat(memoryText.Select(line => line + "\0")));
        var dump = new byte[textOffset + text.Length + 16];
        var span = dump.AsSpan();
        Encoding.ASCII.GetBytes("MDMP").CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], header);
        WriteEntry(span[header..], 4, modules, header + directory);
        WriteEntry(span[(header + 12)..], 6, exception, header + directory + modules);
        var module = span[(header + directory)..];
        BinaryPrimitives.WriteUInt32LittleEndian(module, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(module[4..], 0x100000000);
        BinaryPrimitives.WriteUInt32LittleEndian(module[12..], 0x200000);
        BinaryPrimitives.WriteUInt32LittleEndian(module[24..], (uint)nameOffset);
        var record = span[(header + directory + modules)..];
        BinaryPrimitives.WriteUInt32LittleEndian(record[8..], code);
        BinaryPrimitives.WriteUInt64LittleEndian(record[24..], address);
        BinaryPrimitives.WriteUInt32LittleEndian(span[nameOffset..], (uint)name.Length);
        name.CopyTo(span[(nameOffset + 4)..]);
        text.CopyTo(span[textOffset..]);
        return dump;

        static void WriteEntry(Span<byte> entry, uint type, int size, int offset)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(entry, type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)size);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)offset);
        }
    }
}
