using System.Text;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Tests.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class GameFixEngineTests
{
    [Fact]
    public void Install_is_idempotent_and_uninstall_restores_the_exact_original_bytes()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\n");
        var definition = fixture.Fix("cs.test.safe", "local state = 1", "local state = 2");
        var engine = TestEngine();

        var installed = engine.Install(definition, fixture.GameDirectory);
        var repeated = engine.Install(definition, fixture.GameDirectory);

        Assert.True(installed.Changed);
        Assert.False(repeated.Changed);
        Assert.Equal("local state = 2\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/task.script")));
        Assert.Equal(GameFixState.Installed, engine.GetStatus(definition, fixture.GameDirectory));
        var doctor = GameDoctor.Analyze(GameTarget.ClearSky, fixture.GameDirectory);
        Assert.Collection(doctor.InstalledFixes, fix => Assert.Equal(GameFixState.Installed, fix.State));

        var removed = engine.Uninstall(definition, fixture.GameDirectory);

        Assert.True(removed.Changed);
        Assert.Equal("local state = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/task.script")));
        Assert.Equal(GameFixState.Removed, engine.GetStatus(definition, fixture.GameDirectory));
    }

    [Fact]
    public void Update_reapplies_a_new_version_from_the_original_archive_and_preserves_the_archive()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        const string relativePath = "gamedata/configs/creatures/spawn_sections_garbage.ltx";
        const string source = "[gar_digger_quester]:stalker\r\ncharacter_profile     = gar_digger_quester\r\n";
        const string anchor = "character_profile     = gar_digger_quester\r\n";
        var archiveBytes = XRayArchiveFixtureBuilder.BuildUncompressed(relativePath, Encoding.Latin1.GetBytes(source));
        fixture.WriteArchive("resources", "resources.db0", archiveBytes);
        var first = fixture.Fix("cs.test.archive-update", anchor, anchor + "on_death = old\r\n") with
        {
            TextPatches = [new TextPatchOperation(relativePath, anchor, anchor + "on_death = old\r\n")],
        };
        var second = first with
        {
            Version = "2.0.0",
            TextPatches = [new TextPatchOperation(relativePath, anchor, anchor + "on_death = new\r\n")],
        };
        var engine = TestEngine();
        var path = fixture.GetFile(relativePath);

        engine.Install(first, fixture.GameDirectory);
        var updated = engine.Update(second, fixture.GameDirectory);

        Assert.True(updated.Changed);
        Assert.Contains("on_death = new\r\n", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal("2.0.0", Assert.Single(engine.ListInstalled(fixture.GameDirectory)).Version);
        Assert.Equal(archiveBytes, File.ReadAllBytes(Path.Combine(fixture.GameDirectory, "resources", "resources.db0")));
        engine.Uninstall(second, fixture.GameDirectory);
        Assert.False(File.Exists(path));
        Assert.Equal(archiveBytes, File.ReadAllBytes(Path.Combine(fixture.GameDirectory, "resources", "resources.db0")));
    }

    [Fact]
    public void Failed_update_restores_the_previous_patch_and_manifest()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\n");
        var first = fixture.Fix("cs.test.update-rollback", "local state = 1", "local state = 2");
        var second = first with
        {
            Version = "2.0.0",
            TextPatches = [new TextPatchOperation("gamedata/scripts/task.script", "missing = 1", "missing = 2")],
        };
        var engine = TestEngine();
        var path = fixture.GetFile("gamedata/scripts/task.script");
        engine.Install(first, fixture.GameDirectory);

        Assert.Throws<IOException>(() => engine.Update(second, fixture.GameDirectory));

        Assert.Equal("local state = 2\n", File.ReadAllText(path));
        var installed = Assert.Single(engine.ListInstalled(fixture.GameDirectory));
        Assert.Equal("1.0.0", installed.Version);
        Assert.Equal(GameFixState.Installed, installed.State);
    }

    [Fact]
    public void Uninstall_refuses_to_restore_over_a_file_modified_after_install()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\n");
        var definition = fixture.Fix("cs.test.drift", "local state = 1", "local state = 2");
        var engine = TestEngine();
        engine.Install(definition, fixture.GameDirectory);
        var path = fixture.GetFile("gamedata/scripts/task.script");
        File.WriteAllText(path, "external change\n");

        Assert.Throws<InvalidOperationException>(() => engine.Uninstall(definition, fixture.GameDirectory));
        Assert.Equal("external change\n", File.ReadAllText(path));
        Assert.Equal(GameFixState.Modified, engine.GetStatus(definition, fixture.GameDirectory));
    }

    [Fact]
    public void Unsupported_build_is_rejected_before_any_file_or_manifest_change()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\n");
        var definition = fixture.Fix("cs.test.build", "local state = 1", "local state = 2", supportedBuilds: ["19000001"]);

        Assert.Throws<NotSupportedException>(() => TestEngine().Install(definition, fixture.GameDirectory));
        Assert.Equal("local state = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/task.script")));
        Assert.Equal(GameFixState.NotInstalled, new GameFixEngine().GetStatus(definition, fixture.GameDirectory));
    }

    [Fact]
    public void Production_engine_rejects_synthetic_only_fix_definitions()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\n");
        var definition = fixture.Fix("cs.test.synthetic", "local state = 1", "local state = 2");

        Assert.Throws<NotSupportedException>(() => new GameFixEngine().Install(definition, fixture.GameDirectory));
        Assert.Equal("local state = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/task.script")));
        Assert.Equal(GameFixState.NotInstalled, new GameFixEngine().GetStatus(definition, fixture.GameDirectory));
    }

    [Fact]
    public void Missing_or_ambiguous_text_anchor_is_rejected_without_partial_writes()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\nlocal state = 1\n");
        var definition = fixture.Fix("cs.test.anchor", "local state = 1", "local state = 2");

        Assert.Throws<InvalidDataException>(() => TestEngine().Install(definition, fixture.GameDirectory));
        Assert.Equal("local state = 1\nlocal state = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/task.script")));
        Assert.Equal(GameFixState.NotInstalled, new GameFixEngine().GetStatus(definition, fixture.GameDirectory));
    }

    [Fact]
    public void Dependencies_and_conflicts_are_enforced_from_active_manifests()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/base.script", "state = 1\n");
        fixture.Write("gamedata/scripts/dependent.script", "state = 2\n");
        var first = fixture.Fix("cs.test.base", "state = 1", "state = 11") with
        {
            TextPatches = [new TextPatchOperation("gamedata/scripts/base.script", "state = 1", "state = 11")],
        };
        var dependent = fixture.Fix("cs.test.dependent", "state = 2", "state = 22", dependencies: [first.Id]) with
        {
            TextPatches = [new TextPatchOperation("gamedata/scripts/dependent.script", "state = 2", "state = 22")],
        };
        var conflicting = fixture.Fix("cs.test.conflict", "state = 2", "state = 33", conflicts: [first.Id]);
        var engine = TestEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Install(dependent, fixture.GameDirectory));
        engine.Install(first, fixture.GameDirectory);
        engine.Install(dependent, fixture.GameDirectory);
        Assert.Throws<InvalidOperationException>(() => engine.Install(conflicting, fixture.GameDirectory));
        Assert.Throws<InvalidOperationException>(() => engine.Uninstall(first, fixture.GameDirectory));
    }

    [Fact]
    public void Failed_second_file_write_rolls_back_first_file_and_removes_new_fix_state()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/first.script", "first = 1\n");
        fixture.Write("gamedata/scripts/second.script", "second = 1\n");
        var definition = fixture.Fix("cs.test.rollback", "first = 1", "first = 2") with
        {
            TextPatches =
            [
                new TextPatchOperation("gamedata/scripts/first.script", "first = 1", "first = 2"),
                new TextPatchOperation("gamedata/scripts/second.script", "second = 1", "second = 2"),
            ],
        };
        var fileSystem = new FailOnTargetMoveFileSystem(fixture.GetFile("gamedata/scripts/second.script"));

        Assert.Throws<IOException>(() => new GameFixEngine(fileSystem, allowSyntheticDefinitions: true).Install(definition, fixture.GameDirectory));

        Assert.Equal("first = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/first.script")));
        Assert.Equal("second = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/second.script")));
        Assert.False(Directory.Exists(Path.Combine(fixture.GameDirectory, ".save-editor-game-fixes")));
        Assert.Empty(Directory.GetFiles(fixture.GameDirectory, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Install_refuses_a_target_changed_after_preflight()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        var path = fixture.GetFile("gamedata/scripts/task.script");
        fixture.Write("gamedata/scripts/task.script", "local state = 1\n");
        var definition = fixture.Fix("cs.test.fresh-sha", "local state = 1", "local state = 2");
        var fileSystem = new FailOnTargetMoveFileSystem(changeTargetAfterBackup: path);

        Assert.Throws<IOException>(() => new GameFixEngine(fileSystem, allowSyntheticDefinitions: true).Install(definition, fixture.GameDirectory));

        Assert.Equal("external update\n", File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(fixture.GameDirectory, ".save-editor-game-fixes")));
    }

    [Fact]
    public void Text_patch_preserves_all_nonmatching_single_byte_content_and_rejects_unrepresentable_text()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        var bytes = new byte[] { 0x82, (byte)'=', (byte)' ', (byte)'1', (byte)'\r', (byte)'\n' };
        var path = fixture.GetFile("gamedata/scripts/cp1251.script");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        var engine = TestEngine();
        var definition = fixture.Fix("cs.test.cp1251", "= 1", "= 2") with
        {
            TextPatches = [new TextPatchOperation("gamedata/scripts/cp1251.script", "= 1", "= 2")],
        };

        engine.Install(definition, fixture.GameDirectory);

        Assert.Equal(new byte[] { 0x82, (byte)'=', (byte)' ', (byte)'2', (byte)'\r', (byte)'\n' }, File.ReadAllBytes(path));
        var nonLatin = definition with
        {
            Id = "cs.test.non-latin",
            TextPatches = [new TextPatchOperation("gamedata/scripts/cp1251.script", "= 2", "= Ж")],
        };
        Assert.Throws<ArgumentException>(() => engine.Install(nonLatin, fixture.GameDirectory));
    }

    [Fact]
    public void Patch_from_an_xray_archive_is_uninstalled_by_removing_only_the_new_overlay()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        const string relativePath = "gamedata/configs/creatures/spawn_sections_garbage.ltx";
        const string source = "[gar_digger_quester]:stalker\r\n" +
            "$spawn                     = \"respawn\\gar_digger_quester\"\r\n" +
            "character_profile     = gar_digger_quester\r\n";
        const string anchor = "character_profile     = gar_digger_quester\r\n";
        const string replacement = anchor + "on_death             = %+gar_flea_market_stop_quest_line%\r\n";
        var sourceBytes = Encoding.Latin1.GetBytes(source);
        var archiveBytes = XRayArchiveFixtureBuilder.BuildUncompressed(relativePath, sourceBytes);
        fixture.WriteArchive("resources", "resources.db0", archiveBytes);
        var definition = fixture.Fix("cs.test.archive-overlay", anchor, replacement) with
        {
            TextPatches = [new TextPatchOperation(relativePath, anchor, replacement)],
        };
        var engine = TestEngine();
        var path = fixture.GetFile(relativePath);

        var installed = engine.Install(definition, fixture.GameDirectory);

        Assert.True(installed.Changed);
        Assert.True(File.Exists(path));
        var patchedText = File.ReadAllText(path);
        var parsed = LtxDocument.Parse(patchedText, relativePath);
        Assert.Equal("%+gar_flea_market_stop_quest_line%", parsed["gar_digger_quester"].Values["on_death"]);
        Assert.Equal(GameFixState.Installed, engine.GetStatus(definition, fixture.GameDirectory));

        var removed = engine.Uninstall(definition, fixture.GameDirectory);

        Assert.True(removed.Changed);
        Assert.False(File.Exists(path));
        Assert.Equal(GameFixState.Removed, engine.GetStatus(definition, fixture.GameDirectory));
        Assert.Equal(archiveBytes, File.ReadAllBytes(Path.Combine(fixture.GameDirectory, "resources", "resources.db0")));
    }

    private sealed class SteamGameFixture : IDisposable
    {
        private readonly string _root;

        public SteamGameFixture(GameTarget target, string buildId)
        {
            Target = target;
            _root = Path.Combine(Path.GetTempPath(), "sse-fix-engine-" + Guid.NewGuid().ToString("N"));
            var library = Path.Combine(_root, "steam-library");
            var descriptor = GameTargetCatalog.Get(target);
            GameDirectory = Path.Combine(library, "steamapps", "common", descriptor.Title);
            Directory.CreateDirectory(GameDirectory);
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
            File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{descriptor.SteamAppId}.acf"), $$"""
                "AppState"
                {
                    "appid" "{{descriptor.SteamAppId}}"
                    "buildid" "{{buildId}}"
                    "installdir" "{{descriptor.Title}}"
                }
                """);
        }

        public string GameDirectory { get; }
        public GameTarget Target { get; }
        public string GetFile(string relative) => Path.Combine(GameDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relative, string text)
        {
            var path = GetFile(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void WriteArchive(string alias, string archiveName, byte[] archiveBytes)
        {
            var path = Path.Combine(GameDirectory, alias, archiveName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, archiveBytes);
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"), $$"""
                $arch_dir_resources$ = false| false| $fs_root$| resources\
                $arch_dir_patches$ = false| true| $fs_root$| patches\
                $game_data$ = false| true| $fs_root$| gamedata\
                """);
        }

        public GameFixDefinition Fix(
            string id,
            string before,
            string after,
            IReadOnlyList<string>? supportedBuilds = null,
            IReadOnlyList<string>? dependencies = null,
            IReadOnlyList<string>? conflicts = null) => new GameFixDefinition(
                id,
                Target,
                "1.0.0",
                id,
                supportedBuilds ?? ["19000000"],
                GameFixCategory.Recommended,
                GameFixMaturity.Validated,
                dependencies ?? [],
                conflicts ?? [],
                [new TextPatchOperation("gamedata/scripts/task.script", before, after)],
                "unit-test fixture")
            {
                Problem = "Synthetic regression case",
                Description = "Exercises the transaction and rollback boundary on an isolated fixture.",
                VerificationState = GameFixVerificationState.SyntheticTests,
                DetectionMethod = "Unique exact text anchor in a synthetic file",
            };

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private static GameFixEngine TestEngine() => new(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);

    private sealed class FailOnTargetMoveFileSystem(string? failTarget = null, string? changeTargetAfterBackup = null) : IGameFileSystem
    {
        private readonly PhysicalGameFileSystem _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) => _inner.EnumerateFiles(path, pattern, option);
        public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option) => _inner.EnumerateDirectories(path, pattern, option);
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public string ReadAllText(string path) => _inner.ReadAllText(path);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void WriteAllBytes(string path, byte[] bytes) => _inner.WriteAllBytes(path, bytes);
        public void Move(string source, string destination, bool overwrite)
        {
            if (failTarget is not null && string.Equals(destination, failTarget, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("injected move failure");
            _inner.Move(source, destination, overwrite);
            if (changeTargetAfterBackup is not null && destination.EndsWith(".before", StringComparison.Ordinal))
                File.WriteAllText(changeTargetAfterBackup, "external update\n");
        }
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void DeleteDirectory(string path, bool recursive) => _inner.DeleteDirectory(path, recursive);
    }
}
