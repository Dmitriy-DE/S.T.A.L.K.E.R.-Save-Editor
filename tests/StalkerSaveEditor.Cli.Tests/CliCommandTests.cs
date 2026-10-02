using System.Globalization;
using System.Text.Json;
using StalkerSaveEditor.Cli;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Storage;
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
    public void Crash_discover_command_finds_logs_under_a_detected_installation_as_json()
    {
        var steamRoot = Path.Combine(Path.GetTempPath(), "sse-crash-discover-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(steamRoot, "steamapps", "common", "Call of Pripyat");
        var logPath = Path.Combine(game, "_appdata_", "logs", "xray_steam.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(Path.Combine(game, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        File.WriteAllText(logPath, "[error]Expression : assertion failed\n");
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "appmanifest_41700.acf"), """
            "AppState"
            {
                "appid" "41700"
                "buildid" "11450453"
                "installdir" "Call of Pripyat"
            }
            """);
        try
        {
            var result = Run("crash", "discover", "--steam-root", steamRoot, "--json");

            Assert.Equal(0, result.ExitCode);
            using var response = JsonDocument.Parse(result.Output);
            var discovered = Assert.Single(response.RootElement.EnumerateArray());
            Assert.Equal("CallOfPripyat", discovered.GetProperty("game").GetString());
            Assert.Equal("11450453", discovered.GetProperty("buildId").GetString());
            Assert.Equal(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(logPath)), discovered.GetProperty("path").GetString());
        }
        finally
        {
            Directory.Delete(steamRoot, recursive: true);
        }
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
            Assert.Contains("\"fileAudit\": [", result.Output, StringComparison.Ordinal);
            Assert.Contains("\"owner\": \"Unclassified\"", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Doctor_discover_command_returns_only_structurally_validated_steam_targets_as_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "sse-doctor-discover-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "steamapps", "common", "STALKER Clear Sky");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_20510.acf"), """
            "AppState"
            {
                "appid" "20510"
                "buildid" "11450472"
                "installdir" "STALKER Clear Sky"
            }
            """);
        try
        {
            var result = Run("doctor", "discover", "--steam-root", root, "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"target\": \"ClearSky\"", result.Output, StringComparison.Ordinal);
            Assert.Contains("\"source\": \"Steam\"", result.Output, StringComparison.Ordinal);
            Assert.Contains("\"buildId\": \"11450472\"", result.Output, StringComparison.Ordinal);
            using var response = JsonDocument.Parse(result.Output);
            var discovered = Assert.Single(response.RootElement.EnumerateArray());
            Assert.Equal(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(game)), discovered.GetProperty("directory").GetString());
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
    public void Doctor_quest_command_reports_reader_coverage_without_inventing_task_states()
    {
        using var fixture = Fixture.CreateS2();

        var result = Run("doctor", "quest", fixture.SourcePath, "--json");

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output);
        var root = report.RootElement;
        Assert.Equal("stalker2", root.GetProperty("formatId").GetString());
        Assert.False(root.GetProperty("questStatesAvailable").GetBoolean());
        Assert.Empty(root.GetProperty("states").EnumerateArray());
        Assert.Contains("Shadow of Chernobyl and Clear Sky", root.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fixes_list_reports_retail_verified_catalogue_entries()
    {
        var human = Run("fixes", "list");
        var json = Run("fixes", "list", "--game", "cs", "--json");
        var unsupported = Run("fixes", "list", "--game", "unknown", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("cs.quest.dead-wild-napr [cs] Essential", human.Output, StringComparison.Ordinal);
        Assert.Equal(0, json.ExitCode);
        using var catalogue = JsonDocument.Parse(json.Output);
        var entries = catalogue.RootElement.EnumerateArray().ToArray();
        Assert.Equal(63, entries.Length);
        Assert.Contains(entries, entry => entry.GetProperty("id").GetString() == "cs.quest.dead-wild-napr");
        Assert.All(entries, entry =>
        {
            Assert.Equal("Validated", entry.GetProperty("maturity").GetString());
            Assert.Equal("11450472", Assert.Single(entry.GetProperty("supportedSteamBuildIds").EnumerateArray()).GetString());
        });
        Assert.Equal(2, unsupported.ExitCode);
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
            Assert.Contains("63 catalogued;", human.Output, StringComparison.Ordinal);
            Assert.Equal(0, json.ExitCode);
            using var status = JsonDocument.Parse(json.Output);
            var statusRoot = status.RootElement;
            Assert.Equal("11450472", statusRoot.GetProperty("buildId").GetString());
            Assert.Equal(63, statusRoot.GetProperty("availableFixes").GetArrayLength());
            var recommended = statusRoot.GetProperty("recommendedFixIds").EnumerateArray().Select(id => id.GetString()).ToArray();
            Assert.Equal(61, recommended.Length);
            Assert.NotEmpty(recommended);
            Assert.All(recommended, id => Assert.Contains(statusRoot.GetProperty("availableFixes").EnumerateArray(), fix => fix.GetProperty("id").GetString() == id));
            Assert.Null(statusRoot.GetProperty("catalogueNote").GetString());
            Assert.Contains(statusRoot.GetProperty("recommendedFixIds").EnumerateArray(), id => id.GetString() == "cs.quest.dead-wild-napr");
        }
        finally
        {
            Directory.Delete(library, recursive: true);
        }
    }

    [Fact]
    public void Fixes_apply_preset_all_discovers_from_the_supplied_steam_root_and_returns_structured_json()
    {
        var steamRoot = Path.Combine(Path.GetTempPath(), "sse-fix-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(steamRoot);
        try
        {
            var result = Run("fixes", "apply-preset", "recommended", "all", "--steam-root", steamRoot, "--json");

            Assert.Equal(0, result.ExitCode);
            using var response = JsonDocument.Parse(result.Output);
            Assert.Equal("Recommended", response.RootElement.GetProperty("preset").GetString());
            Assert.Equal(GameFixCatalog.DatasetVersion, response.RootElement.GetProperty("catalogueVersion").GetString());
            Assert.Empty(response.RootElement.GetProperty("installations").EnumerateArray());
        }
        finally
        {
            Directory.Delete(steamRoot, recursive: true);
        }
    }

    [Fact]
    public void Fixes_apply_preset_all_skips_an_unsupported_discovered_build_without_writing()
    {
        var steamRoot = Path.Combine(Path.GetTempPath(), "sse-fix-build-gate-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(steamRoot, "steamapps");
        var gameDirectory = Path.Combine(library, "common", "Call of Pripyat");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
        File.WriteAllText(Path.Combine(library, "appmanifest_41700.acf"), "\"AppState\" { \"appid\" \"41700\" \"buildid\" \"19000000\" \"installdir\" \"Call of Pripyat\" }");
        try
        {
            var result = Run("fixes", "apply-preset", "recommended", "all", "--steam-root", steamRoot, "--json");

            Assert.Equal(0, result.ExitCode);
            using var response = JsonDocument.Parse(result.Output);
            var install = Assert.Single(response.RootElement.GetProperty("installations").EnumerateArray());
            Assert.Equal("cop", install.GetProperty("game").GetString());
            Assert.Equal("19000000", install.GetProperty("buildId").GetString());
            Assert.Equal(21, install.GetProperty("selectedFixCount").GetInt32());
            Assert.Empty(install.GetProperty("installedFixIds").EnumerateArray());
            Assert.False(Directory.Exists(Path.Combine(gameDirectory, ".save-editor-game-fixes")));
        }
        finally
        {
            Directory.Delete(steamRoot, recursive: true);
        }
    }

    [Fact]
    public void Fixes_apply_recommended_preset_fails_closed_and_records_a_safety_snapshot()
    {
        var library = Path.Combine(Path.GetTempPath(), "sse-fix-preset-cli-" + Guid.NewGuid().ToString("N"));
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
            var result = Run("fixes", "apply-preset", "recommended", "cs", root, "--json");

            Assert.Equal(3, result.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.Contains("Safety snapshot", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, ".save-editor-game-fixes")));
            var targetIdentity = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(root));
            Assert.Contains(new ToolkitSnapshotService().List(), snapshot =>
                string.Equals(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(snapshot.GameDirectory)), targetIdentity,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
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

            Assert.Equal(4, install.ExitCode);
            Assert.Contains("not present in the evidence-validated catalogue", install.Error, StringComparison.Ordinal);
            Assert.Equal(4, update.ExitCode);
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
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Edit_without_changes_is_rejected()
    {
        using var fixture = Fixture.CreateS2();
        var result = Run("edit", fixture.SourcePath, "--backup-dir", fixture.BackupDirectory);
        Assert.Equal(3, result.ExitCode);
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

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(fixture.SourcePath));
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Missing_file_and_unknown_command_return_errors_without_tracebacks()
    {
        var missing = Run("info", "missing.sav");
        var unknown = Run("not-a-command");
        Assert.Equal(5, missing.ExitCode);
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
