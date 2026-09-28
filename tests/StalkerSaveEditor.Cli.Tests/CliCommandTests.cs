using System.Globalization;
using StalkerSaveEditor.Cli;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Cli.Tests;

public sealed class CliCommandTests
{
    [Fact]
    public void Version_command_prints_application_version()
    {
        var result = Run("version");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Theory]
    [InlineData("info")]
    [InlineData("inventory")]
    [InlineData("inventory", "--all")]
    [InlineData("orphans")]
    public void Read_commands_inspect_synthetic_saves(string command, string? option = null)
    {
        using var fixture = Fixture.CreateS2();
        var result = option is null ? Run(command, fixture.SourcePath) : Run(command, fixture.SourcePath, option);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("stalker2", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Set_money_exports_a_copy_and_keeps_the_source()
    {
        using var fixture = Fixture.CreateS2();
        var source = File.ReadAllBytes(fixture.SourcePath);
        var result = Run("set-money", fixture.SourcePath, "900000", "-o", fixture.OutputPath,
            "--backup-dir", fixture.BackupDirectory);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(source, File.ReadAllBytes(fixture.SourcePath));
        Assert.Equal(900000u, Stalker2SaveReader.FromBytes(File.ReadAllBytes(fixture.OutputPath)).Money);
        Assert.Contains("Backup:", result.Output);
    }

    [Fact]
    public void Set_stack_uses_shared_edit_service_and_exports_a_copy()
    {
        using var fixture = Fixture.CreateXRay();
        var source = File.ReadAllBytes(fixture.SourcePath);
        var item = Assert.Single(XRayTrilogyReader.FromBytes(source).Inventory, value => value.EditableCount);
        var result = Run("set-stack", fixture.SourcePath, "0x" + item.Handle.ToString("X"), "7", "-o", fixture.OutputPath,
            "--backup-dir", fixture.BackupDirectory);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(source, File.ReadAllBytes(fixture.SourcePath));
        Assert.Equal((ushort)7, Assert.Single(XRayTrilogyReader.FromBytes(File.ReadAllBytes(fixture.OutputPath)).Inventory).Count);
    }

    [Fact]
    public void Edit_accepts_batch_money_and_stack_options()
    {
        using var fixture = Fixture.CreateS2();
        var item = Stalker2SaveReader.FromBytes(File.ReadAllBytes(fixture.SourcePath)).Inventory[0];
        var stack = "0x" + item.Handle.ToString("X") + "=5";
        var result = Run("edit", fixture.SourcePath, "--money", "123456", "--stack", stack,
            "-o", fixture.OutputPath, "--backup-dir", fixture.BackupDirectory);
        Assert.True(result.ExitCode == 0, result.Error);
        var written = Stalker2SaveReader.FromBytes(File.ReadAllBytes(fixture.OutputPath));
        Assert.Equal(123456u, written.Money);
        Assert.Equal(5u, Assert.Single(written.Inventory, value => value.Handle == item.Handle).Count);
    }

    [Fact]
    public void Dump_record_reads_a_bounded_s2_record_window()
    {
        using var fixture = Fixture.CreateS2();
        var item = Stalker2SaveReader.FromBytes(File.ReadAllBytes(fixture.SourcePath)).Inventory[0];
        var result = Run("dump-record", fixture.SourcePath, "0x" + item.Handle.ToString("X"), "--limit", "64");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("base=0x", result.Output);
        Assert.Contains("bytes=", result.Output);
    }

    [Fact]
    public void Dump_record_reads_an_exact_xray_object_window()
    {
        using var fixture = Fixture.CreateXRay();
        var save = XRayTrilogyReader.FromBytes(File.ReadAllBytes(fixture.SourcePath));
        var item = save.Inventory[0];
        var result = Run("dump-record", fixture.SourcePath, item.Handle.ToString(CultureInfo.InvariantCulture), "--limit", "64");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("base=0x", result.Output);
        Assert.Contains("bytes=", result.Output);
    }

    [Fact]
    public void Diff_record_compares_two_synthetic_saves()
    {
        using var fixture = Fixture.CreateS2();
        var second = fixture.SourcePath + ".copy";
        File.Copy(fixture.SourcePath, second);
        var item = Stalker2SaveReader.FromBytes(File.ReadAllBytes(fixture.SourcePath)).Inventory[0];
        var result = Run("diff-record", fixture.SourcePath, second, "0x" + item.Handle.ToString("X"));
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("No differences", result.Output);
        File.Delete(second);
    }

    [Fact]
    public void Diff_record_compares_xray_object_records()
    {
        using var fixture = Fixture.CreateXRay();
        var second = fixture.SourcePath + ".copy";
        File.Copy(fixture.SourcePath, second);
        var item = XRayTrilogyReader.FromBytes(File.ReadAllBytes(fixture.SourcePath)).Inventory[0];
        var result = Run("diff-record", fixture.SourcePath, second, item.Handle.ToString(CultureInfo.InvariantCulture));
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("No differences", result.Output);
        File.Delete(second);
    }

    [Theory]
    [InlineData("move", "0x1234", "1", "2")]
    [InlineData("detach", "0x1234")]
    [InlineData("attach-orphan", "0x1234", "1", "2", "1", "1")]
    [InlineData("raw", "0", "u32", "123")]
    public void Commands_without_confirmed_core_writers_fail_closed(string command, params string[] operationArgs)
    {
        using var fixture = Fixture.CreateS2();
        var result = Run(new[] { command, fixture.SourcePath }.Concat(operationArgs).ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Edit_without_changes_is_rejected()
    {
        using var fixture = Fixture.CreateS2();
        var result = Run("edit", fixture.SourcePath, "--backup-dir", fixture.BackupDirectory);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("No changes", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Theory]
    [InlineData("--add", "bandage=1")]
    [InlineData("--durability", "0x1234=0.5")]
    [InlineData("--upgrade", "0x1234=up_test")]
    [InlineData("--placement", "0x1234=ruck")]
    [InlineData("--move", "0x1234=1,1")]
    [InlineData("--detach", "0x1234")]
    [InlineData("--attach", "0x1234=1,1,1,1")]
    [InlineData("--raw", "0:u32:1")]
    public void Edit_options_without_complete_readback_verification_fail_closed(string option, string value)
    {
        using var fixture = Fixture.CreateS2();
        var original = File.ReadAllBytes(fixture.SourcePath);
        var result = Run("edit", fixture.SourcePath, option, value, "-o", fixture.OutputPath,
            "--backup-dir", fixture.BackupDirectory);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(fixture.SourcePath));
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Missing_file_and_unknown_command_return_errors_without_tracebacks()
    {
        var missing = Run("info", "missing.sav");
        var unknown = Run("not-a-command");
        Assert.Equal(2, missing.ExitCode);
        Assert.DoesNotContain("Traceback", missing.Error);
        Assert.Equal(2, unknown.ExitCode);
        Assert.Contains("Usage:", unknown.Error);
    }

    private static CliResult Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var oldOut = Console.Out;
        var oldError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exit = Program.Main(args);
            return new CliResult(exit, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldError);
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private sealed class Fixture : IDisposable
    {
        private Fixture(byte[] bytes)
        {
            Root = Path.Combine(Path.GetTempPath(), "sse-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SourcePath = Path.Combine(Root, "source.sav");
            OutputPath = Path.Combine(Root, "edited.sav");
            BackupDirectory = Root + "-backups";
            File.WriteAllBytes(SourcePath, bytes);
        }

        private string Root { get; }
        public string SourcePath { get; }
        public string OutputPath { get; }
        public string BackupDirectory { get; }

        public static Fixture CreateS2() => new(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "writer-s2-money", "s2-money-source.sav")));

        public static Fixture CreateXRay() => new(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "writer-stacks", "xray-stack-cop-source.sav")));

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            if (Directory.Exists(BackupDirectory)) Directory.Delete(BackupDirectory, recursive: true);
        }
    }
}
