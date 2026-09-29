using System.Security.Cryptography;
using System.Text;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class ToolkitSnapshotServiceTests
{
    [Fact]
    public void Snapshot_is_content_addressed_and_restore_replays_only_the_managed_fix()
    {
        using var fixture = new SnapshotGameFixture();
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        fixture.Write("gamedata/scripts/user.script", "user = before\n");
        var fix = SnapshotGameFixture.Fix("cop.test.snapshot", "task = before\n", "task = after\n");
        var engine = new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);
        engine.Install(fix, fixture.GameDirectory);
        var service = new ToolkitSnapshotService(fixture.SnapshotRoot, engine, new Dictionary<string, GameFixDefinition> { [fix.Id] = fix }, configStateDirectory: fixture.ConfigStateRoot);

        var snapshot = service.Create(GameTarget.CallOfPripyat, fixture.GameDirectory);

        Assert.Equal([fix.Id], snapshot.FixIds);
        Assert.DoesNotContain(snapshot.Files, file => file.RelativePath.Contains("user.script", StringComparison.Ordinal));
        var managedFile = Assert.Single(snapshot.Files, file => file.Provider == "GameFix" && file.RelativePath == "gamedata/scripts/task.script");
        var objectPath = Path.Combine(fixture.SnapshotRoot, "objects", managedFile.Sha256);
        Assert.True(File.Exists(objectPath));
        Assert.Equal(managedFile.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(objectPath))).ToLowerInvariant());
        Assert.Equal("task = after\n", File.ReadAllText(objectPath));

        engine.Uninstall(fix.Id, fixture.GameDirectory);
        fixture.Write("gamedata/scripts/user.script", "user = edited outside toolkit\n");
        var restore = service.Restore(snapshot.Id);

        Assert.True(restore.Restored);
        Assert.Contains("automatic safety snapshot", restore.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("task = after\n", File.ReadAllText(fixture.FilePath("gamedata/scripts/task.script")));
        Assert.Equal("user = edited outside toolkit\n", File.ReadAllText(fixture.FilePath("gamedata/scripts/user.script")));
    }

    [Fact]
    public void Snapshot_creation_refuses_modified_managed_files()
    {
        using var fixture = new SnapshotGameFixture();
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        var fix = SnapshotGameFixture.Fix("cop.test.snapshot-drift", "task = before\n", "task = after\n");
        var engine = new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);
        engine.Install(fix, fixture.GameDirectory);
        fixture.Write("gamedata/scripts/task.script", "task = user edit\n");
        var service = new ToolkitSnapshotService(fixture.SnapshotRoot, engine, new Dictionary<string, GameFixDefinition> { [fix.Id] = fix }, configStateDirectory: fixture.ConfigStateRoot);

        Assert.Throws<InvalidOperationException>(() => service.Create(GameTarget.CallOfPripyat, fixture.GameDirectory));
        Assert.Empty(Directory.Exists(Path.Combine(fixture.SnapshotRoot, "snapshots"))
            ? Directory.EnumerateFiles(Path.Combine(fixture.SnapshotRoot, "snapshots"), "*", SearchOption.AllDirectories)
            : []);
    }

    [Fact]
    public void Compatible_preset_attempt_creates_a_recovery_snapshot_before_the_batch()
    {
        using var fixture = new SnapshotGameFixture();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.GameDirectory)!, "..", "appmanifest_41700.acf"),
            "\"AppState\" { \"appid\" \"41700\" \"installdir\" \"Call of Pripyat\" \"buildid\" \"11450453\" }");
        var engine = new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);
        var service = new ToolkitSnapshotService(fixture.SnapshotRoot, engine,
            GameFixCatalog.All.ToDictionary(fix => fix.Id, StringComparer.Ordinal), configStateDirectory: fixture.ConfigStateRoot);

        var application = service.ApplyFixPreset(GameTarget.CallOfPripyat, fixture.GameDirectory, GameFixPreset.Recommended);

        Assert.False(application.Applied);
        Assert.True(application.PriorStateRestored, application.Error);
        Assert.NotNull(application.SafetySnapshotId);
        Assert.Empty(service.Inspect(application.SafetySnapshotId!).FixIds);
        Assert.False(Directory.Exists(Path.Combine(fixture.GameDirectory, ".save-editor-game-fixes")));
    }

    [Fact]
    public void Profile_replays_managed_fixes_and_config_after_creating_a_safety_snapshot()
    {
        using var fixture = new SnapshotGameFixture();
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        fixture.Write("user.ltx", "\n");
        var fix = SnapshotGameFixture.Fix("cop.test.profile", "task = before\n", "task = after\n");
        var definitions = new Dictionary<string, GameFixDefinition> { [fix.Id] = fix };
        var engine = new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);
        engine.Install(fix, fixture.GameDirectory);
        ManagedUserLtxSettings.SetOverrides(fixture.FilePath("user.ltx"), fixture.ConfigStateRoot,
            new Dictionary<string, string> { ["g_fov"] = "90" });
        var snapshots = new ToolkitSnapshotService(fixture.SnapshotRoot, engine, definitions, configStateDirectory: fixture.ConfigStateRoot);
        var profiles = new ToolkitProfileService(fixture.ProfileRoot, engine, definitions, companion: null, snapshots, fixture.ConfigStateRoot);

        var profile = profiles.SaveCurrent("Quiet Zone", GameTarget.CallOfPripyat, fixture.GameDirectory, fixture.FilePath("user.ltx"));
        engine.Uninstall(fix.Id, fixture.GameDirectory);
        ManagedUserLtxSettings.SetOverrides(fixture.FilePath("user.ltx"), fixture.ConfigStateRoot,
            new Dictionary<string, string> { ["g_fov"] = "110" });

        var result = profiles.Apply(profile.Id, GameTarget.CallOfPripyat, fixture.GameDirectory, fixture.FilePath("user.ltx"));

        Assert.True(result.Restored, result.Message);
        Assert.Equal(GameFixState.Installed, engine.GetStatus(fix, fixture.GameDirectory));
        Assert.Contains("g_fov 90", File.ReadAllText(fixture.FilePath("user.ltx")), StringComparison.Ordinal);
        Assert.Single(snapshots.List());
        Assert.Equal("Quiet Zone", Assert.Single(profiles.List()).Name);
        profiles.Delete(profile.Id);
        Assert.Empty(profiles.List());
    }

    [Fact]
    public void Snapshot_restores_user_ltx_owner_record_and_exact_original_line()
    {
        using var fixture = new SnapshotGameFixture();
        fixture.Write("user.ltx", "G_FOV = 95\r\ncustom_setting stays\r\n");
        var engine = new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);
        var service = new ToolkitSnapshotService(fixture.SnapshotRoot, engine, new Dictionary<string, GameFixDefinition>(), configStateDirectory: fixture.ConfigStateRoot);
        ManagedUserLtxSettings.SetOverrides(fixture.FilePath("user.ltx"), fixture.ConfigStateRoot,
            new Dictionary<string, string> { ["g_fov"] = "110" });
        var snapshot = service.Create(GameTarget.CallOfPripyat, fixture.GameDirectory, fixture.FilePath("user.ltx"));
        ManagedUserLtxSettings.SetOverrides(fixture.FilePath("user.ltx"), fixture.ConfigStateRoot,
            new Dictionary<string, string> { ["g_fov"] = "120" });

        var result = service.Restore(snapshot.Id);

        Assert.True(result.Restored, result.Message);
        Assert.Contains("G_FOV = 110\r\n", File.ReadAllText(fixture.FilePath("user.ltx")), StringComparison.Ordinal);
        Assert.Contains("custom_setting stays\r\n", File.ReadAllText(fixture.FilePath("user.ltx")), StringComparison.Ordinal);
        ManagedUserLtxSettings.RestoreDefault(fixture.FilePath("user.ltx"), fixture.ConfigStateRoot, "g_fov");
        Assert.Equal("G_FOV = 95\r\ncustom_setting stays\r\n", File.ReadAllText(fixture.FilePath("user.ltx")));
    }

    [Fact]
    public void Deleting_snapshots_prunes_only_unreferenced_content_objects()
    {
        using var fixture = new SnapshotGameFixture();
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        var fix = SnapshotGameFixture.Fix("cop.test.snapshot-delete", "task = before\n", "task = after\n");
        var engine = new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true);
        engine.Install(fix, fixture.GameDirectory);
        var service = new ToolkitSnapshotService(fixture.SnapshotRoot, engine,
            new Dictionary<string, GameFixDefinition> { [fix.Id] = fix }, configStateDirectory: fixture.ConfigStateRoot);
        var first = service.Create(GameTarget.CallOfPripyat, fixture.GameDirectory);
        var second = service.Create(GameTarget.CallOfPripyat, fixture.GameDirectory);
        var objectPath = Path.Combine(fixture.SnapshotRoot, "objects", Assert.Single(first.Files, file => file.Provider == "GameFix").Sha256);

        service.Delete(first.Id);
        Assert.True(File.Exists(objectPath));
        Assert.Single(service.List());

        service.Delete(second.Id);
        Assert.False(File.Exists(objectPath));
        Assert.Empty(service.List());
    }

    private sealed class SnapshotGameFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "save-editor-snapshot-" + Guid.NewGuid().ToString("N"));

        public SnapshotGameFixture()
        {
            Directory.CreateDirectory(Path.Combine(_root, "steamapps", "common", "Call of Pripyat"));
            Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
            GameDirectory = Path.Combine(_root, "steamapps", "common", "Call of Pripyat");
            SnapshotRoot = Path.Combine(_root, "snapshots-store");
            ProfileRoot = Path.Combine(_root, "profiles-store");
            ConfigStateRoot = Path.Combine(_root, "config-state");
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"),
                "$game_data$ = false| true| $fs_root$| gamedata\\\n" +
                "$game_config$ = true| false| $game_data$| configs\\\n" +
                "$arch_dir_resources$ = false| false| $fs_root$| resources\\\n");
            File.WriteAllText(Path.Combine(_root, "steamapps", "appmanifest_41700.acf"),
                "\"AppState\" { \"appid\" \"41700\" \"installdir\" \"Call of Pripyat\" \"buildid\" \"19000000\" }");
        }

        public string GameDirectory { get; }
        public string SnapshotRoot { get; }
        public string ProfileRoot { get; }
        public string ConfigStateRoot { get; }
        public string FilePath(string relative) => Path.Combine(GameDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relative, string content)
        {
            var path = FilePath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public static GameFixDefinition Fix(string id, string before, string after) => new(
            id,
            GameTarget.CallOfPripyat,
            "1.0.0",
            id,
            ["19000000"],
            GameFixCategory.Recommended,
            GameFixMaturity.Validated,
            [],
            [],
            [new TextPatchOperation("gamedata/scripts/task.script", before, after)],
            "synthetic test definition")
        {
            Problem = "Snapshot service regression case",
            Description = "Tests a managed, exact text patch.",
            VerificationState = GameFixVerificationState.SyntheticTests,
            DetectionMethod = "Exact unique test anchor",
        };

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
