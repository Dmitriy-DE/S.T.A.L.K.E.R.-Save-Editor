using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

// Not in parallel with other classes: the appearance is one per process, and every settings view model created
// elsewhere applies its own, which reset the theme between this class's change and its check (seen in CI).
[Collection(AvaloniaViewTestGroup.Name)]
public sealed class SettingsAndAudioTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "se-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Settings_survive_a_restart_including_folders_added_by_hand()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new SettingsViewModel(["/a"], "/backups", settingsPath: path);
        first.AddSaveDirectory("/games/cop/savedgames");
        first.SoundVolume = 35;
        first.MusicEnabled = true;
        first.SaveSettings();

        var stored = AppSettings.Load(path);
        var second = new SettingsViewModel(stored.SaveDirectories!, stored.BackupDirectory!, settingsPath: path, stored: stored);

        Assert.Equal(["/a", "/games/cop/savedgames"], second.SaveDirectories);
        Assert.Equal(35, second.SoundVolume);
        Assert.True(second.MusicEnabled);
    }

    [Fact]
    public void Visual_preferences_apply_live_and_persist_without_the_save_button()
    {
        var path = Path.Combine(_directory, "settings.json");
        StalkerTheme.ApplyAppearance("zone", "amber", 100);
        var originalBackground = StalkerTheme.BgBase;

        try
        {
            var settings = new SettingsViewModel(["/a"], "/backups", settingsPath: path);
            settings.ThemeId = "clear-sky";
            settings.AccentId = "teal";
            settings.UiScalePercent = 125;

            var saved = AppSettings.Load(path);
            Assert.Equal("clear-sky", saved.ThemeId);
            Assert.Equal("teal", saved.AccentId);
            Assert.Equal(125, saved.UiScalePercent);
            Assert.NotEqual(originalBackground, StalkerTheme.BgBase);
            Assert.Equal(1.25, settings.UiScaleFactor);

            var reloaded = new SettingsViewModel(
                saved.SaveDirectories ?? ["/a"],
                saved.BackupDirectory ?? "/backups",
                settingsPath: path,
                stored: saved);
            Assert.Equal("clear-sky", reloaded.ThemeId);
            Assert.Equal("teal", reloaded.AccentId);
            Assert.Equal(125, reloaded.UiScalePercent);
        }
        finally
        {
            StalkerTheme.ApplyAppearance("zone", "amber", 100);
        }
    }

    [Fact]
    public void The_reports_notice_is_answered_once_and_the_answer_is_kept()
    {
        var path = Path.Combine(_directory, "settings.json");
        var settings = new SettingsViewModel(["/a"], "/b", settingsPath: path);
        Assert.True(settings.ReportsNoticeVisible);
        Assert.True(settings.SendReports);

        settings.AnswerReportsNotice(send: false);
        settings.MarkReportSent(new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
        var stored = AppSettings.Load(path);

        Assert.True(stored.ReportsNoticeShown);
        Assert.False(stored.SendReports);
        Assert.NotNull(stored.LastReportUtc);
        Assert.False(new SettingsViewModel(["/a"], "/b", settingsPath: path, stored: stored).ReportsNoticeVisible);
    }

    [Fact]
    public void Damaged_settings_fall_back_to_defaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ not json");

        var settings = AppSettings.Load(path);

        Assert.Null(settings.SaveDirectories);
        Assert.True(settings.SoundEnabled);
    }

    [Theory]
    [InlineData("stalker-soc", "soc")]
    [InlineData("stalker-cs-ee", "clear_sky")]
    [InlineData("stalker-cop", "cop")]
    [InlineData("stalker2", "cop")]
    public void Each_release_uses_its_own_game_sounds(string releaseId, string family)
    {
        Assert.Equal(family, GameAudioService.FamilyOf(releaseId));
    }

    [Fact]
    public void Shipped_sounds_of_all_three_games_decode()
    {
        var result = GameAudioValidator.Validate(
            Path.Combine(RepositoryRoot(), "src", "StalkerSaveEditor.Desktop", "Assets", "Sounds", "game"),
            _directory);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(21, result.VerifiedSounds);
        var wav = Directory.GetFiles(_directory, "soc-music-*.wav").Single();
        var header = File.ReadAllBytes(wav).AsSpan(0, 24).ToArray();
        Assert.Equal("RIFF"u8.ToArray(), header[..4]);
        Assert.Equal(2, BitConverter.ToInt16(header, 22)); // two mono halves → stereo
        var bytes = File.ReadAllBytes(wav);
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
        Assert.Equal(bytes.Length - 44, BitConverter.ToInt32(bytes, 40));
    }

    [Fact]
    public void A_volume_change_replaces_the_cached_sound_instead_of_adding_one()
    {
        var service = new GameAudioService(
            Path.Combine(RepositoryRoot(), "src", "StalkerSaveEditor.Desktop", "Assets", "Sounds", "game"),
            _directory);
        service.Apply(enabled: true, volume: 80, music: false);
        Assert.NotNull(service.Prepare("cop", ["menu_accept.ogg"], "cop-Click"));
        service.Apply(enabled: true, volume: 40, music: false);
        var wav = service.Prepare("cop", ["menu_accept.ogg"], "cop-Click");

        Assert.Equal(wav, Assert.Single(Directory.GetFiles(_directory, "cop-Click-v*.wav")));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }
}
