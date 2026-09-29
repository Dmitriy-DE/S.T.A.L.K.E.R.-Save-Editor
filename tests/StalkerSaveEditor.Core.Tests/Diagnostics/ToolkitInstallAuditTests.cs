using System.Security.Cryptography;
using System.Text;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class ToolkitInstallAuditTests
{
    [Fact]
    public void Unknown_loose_files_and_manifestless_state_are_reported_without_cleanup_candidates()
    {
        using var fixture = new AuditFixture();
        fixture.Write("gamedata/scripts/unclassified.script", "user = true\n");
        fixture.Write(".save-editor-game-fixes/orphan/backups/0.bin", "possibly local state\n");

        var report = ToolkitInstallAudit.Analyze(GameTarget.CallOfPripyat, fixture.GameDirectory);

        Assert.False(report.VanillaBaselineBundled);
        Assert.Contains(report.Entries, entry => entry.RelativePath == "gamedata/scripts/unclassified.script" &&
            entry.Classification == ToolkitAuditClassification.Unknown && !entry.CanCleanup);
        Assert.Contains(report.Entries, entry => entry.RelativePath == ".save-editor-game-fixes/orphan/backups/0.bin" &&
            entry.Classification == ToolkitAuditClassification.OrphanedStateNeedsReview && !entry.CanCleanup);
        Assert.Equal(0, report.SafeCleanupCandidateCount);
    }

    [Fact]
    public void Exact_build_file_matching_a_known_retail_hash_is_classified_as_vanilla()
    {
        using var fixture = new AuditFixture();
        var relativePath = "gamedata/configs/scripts/vanilla.ltx";
        var source = Encoding.Latin1.GetBytes("vanilla = true\n");
        fixture.Write(relativePath, source);
        var definition = MakeFix("cop.audit-baseline", relativePath, "vanilla = true\n", "vanilla = false\n") with
        {
            TextPatches =
            [
                new TextPatchOperation(relativePath, "vanilla = true\n", "vanilla = false\n")
                {
                    ExpectedFileSha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
                },
            ],
        };

        var report = ToolkitInstallAudit.Analyze(GameTarget.CallOfPripyat, fixture.GameDirectory, [definition]);

        Assert.Contains(report.Entries, entry => entry.RelativePath == relativePath &&
            entry.Classification == ToolkitAuditClassification.Vanilla && entry.Status == GameDoctorStatus.Ok);
    }

    [Fact]
    public void Manifest_verified_fix_from_removed_catalogue_can_be_cleaned_through_game_fix_provider()
    {
        using var fixture = new AuditFixture();
        var relativePath = "gamedata/configs/scripts/retired.ltx";
        var original = "retired_fix = false\n";
        fixture.Write(relativePath, original);
        var definition = MakeFix("cop.retired.audit-test", relativePath, original, "retired_fix = true\n");
        new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true).Install(definition, fixture.GameDirectory);

        var report = ToolkitInstallAudit.Analyze(GameTarget.CallOfPripyat, fixture.GameDirectory);
        var orphan = Assert.Single(report.Entries, entry => entry.RelativePath == relativePath);
        Assert.Equal(ToolkitAuditClassification.OrphanedToolkitOwned, orphan.Classification);
        Assert.True(orphan.CanCleanup);
        Assert.Equal(definition.Id, orphan.OwnerId);
        Assert.Equal(1, report.SafeCleanupCandidateCount);

        var result = ToolkitInstallAudit.Cleanup(GameTarget.CallOfPripyat, fixture.GameDirectory, definition.Id);

        Assert.True(result.Changed);
        Assert.Equal(original, File.ReadAllText(fixture.FilePath(relativePath)));
        Assert.DoesNotContain(ToolkitInstallAudit.Analyze(GameTarget.CallOfPripyat, fixture.GameDirectory).Entries,
            entry => entry.Classification == ToolkitAuditClassification.OrphanedToolkitOwned);
    }

    [Fact]
    public void Orphan_cleanup_refuses_external_drift_and_leaves_the_file_untouched()
    {
        using var fixture = new AuditFixture();
        var relativePath = "gamedata/configs/scripts/retired.ltx";
        fixture.Write(relativePath, "retired_fix = false\n");
        var definition = MakeFix("cop.retired.drift-test", relativePath, "retired_fix = false\n", "retired_fix = true\n");
        new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true).Install(definition, fixture.GameDirectory);
        var external = "retired_fix = user-change\n";
        File.WriteAllText(fixture.FilePath(relativePath), external);

        var report = ToolkitInstallAudit.Analyze(GameTarget.CallOfPripyat, fixture.GameDirectory);
        var orphan = Assert.Single(report.Entries, entry => entry.RelativePath == relativePath);
        Assert.Equal(ToolkitAuditClassification.OrphanedToolkitOwned, orphan.Classification);
        Assert.False(orphan.CanCleanup);
        Assert.Equal(0, report.SafeCleanupCandidateCount);

        Assert.Throws<InvalidOperationException>(() => ToolkitInstallAudit.Cleanup(GameTarget.CallOfPripyat, fixture.GameDirectory, definition.Id));
        Assert.Equal(external, File.ReadAllText(fixture.FilePath(relativePath)));
    }

    private static GameFixDefinition MakeFix(string id, string relativePath, string expected, string replacement) => new(
        id,
        GameTarget.CallOfPripyat,
        "1.0.0",
        "Audit fixture fix",
        ["11450453"],
        GameFixCategory.Recommended,
        GameFixMaturity.Validated,
        [],
        [],
        [new TextPatchOperation(relativePath, expected, replacement)],
        "Toolkit install audit test")
    {
        Problem = "Test an orphaned manifest-owned fix.",
        Description = "Synthetic test-only definition.",
        VerificationState = GameFixVerificationState.RetailFilesVerified,
        DetectionMethod = "Synthetic install fixture.",
        References = ["https://example.test/audit"],
    };

    private sealed class AuditFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "save-editor-toolkit-audit-" + Guid.NewGuid().ToString("N"));

        public AuditFixture()
        {
            GameDirectory = Path.Combine(_root, "steamapps", "common", "Call of Pripyat");
            Directory.CreateDirectory(GameDirectory);
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"), "$app_data_root$ = true| false| $fs_root$| _appdata_\\\n");
            File.WriteAllText(Path.Combine(_root, "steamapps", "appmanifest_41700.acf"), "\"AppState\" { \"appid\" \"41700\" \"buildid\" \"11450453\" \"installdir\" \"Call of Pripyat\" }");
        }

        public string GameDirectory { get; }

        public string FilePath(string relative) => System.IO.Path.Combine(GameDirectory, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public void Write(string relative, string text)
        {
            Write(relative, Encoding.UTF8.GetBytes(text));
        }

        public void Write(string relative, byte[] bytes)
        {
            var path = FilePath(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
