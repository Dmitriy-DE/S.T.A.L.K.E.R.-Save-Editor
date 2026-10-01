using StalkerSaveEditor.Core.Patching;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Patching;

public sealed class ManagedUserLtxSettingsTests
{
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "save-editor-user-ltx-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void A_setting_that_was_absent_is_absent_again_after_two_overrides_and_a_restore()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "user.ltx");
        var state = Path.Combine(directory.Path, "toolkit-state");
        const string original = "custom_setting stays\r\n";
        File.WriteAllText(path, original);

        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["g_fov"] = "90" });
        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["g_fov"] = "100" });
        Assert.Contains("g_fov 100", File.ReadAllText(path), StringComparison.Ordinal);
        ManagedUserLtxSettings.RestoreDefault(path, state, "g_fov");

        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void A_utf8_file_keeps_its_bom_and_is_read_the_same_way_after_a_write()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "user.ltx");
        var state = Path.Combine(directory.Path, "toolkit-state");
        var bom = System.Text.Encoding.UTF8.Preamble.ToArray();
        File.WriteAllBytes(path, [.. bom, .. System.Text.Encoding.UTF8.GetBytes("; настройки игрока\r\ng_fov 67.5\r\n")]);

        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["g_fov"] = "90" });

        var written = File.ReadAllBytes(path);
        Assert.True(written.AsSpan().StartsWith(bom));
        Assert.Contains("; настройки игрока", System.Text.Encoding.UTF8.GetString(written), StringComparison.Ordinal);
        Assert.False(ManagedUserLtxSettings.Inspect(path, state).HasConflict);
        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["g_fov"] = "95" });
        ManagedUserLtxSettings.RestoreDefault(path, state, "g_fov");
        Assert.Equal([.. bom, .. System.Text.Encoding.UTF8.GetBytes("; настройки игрока\r\ng_fov 67.5\r\n")], File.ReadAllBytes(path));
    }

    [Fact]
    public void Applies_known_overrides_and_restore_default_restores_exact_original_line()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "user.ltx");
        var state = Path.Combine(directory.Path, "toolkit-state");
        File.WriteAllText(path, "// player settings\r\ng_fov 67.5 ; original\r\ncustom_setting untouched\r\n");

        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string>
        {
            ["g_fov"] = "90",
            ["hud_crosshair"] = "off",
        });

        var content = File.ReadAllText(path);
        Assert.Contains("g_fov 90 ; original\r\n", content, StringComparison.Ordinal);
        Assert.Contains("custom_setting untouched\r\n", content, StringComparison.Ordinal);
        Assert.Contains("hud_crosshair off", content, StringComparison.Ordinal);
        var report = ManagedUserLtxSettings.Inspect(path, state);
        Assert.All(report.Settings.Where(setting => setting.ChangedByToolkit), setting => Assert.True(setting.CanRestoreDefault));

        ManagedUserLtxSettings.RestoreDefault(path, state, "g_fov");
        ManagedUserLtxSettings.RestoreDefault(path, state, "hud_crosshair");

        Assert.Equal("// player settings\r\ng_fov 67.5 ; original\r\ncustom_setting untouched\r\n", File.ReadAllText(path));
        Assert.DoesNotContain(ManagedUserLtxSettings.Inspect(path, state).Settings, setting => setting.ChangedByToolkit);
    }

    [Fact]
    public void Refuses_to_change_a_managed_setting_after_external_drift()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "user.ltx");
        var state = Path.Combine(directory.Path, "toolkit-state");
        File.WriteAllText(path, "g_fov 67.5\n");
        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["g_fov"] = "90" });
        File.WriteAllText(path, "g_fov 100\n");
        var before = File.ReadAllBytes(path);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["g_fov"] = "80" }));

        Assert.Contains("changed outside the toolkit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("unknown_setting", "1")]
    [InlineData("g_fov", "999")]
    [InlineData("mouse_sens", "NaN")]
    [InlineData("hud_crosshair", "true")]
    public void Rejects_unknown_settings_and_out_of_range_values(string key, string value)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "user.ltx");
        File.WriteAllText(path, "");

        Assert.Throws<ArgumentException>(() => ManagedUserLtxSettings.SetOverrides(
            path,
            Path.Combine(directory.Path, "toolkit-state"),
            new Dictionary<string, string> { [key] = value }));
    }

    [Fact]
    public void Original_absence_is_reported_as_engine_default_and_removed_on_restore()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "user.ltx");
        var state = Path.Combine(directory.Path, "toolkit-state");
        File.WriteAllText(path, "other_setting 1\n");
        ManagedUserLtxSettings.SetOverrides(path, state, new Dictionary<string, string> { ["hud_fov"] = "0.45" });

        var setting = Assert.Single(ManagedUserLtxSettings.Inspect(path, state).Settings, value => value.Key == "hud_fov");

        Assert.Null(setting.OriginalValue);
        Assert.Equal("0.45", setting.CurrentValue);
        Assert.True(setting.ChangedByToolkit);
        ManagedUserLtxSettings.RestoreDefault(path, state, "hud_fov");
        Assert.Equal("other_setting 1\n", File.ReadAllText(path));
    }
}
