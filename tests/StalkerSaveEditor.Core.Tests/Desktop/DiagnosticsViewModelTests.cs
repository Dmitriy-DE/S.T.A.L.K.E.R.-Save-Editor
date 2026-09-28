using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection("AppLog")]
public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public async Task Shows_every_check_and_counts_problems()
    {
        var vm = new DiagnosticsViewModel(() =>
        [
            new EnvironmentCheck("app", "data folder", CheckStatus.Ok, "fine"),
            new EnvironmentCheck("game:stalker-cop", "saves", CheckStatus.Warn, "no saves found", "choose the folder"),
            new EnvironmentCheck("codecs", "kraken (S2)", CheckStatus.Fail, "missing"),
        ]);

        await vm.RunChecksAsync();

        Assert.Equal(["OK", "!", "✕"], vm.Checks.Select(row => row.Status));
        Assert.True(vm.Checks[1].HasHint);
        Assert.Equal("Ошибок: 1, предупреждений: 1.", vm.Status);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public void Exports_a_bundle_and_reports_where()
    {
        var directory = Path.Combine(Path.GetTempPath(), "se-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.Configure(directory);
            var vm = new DiagnosticsViewModel(() => [], pendingCrash: "boom");
            Assert.True(vm.HasPendingCrash, "pending");

            var path = vm.ExportBundle(Path.Combine(directory, "report.txt.gz"));

            Assert.NotNull(path);
            Assert.True(new FileInfo(path).Length > 0, "length " + vm.Status);
            Assert.Contains("report.txt.gz", vm.Status, StringComparison.Ordinal);
            Assert.Null(vm.ExportBundle(directory));
            vm.DismissCrash();
            Assert.False(vm.HasPendingCrash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
