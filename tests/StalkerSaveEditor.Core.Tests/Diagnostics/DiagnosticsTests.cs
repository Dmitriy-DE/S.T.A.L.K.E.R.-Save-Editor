using System.IO.Compression;
using System.Text;
using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

[Collection("AppLog")]
public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "se-logs-" + Guid.NewGuid().ToString("N"));

    public DiagnosticsTests() => AppLog.Configure(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Redacts_home_directories_and_steam_ids()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = AppLog.Redact($"{home}/saves /home/someone/x C:\\Users\\Bob\\AppData Steam/userdata/990854815/1643320");

        Assert.DoesNotContain(home, text, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", text, StringComparison.Ordinal);
        Assert.DoesNotContain("990854815", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Crash_is_kept_for_the_next_start_and_can_be_dismissed()
    {
        CrashReporter.Record("test crash", new InvalidOperationException("boom"));

        Assert.Contains("boom", CrashReporter.Pending(), StringComparison.Ordinal);
        Assert.Contains("test crash", File.ReadAllText(Path.Combine(_directory, AppLog.FileName)), StringComparison.Ordinal);
        CrashReporter.Dismiss();
        Assert.Null(CrashReporter.Pending());
    }

    [Fact]
    public void Bundle_contains_crash_environment_and_log_without_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AppLog.Info($"opened {home}/save.sav");
        CrashReporter.Record("crash", new InvalidDataException("bad"));

        var bundle = DiagnosticsBundle.Create("[Ok] app / data: fine");
        using var gzip = new GZipStream(new MemoryStream(bundle), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var text = reader.ReadToEnd();

        Assert.Contains("--- crash ---", text, StringComparison.Ordinal);
        Assert.Contains("--- environment ---", text, StringComparison.Ordinal);
        Assert.Contains("opened <home>/save.sav", text, StringComparison.Ordinal);
        Assert.DoesNotContain(home + "/", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_rotates_instead_of_growing_forever()
    {
        var line = new string('x', 64 * 1024);
        for (var index = 0; index < 40; index++) AppLog.Info(line);

        var size = new FileInfo(Path.Combine(_directory, AppLog.FileName)).Length;
        Assert.True(size <= 1024 * 1024, $"log is {size} bytes");
        Assert.True(File.Exists(Path.Combine(_directory, AppLog.FileName + ".1")));
    }

    [Fact]
    public void Doctor_returns_every_check_group_even_when_one_fails()
    {
        var checks = EnvironmentDoctor.Run([() => throw new InvalidOperationException("broken provider")], includeMachineChecks: false);

        Assert.Contains(checks, check => check.Group == "app");
        Assert.Contains(checks, check => check.Group == "codecs");
        Assert.Contains(checks, check => check.Group == "extra" && check.Status == CheckStatus.Fail);
        Assert.Contains("[", EnvironmentDoctor.Format(checks), StringComparison.Ordinal);
    }
}
