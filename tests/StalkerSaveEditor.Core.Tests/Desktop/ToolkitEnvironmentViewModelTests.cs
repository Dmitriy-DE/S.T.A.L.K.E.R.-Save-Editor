using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class ToolkitEnvironmentViewModelTests
{
    [Fact]
    public async Task Audit_shows_safe_stale_fix_action_and_refreshes_after_cleanup()
    {
        using var fixture = new EnvironmentFixture();
        var relativePath = "gamedata/configs/scripts/retired.ltx";
        fixture.Write(relativePath, "retired_fix = false\n");
        var definition = MakeRetiredFix(relativePath);
        new GameFixEngine(new PhysicalGameFileSystem(), allowSyntheticDefinitions: true).Install(definition, fixture.GameDirectory);

        var definitions = GameFixCatalog.All.ToDictionary(fix => fix.Id, StringComparer.Ordinal);
        var engine = new GameFixEngine();
        var configState = Path.Combine(fixture.Root, "config-state");
        var snapshots = new ToolkitSnapshotService(Path.Combine(fixture.Root, "snapshots"), engine, definitions, configStateDirectory: configState);
        var profiles = new ToolkitProfileService(Path.Combine(fixture.Root, "profiles"), engine, definitions, null, snapshots, configState);
        var viewModel = new ToolkitEnvironmentViewModel(
            () => (GameTarget.CallOfPripyat, fixture.GameDirectory), snapshots, profiles, configState);

        viewModel.Audit();
        var row = Assert.Single(viewModel.AuditRows);
        Assert.Equal(definition.Id, row.OwnerId);
        Assert.True(row.CanCleanup);

        await viewModel.CleanupOrphanAsync(row);

        Assert.Equal("retired_fix = false\n", File.ReadAllText(fixture.FilePath(relativePath)));
        var restoredFile = Assert.Single(viewModel.AuditRows);
        Assert.Equal(relativePath, restoredFile.RelativePath);
        Assert.Equal("НЕИЗВЕСТНО", restoredFile.Classification);
        Assert.False(restoredFile.CanCleanup);
        Assert.DoesNotContain(viewModel.AuditRows, auditRow => auditRow.OwnerId == definition.Id);
        Assert.Contains("Устаревший фикс снят", viewModel.Status, StringComparison.Ordinal);
    }

    private static GameFixDefinition MakeRetiredFix(string relativePath) => new(
        "cop.retired.environment-test",
        GameTarget.CallOfPripyat,
        "1.0.0",
        "Retired environment test fix",
        ["11450453"],
        GameFixCategory.Recommended,
        GameFixMaturity.Validated,
        [],
        [],
        [new TextPatchOperation(relativePath, "retired_fix = false\n", "retired_fix = true\n")],
        "Toolkit environment test")
    {
        Problem = "Test a stale manifest-owned fix.",
        Description = "Synthetic test-only definition.",
        VerificationState = GameFixVerificationState.RetailFilesVerified,
        DetectionMethod = "Synthetic install fixture.",
        References = ["https://example.test/environment"],
    };

    private sealed class EnvironmentFixture : IDisposable
    {
        public EnvironmentFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "save-editor-environment-audit-" + Guid.NewGuid().ToString("N"));
            GameDirectory = Path.Combine(Root, "steamapps", "common", "Call of Pripyat");
            Directory.CreateDirectory(GameDirectory);
            File.WriteAllText(Path.Combine(GameDirectory, "fsgame.ltx"), "$app_data_root$ = true| false| $fs_root$| _appdata_\\\n");
            File.WriteAllText(Path.Combine(Root, "steamapps", "appmanifest_41700.acf"), "\"AppState\" { \"appid\" \"41700\" \"buildid\" \"11450453\" \"installdir\" \"Call of Pripyat\" }");
        }

        public string Root { get; }

        public string GameDirectory { get; }

        public string FilePath(string relative) => Path.Combine(GameDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relative, string text)
        {
            var path = FilePath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
