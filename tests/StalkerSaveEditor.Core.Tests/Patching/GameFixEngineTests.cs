using System.Security.Cryptography;
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
    public void Recommended_preset_installs_each_selected_catalogue_fix_from_its_exact_text_fragments()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "11450472");
        var selected = GameFixCatalog.ForPreset(GameTarget.ClearSky, GameFixPreset.Recommended)
            .Select(definition => definition with
            {
                TextPatches = definition.TextPatches.Select(operation => operation with { ExpectedFileSha256 = null }).ToArray(),
            })
            .ToArray();
        foreach (var definition in selected)
        {
            foreach (var group in definition.TextPatches.GroupBy(operation => operation.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var codePage = Assert.Single(group.Select(operation => operation.CodePage).Distinct());
                var source = EncodingForPatchTest(codePage).GetBytes(string.Concat(group.Select(operation => operation.ExpectedText)));
                var path = fixture.GetFile(group.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, source);
            }
        }

        var result = TestEngine().ApplyFixes(GameTarget.ClearSky, GameFixPreset.Recommended, selected, fixture.GameDirectory);

        Assert.Equal(selected.Length, result.SelectedFixCount);
        Assert.Equal(selected.Length, result.InstalledFixIds.Count);
        Assert.Empty(result.AlreadyInstalledFixIds);
        Assert.True(result.Changed);
        Assert.Equal(selected.Length, TestEngine().ListInstalled(fixture.GameDirectory).Count);
    }

    [Fact]
    public void Preset_batch_rolls_back_only_its_new_fixes_when_a_later_fix_fails()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/first.script", "first = 1\n");
        fixture.Write("gamedata/scripts/second.script", "second = 1\n");
        var first = fixture.Fix("cs.test.preset-first", "first = 1", "first = 2") with
        {
            TextPatches = [new TextPatchOperation("gamedata/scripts/first.script", "first = 1", "first = 2")],
        };
        var second = fixture.Fix("cs.test.preset-second", "missing = 1", "missing = 2") with
        {
            TextPatches = [new TextPatchOperation("gamedata/scripts/second.script", "missing = 1", "missing = 2")],
        };
        var engine = TestEngine();

        Assert.Throws<IOException>(() => engine.ApplyFixes(GameTarget.ClearSky, GameFixPreset.Recommended, [first, second], fixture.GameDirectory));

        Assert.Equal("first = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/first.script")));
        Assert.Equal("second = 1\n", File.ReadAllText(fixture.GetFile("gamedata/scripts/second.script")));
        Assert.Empty(engine.ListInstalled(fixture.GameDirectory));
    }

    [Fact]
    public void Preset_refuses_to_report_an_installed_fix_current_after_the_game_build_changes()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        var definition = fixture.Fix("cs.test.preset-build", "task = before", "task = after");
        var engine = TestEngine();
        engine.ApplyFixes(GameTarget.ClearSky, GameFixPreset.Recommended, [definition], fixture.GameDirectory);

        fixture.SetBuildId("19000001");

        Assert.Throws<NotSupportedException>(() => engine.ApplyFixes(
            GameTarget.ClearSky,
            GameFixPreset.Recommended,
            [definition],
            fixture.GameDirectory));
    }

    [Fact]
    public void Production_preset_refuses_to_count_a_preexisting_synthetic_test_fix_as_current()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        var definition = fixture.Fix("cs.test.preset-synthetic", "task = before", "task = after");
        TestEngine().Install(definition, fixture.GameDirectory);

        Assert.Throws<NotSupportedException>(() => new GameFixEngine().ApplyFixes(
            GameTarget.ClearSky,
            GameFixPreset.Recommended,
            [definition],
            fixture.GameDirectory));
    }

    [Fact]
    public void Game_doctor_audits_owned_fix_files_and_keeps_unowned_loose_files_unclassified()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        fixture.Write("gamedata/scripts/task.script", "task = before\n");
        fixture.Write("gamedata/scripts/user.script", "user = true\n");
        var definition = fixture.Fix("cs.test.audit", "task = before", "task = after");
        TestEngine().Install(definition, fixture.GameDirectory);

        var cleanReport = GameDoctor.Analyze(GameTarget.ClearSky, fixture.GameDirectory);

        var managed = Assert.Single(cleanReport.FileAudit, file => file.RelativePath == "gamedata/scripts/task.script");
        Assert.Equal("Game Fix: cs.test.audit", managed.Owner);
        Assert.Equal(GameDoctorStatus.Ok, managed.Status);
        var unclassified = Assert.Single(cleanReport.FileAudit, file => file.RelativePath == "gamedata/scripts/user.script");
        Assert.Equal("Unclassified", unclassified.Owner);
        Assert.Equal(GameDoctorStatus.Unknown, unclassified.Status);

        fixture.Write("gamedata/scripts/task.script", "external = true\n");
        var drifted = Assert.Single(GameDoctor.Analyze(GameTarget.ClearSky, fixture.GameDirectory).FileAudit,
            file => file.RelativePath == "gamedata/scripts/task.script");
        Assert.Equal(GameDoctorStatus.Warning, drifted.Status);
    }

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
    public void Production_install_refuses_an_unowned_loose_file_without_a_verified_source_hash()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        const string relativePath = "gamedata/scripts/task.script";
        const string original = "task = vanilla\n";
        fixture.Write(relativePath, original);
        var definition = fixture.Fix("cs.test.unowned-loose", "task = vanilla", "task = fixed") with
        {
            VerificationState = GameFixVerificationState.RetailFilesVerified,
            TextPatches = [new TextPatchOperation(relativePath, "task = vanilla", "task = fixed")],
        };

        Assert.Throws<InvalidDataException>(() => new GameFixEngine().Install(definition, fixture.GameDirectory));

        Assert.Equal(original, File.ReadAllText(fixture.GetFile(relativePath)));
        Assert.Empty(new GameFixEngine().ListInstalled(fixture.GameDirectory));
    }

    [Fact]
    public void Production_install_accepts_a_loose_file_only_when_its_source_hash_is_verified()
    {
        using var fixture = new SteamGameFixture(GameTarget.ClearSky, "19000000");
        const string relativePath = "gamedata/scripts/task.script";
        const string original = "task = vanilla\n";
        fixture.Write(relativePath, original);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(original))).ToLowerInvariant();
        var definition = fixture.Fix("cs.test.verified-loose", "task = vanilla", "task = fixed") with
        {
            VerificationState = GameFixVerificationState.RetailFilesVerified,
            TextPatches = [new TextPatchOperation(relativePath, "task = vanilla", "task = fixed")
            {
                ExpectedFileSha256 = sourceHash,
            }],
        };

        var result = new GameFixEngine().Install(definition, fixture.GameDirectory);

        Assert.Equal(GameFixState.Installed, result.State);
        Assert.Equal("task = fixed\n", File.ReadAllText(fixture.GetFile(relativePath)));
    }

    [Fact]
    public void Every_catalogue_patch_fragment_installs_idempotently_and_uninstall_restores_exact_bytes()
    {
        foreach (var shippedFix in GameFixCatalog.All)
        {
            using var fixture = new SteamGameFixture(shippedFix.Game, shippedFix.SupportedSteamBuildIds[0]);
            var operations = shippedFix.TextPatches
                .Select(operation => operation with { ExpectedFileSha256 = null })
                .ToArray();
            var originalFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in operations.GroupBy(operation => operation.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var codePages = group.Select(operation => operation.CodePage).Distinct().ToArray();
                Assert.Single(codePages);
                var encoding = PatchEncoding(codePages[0]);
                var sourceText = string.Concat(group.Select(operation => operation.ExpectedText));
                var sourceBytes = encoding.GetBytes(sourceText);
                var path = fixture.GetFile(group.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, sourceBytes);
                originalFiles.Add(group.Key, sourceBytes);
            }

            var testFix = shippedFix with { TextPatches = operations };
            var engine = TestEngine();
            var expectedFiles = originalFiles.ToDictionary(
                pair => pair.Key,
                pair => PatchExpectedFragment(testFix.TextPatches.Where(operation => PathComparerForTests(operation.RelativePath, pair.Key)), pair.Value),
                StringComparer.OrdinalIgnoreCase);

            var installed = engine.Install(testFix, fixture.GameDirectory);
            Assert.Equal(GameFixState.Installed, installed.State);
            foreach (var (relativePath, expectedBytes) in expectedFiles)
                Assert.Equal(expectedBytes, File.ReadAllBytes(fixture.GetFile(relativePath)));

            var repeated = engine.Install(testFix, fixture.GameDirectory);
            Assert.False(repeated.Changed);
            Assert.Equal(GameFixState.Installed, repeated.State);

            Assert.Equal(GameFixState.Removed, engine.Uninstall(testFix, fixture.GameDirectory).State);
            foreach (var (relativePath, originalBytes) in originalFiles)
                Assert.Equal(originalBytes, File.ReadAllBytes(fixture.GetFile(relativePath)));
        }
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
    public void Text_patch_round_trips_explicit_cp1251_text_and_restores_original_bytes()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        using var fixture = new SteamGameFixture(GameTarget.CallOfPripyat, "19000000");
        const string relativePath = "gamedata/configs/text/rus/st_dialogs.xml";
        const string original = "<text>Неправильное описание артефакта</text>\r\n";
        const string replacement = "<text>Исправленное описание артефакта</text>\r\n";
        var path = fixture.GetFile(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var before = cp1251.GetBytes(original);
        File.WriteAllBytes(path, before);

        var definition = fixture.Fix("cop.test.cp1251", original, replacement) with
        {
            TextPatches =
            [
                new TextPatchOperation(relativePath, original, replacement)
                {
                    CodePage = 1251,
                },
            ],
        };
        var engine = TestEngine();

        Assert.Equal(GameFixState.Installed, engine.Install(definition, fixture.GameDirectory).State);
        Assert.Equal(cp1251.GetBytes(replacement), File.ReadAllBytes(path));
        Assert.Equal(GameFixState.Installed, engine.Install(definition, fixture.GameDirectory).State);

        Assert.Equal(GameFixState.Removed, engine.Uninstall(definition, fixture.GameDirectory).State);
        Assert.Equal(before, File.ReadAllBytes(path));
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
        private readonly string _manifestPath;

        public SteamGameFixture(GameTarget target, string buildId)
        {
            Target = target;
            _root = Path.Combine(Path.GetTempPath(), "sse-fix-engine-" + Guid.NewGuid().ToString("N"));
            var library = Path.Combine(_root, "steam-library");
            var descriptor = GameTargetCatalog.Get(target);
            GameDirectory = Path.Combine(library, "steamapps", "common", descriptor.Title);
            Directory.CreateDirectory(GameDirectory);
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"), "$game_data$ = false| true| $fs_root$| gamedata\\\n");
            _manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{descriptor.SteamAppId}.acf");
            File.WriteAllText(_manifestPath, $$"""
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

        public void SetBuildId(string buildId)
        {
            var manifest = File.ReadAllText(_manifestPath);
            manifest = System.Text.RegularExpressions.Regex.Replace(
                manifest,
                "\\\"buildid\\\"\\s+\\\"[^\\\"]+\\\"",
                $"\\\"buildid\\\" \\\"{buildId}\\\"");
            File.WriteAllText(_manifestPath, manifest);
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

    private static Encoding EncodingForPatchTest(int codePage)
    {
        if (codePage == 1251) Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static bool PathComparerForTests(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static byte[] PatchExpectedFragment(IEnumerable<TextPatchOperation> operations, byte[] sourceBytes)
    {
        var operationList = operations.ToArray();
        if (operationList.Length == 0) return sourceBytes;
        var encoding = PatchEncoding(operationList[0].CodePage);
        var text = encoding.GetString(sourceBytes);
        foreach (var operation in operationList)
        {
            var first = text.IndexOf(operation.ExpectedText, StringComparison.Ordinal);
            Assert.True(first >= 0, $"The catalogue fragment for {operation.RelativePath} was absent from the test source.");
            Assert.Equal(-1, text.IndexOf(operation.ExpectedText, first + operation.ExpectedText.Length, StringComparison.Ordinal));
            text = text[..first] + operation.ReplacementText + text[(first + operation.ExpectedText.Length)..];
        }
        return encoding.GetBytes(text);
    }

    private static Encoding PatchEncoding(int codePage)
    {
        if (codePage == 1251) Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

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
