using System.IO.Compression;
using System.Text;
using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

[Collection("AppLog")]
public sealed class DiagnosticsTests : IDisposable
{
    private readonly TemporaryAppLog _log = new();
    private readonly string _directory;

    public DiagnosticsTests() => _directory = _log.Directory;

    public void Dispose() => _log.Dispose();

    [Fact]
    public void The_tail_cap_counts_utf8_bytes_and_never_splits_a_character()
    {
        Assert.Equal("abc", DiagnosticsBundle.TailWithinBytes("abc", 3));
        Assert.Equal("bc", DiagnosticsBundle.TailWithinBytes("abc", 2));
        // Two bytes per Cyrillic letter: five bytes hold two letters, not five.
        Assert.Equal("ог", DiagnosticsBundle.TailWithinBytes("сталкер лог", 5));
        // A surrogate pair is four bytes and is kept whole or not at all.
        Assert.Equal("b", DiagnosticsBundle.TailWithinBytes("a\U0001F600b", 4));
        Assert.Equal("\U0001F600b", DiagnosticsBundle.TailWithinBytes("a\U0001F600b", 5));
        Assert.Empty(DiagnosticsBundle.TailWithinBytes("abc", 0));
    }

    [Fact]
    public void A_huge_non_latin_environment_report_does_not_push_the_bundle_past_its_byte_cap()
    {
        var bundle = DiagnosticsBundle.Create(new string('ж', 3 * 1024 * 1024));

        using var gzip = new System.IO.Compression.GZipStream(new MemoryStream(bundle), System.IO.Compression.CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gzip.CopyTo(plain);
        Assert.InRange(plain.Length, 1, 2 * 1024 * 1024);
        Assert.Contains("--- environment ---", System.Text.Encoding.UTF8.GetString(plain.ToArray()), StringComparison.Ordinal);
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
    public void Redacts_wine_user_folders_and_steam_id64()
    {
        var text = AppLog.Redact("/games/pfx/drive_c/users/alice/AppData id=76561198012345678 C:\\x\\drive_c\\users\\bob\\y");

        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bob", text, StringComparison.Ordinal);
        Assert.DoesNotContain("76561198012345678", text, StringComparison.Ordinal);
        Assert.Contains("<steamid>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Daily_bundle_carries_only_lines_written_after_the_last_report()
    {
        const string log = "2026-09-27T10:00:00.000Z INFO old line\nSystem.Exception: old trace\n2026-09-28T10:00:00.000Z INFO new line\n  at new trace\n";

        var tail = DiagnosticsBundle.LinesAfter(log, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("2026-09-28T10:00:00.000Z INFO new line\n  at new trace\n", tail);
        Assert.Empty(DiagnosticsBundle.LinesAfter(log, new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc)));
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
