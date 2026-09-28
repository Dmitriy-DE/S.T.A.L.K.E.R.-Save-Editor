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

    [Fact]
    public void Crash_analyse_command_reports_parsed_log_fields_as_json()
    {
        using var fixture = Fixture.CreateCrashLog();

        var result = Run("crash", "analyse", fixture.SourcePath, "--game", "Clear Sky", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"kind\": \"FatalError\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("task_manager.script", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"fileLastWriteTimeUtc\":", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"knownIssueId\": null", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_game_command_audits_an_explicit_installation_as_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-doctor-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "gamedata", "scripts"));
        File.WriteAllText(Path.Combine(root, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        File.WriteAllText(Path.Combine(root, "gamedata", "scripts", "local.script"), "function local_script() end");
        try
        {
            var result = Run("doctor", "game", "cs", root, "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"target\": \"ClearSky\"", result.Output, StringComparison.Ordinal);
            Assert.Contains("loose-files", result.Output, StringComparison.Ordinal);
            Assert.Contains("unknown because no retail baseline is bundled", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Doctor_save_command_reports_structural_health_and_unknown_semantics_as_json()
    {
        using var fixture = Fixture.CreateS2();

        var result = Run("doctor", "save", fixture.SourcePath, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"status\": \"Ok\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("semantic-state", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"repair\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixes_list_reports_only_researched_catalogue_entries()
    {
        var human = Run("fixes", "list");
        var json = Run("fixes", "list", "--json");
        var unsupported = Run("fixes", "list", "--game", "soc", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("cs.quest.dead-wild-napr [cs] Essential", human.Output, StringComparison.Ordinal);
        Assert.Equal(0, json.ExitCode);
        Assert.Contains("cs.quest.dead-wild-napr", json.Output, StringComparison.Ordinal);
        Assert.Contains("11450472", json.Output, StringComparison.Ordinal);
        Assert.Contains("Experimental", json.Output, StringComparison.Ordinal);
        Assert.Equal(0, unsupported.ExitCode);
        Assert.Equal("[]" + Environment.NewLine, unsupported.Output);
    }

    [Fact]
    public void Fixes_status_reports_catalogue_and_safe_preset_counts()
    {
        var library = Path.Combine(Path.GetTempPath(), "sse-fix-status-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(library, "steamapps", "common", "STALKER Clear Sky");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_20510.acf"), """
            "AppState"
            {
                "appid" "20510"
                "buildid" "11450472"
                "installdir" "STALKER Clear Sky"
            }
            """);
        try
        {
            var human = Run("fixes", "status", "cs", root);
            var json = Run("fixes", "status", "cs", root, "--json");

            Assert.Equal(0, human.ExitCode);
            Assert.Contains("1 catalogued; 0 safe recommendations; 0 installed; 1 experimental", human.Output, StringComparison.Ordinal);
            Assert.Equal(0, json.ExitCode);
            Assert.Contains("\"buildId\": \"11450472\"", json.Output, StringComparison.Ordinal);
            Assert.Contains("\"recommendedFixIds\": []", json.Output, StringComparison.Ordinal);
            Assert.Contains("experimental or research-only", json.Output, StringComparison.Ordinal);
            Assert.Contains("cs.quest.dead-wild-napr", json.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(library, recursive: true);
        }
    }

    [Fact]
    public void Fixes_install_fails_closed_for_unknown_catalogue_id_and_remove_is_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-empty-fix-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        try
        {
            var install = Run("fixes", "install", "cs.not-reviewed", root);
            var update = Run("fixes", "update", "cs.not-reviewed", root);
            var remove = Run("fixes", "remove", "cs.not-reviewed", root);

            Assert.Equal(2, install.ExitCode);
            Assert.Contains("not present in the evidence-validated catalogue", install.Error, StringComparison.Ordinal);
            Assert.Equal(2, update.ExitCode);
            Assert.Contains("not present in the evidence-validated catalogue", update.Error, StringComparison.Ordinal);
            Assert.Equal(0, remove.ExitCode);
            Assert.Contains("NotInstalled", remove.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, ".save-editor-game-fixes")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void S2_mod_commands_disable_and_restore_the_custom_mod_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-s2-mod-cli-" + Guid.NewGuid().ToString("N"));
        var paks = Path.Combine(root, "Stalker2", "Content", "Paks");
        var mod = Path.Combine(paks, "~mods", "custom.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
        File.WriteAllText(mod, "mod-bytes");
        try
        {
            var disable = Run("mods", "s2-disable", root);
            Assert.Equal(0, disable.ExitCode);
            Assert.False(Directory.Exists(Path.Combine(paks, "~mods")));
            Assert.True(File.Exists(Path.Combine(root, "Stalker2", "Content", "~mods.disabled", "custom.pak")));

            var restore = Run("mods", "s2-restore", root);
            Assert.Equal(0, restore.ExitCode);
            Assert.Equal("mod-bytes", File.ReadAllText(mod));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    [Fact]
    public void Companion_command_validates_arguments_and_reports_a_missing_game()
    {
        Assert.Equal(2, Run("companion", "install", "stalker3").ExitCode);
        Assert.Equal(2, Run("companion", "install", "all", "--game-dir", "/nowhere").ExitCode);

        var missing = Run("companion", "status", "cop", "--game-dir", Path.Combine(Path.GetTempPath(), "no-game-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("CallOfPripyat: game not found", missing.Output, StringComparison.Ordinal);
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

        public static Fixture CreateCrashLog()
        {
            var fixture = new Fixture([]);
            File.WriteAllText(fixture.SourcePath, """
                [error]Expression : assertion failed
                [error]File : gamedata\\scripts\\task_manager.script
                [error]Line : 428
                """);
            return fixture;
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            if (Directory.Exists(BackupDirectory)) Directory.Delete(BackupDirectory, recursive: true);
        }
    }
}
