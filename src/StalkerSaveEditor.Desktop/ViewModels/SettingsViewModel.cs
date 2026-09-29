using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class LanguageOption(string code, string name)
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed record AppearanceOption(string Id, string Name);
public sealed record UiScaleOption(int Percent, string Name);

public sealed class SettingsViewModel : ObservableViewModel
{
    private string _backupDirectory;
    private string _newSaveDirectory = string.Empty;
    private LanguageOption _selectedLanguage;
    private bool _soundEnabled = true;
    private int _soundVolume = 80;
    private string _settingsStatus = string.Empty;
    private bool _musicEnabled;
    private readonly string? _settingsPath;
    private AppSettings _stored;
    private readonly Lock _persistGate = new();
    private bool _sendReports;
    private string _themeId = "zone";
    private string _accentId = "amber";
    private int _uiScalePercent = 100;
    private ScaleTransform _uiScaleTransform = new(1, 1);

    public bool SendReports
    {
        get => _sendReports;
        set => SetProperty(ref _sendReports, value);
    }

    /// <summary>The first-run notice about reports is still to be answered.</summary>
    public bool ReportsNoticeVisible => !_stored.ReportsNoticeShown;

    public DateTime? LastReportUtc => _stored.LastReportUtc;

    /// <summary>Answer to the notice: keep sending (default) or switch it off.</summary>
    public void AnswerReportsNotice(bool send)
    {
        SendReports = send;
        _stored = _stored with { ReportsNoticeShown = true };
        TryPersist();
        OnPropertyChanged(nameof(ReportsNoticeVisible));
    }

    /// <summary>Called from the report task: stores only the time, never the screen's unsaved edits.</summary>
    public void MarkReportSent(DateTime utc)
    {
        lock (_persistGate)
        {
            _stored = _stored with { LastReportUtc = utc };
            try
            {
                if (_settingsPath is not null) _stored.Save(_settingsPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Warn("report time not stored", exception);
            }
        }
    }

    /// <summary>Raised after the user saved; the window applies sound settings.</summary>
    public event EventHandler? Saved;

    public bool MusicEnabled
    {
        get => _musicEnabled;
        set => SetProperty(ref _musicEnabled, value);
    }

    public SettingsViewModel(
        IEnumerable<string> saveDirectories,
        string backupDirectory,
        string? currentLanguageCode = null,
        string? settingsPath = null,
        AppSettings? stored = null)
    {
        _settingsPath = settingsPath;
        _stored = stored ?? new AppSettings();
        _sendReports = _stored.SendReports;
        if (stored is not null)
        {
            currentLanguageCode = stored.Language ?? currentLanguageCode;
            _soundEnabled = stored.SoundEnabled;
            _soundVolume = Math.Clamp(stored.SoundVolume, 0, 100);
            _musicEnabled = stored.MusicEnabled;
        }

        _themeId = NormalizeThemeId(_stored.ThemeId);
        _accentId = NormalizeAccentId(_stored.AccentId);
        _uiScalePercent = NormalizeUiScale(_stored.UiScalePercent);
        _uiScaleTransform = new ScaleTransform(_uiScalePercent / 100d, _uiScalePercent / 100d);
        StalkerTheme.ApplyAppearance(_themeId, _accentId, _uiScalePercent);

        Themes =
        [
            new AppearanceOption("zone", L.T("Зона (тёмная)")),
            new AppearanceOption("clear-sky", L.T("Чистое небо")),
            new AppearanceOption("day", L.T("День")),
        ];
        Accents =
        [
            new AppearanceOption("amber", L.T("Янтарный")),
            new AppearanceOption("teal", L.T("Бирюзовый")),
            new AppearanceOption("blue", L.T("Синий")),
            new AppearanceOption("rust", L.T("Ржавый")),
        ];
        UiScales = [new UiScaleOption(100, "100%"), new UiScaleOption(125, "125%"), new UiScaleOption(150, "150%")];

        SaveDirectories = new ObservableCollection<string>(saveDirectories);
        _backupDirectory = backupDirectory;

        Languages =
        [
            new LanguageOption("ru", "Русский"),
            new LanguageOption("uk", "Українська"),
            new LanguageOption("en", "English"),
            new LanguageOption("de", "Deutsch"),
            new LanguageOption("fr", "Français"),
            new LanguageOption("it", "Italiano"),
            new LanguageOption("es", "Español"),
            new LanguageOption("pl", "Polski"),
            new LanguageOption("cs", "Čeština"),
            new LanguageOption("pt_BR", "Português (Brasil)"),
            new LanguageOption("tr", "Türkçe"),
            new LanguageOption("ja", "日本語"),
            new LanguageOption("ko", "한국어"),
            new LanguageOption("zh_CN", "简体中文"),
            new LanguageOption("zh_TW", "繁體中文"),
        ];

        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == (currentLanguageCode ?? I18nService.Instance.CurrentLanguage)) ?? Languages[0];

        AddSaveDirectoryCommand = new RelayCommand(() => AddSaveDirectory(NewSaveDirectory), () => !string.IsNullOrWhiteSpace(NewSaveDirectory));
        RemoveSaveDirectoryCommand = new RelayCommand<string>(RemoveSaveDirectory);
        AutoDetectSaveDirectoriesCommand = new RelayCommand(() => AutoDetectSaveDirectories());
        SaveSettingsCommand = new RelayCommand(SaveSettings);
    }

    public ObservableCollection<string> SaveDirectories { get; }
    public IReadOnlyList<LanguageOption> Languages { get; }
    public IReadOnlyList<AppearanceOption> Themes { get; }
    public IReadOnlyList<AppearanceOption> Accents { get; }
    public IReadOnlyList<UiScaleOption> UiScales { get; }

    public string ThemeId
    {
        get => _themeId;
        set
        {
            var normalized = NormalizeThemeId(value);
            if (SetProperty(ref _themeId, normalized)) ApplyVisualPreferences();
        }
    }

    public string AccentId
    {
        get => _accentId;
        set
        {
            var normalized = NormalizeAccentId(value);
            if (SetProperty(ref _accentId, normalized)) ApplyVisualPreferences();
        }
    }

    public int UiScalePercent
    {
        get => _uiScalePercent;
        set
        {
            var normalized = NormalizeUiScale(value);
            if (!SetProperty(ref _uiScalePercent, normalized)) return;
            _uiScaleTransform = new ScaleTransform(normalized / 100d, normalized / 100d);
            OnPropertyChanged(nameof(UiScaleFactor));
            OnPropertyChanged(nameof(UiScaleTransform));
            ApplyVisualPreferences();
        }
    }

    public double UiScaleFactor => _uiScalePercent / 100d;
    public ScaleTransform UiScaleTransform => _uiScaleTransform;

    public RelayCommand AddSaveDirectoryCommand { get; }
    public RelayCommand<string> RemoveSaveDirectoryCommand { get; }
    public RelayCommand AutoDetectSaveDirectoriesCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }

    public string BackupDirectory
    {
        get => _backupDirectory;
        set => SetProperty(ref _backupDirectory, value);
    }

    public string NewSaveDirectory
    {
        get => _newSaveDirectory;
        set
        {
            if (SetProperty(ref _newSaveDirectory, value))
            {
                AddSaveDirectoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => SetProperty(ref _soundEnabled, value);
    }

    public int SoundVolume
    {
        get => _soundVolume;
        set => SetProperty(ref _soundVolume, Math.Clamp(value, 0, 100));
    }

    public string SettingsStatus
    {
        get => _settingsStatus;
        set => SetProperty(ref _settingsStatus, value);
    }

    public bool AddSaveDirectory(string? directory)
    {
        var path = directory?.Trim();
        if (!string.IsNullOrEmpty(path) && !SaveDirectories.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            SaveDirectories.Add(path);
            NewSaveDirectory = string.Empty;
            SettingsStatus = L.T("Папка добавлена: {0}", path);
            TryPersist();
            return true;
        }
        return false;
    }

    public void RemoveSaveDirectory(string? directory)
    {
        if (directory is not null && SaveDirectories.Remove(directory))
        {
            SettingsStatus = L.T("Папка удалена из списка.");
            TryPersist();
        }
    }

    public int AutoDetectSaveDirectories()
    {
        var detected = SaveDirectoryDiscovery.GetExistingDirectories();
        int added = 0;
        foreach (var dir in detected)
        {
            if (!SaveDirectories.Contains(dir, StringComparer.OrdinalIgnoreCase))
            {
                SaveDirectories.Add(dir);
                added++;
            }
        }
        if (added > 0) TryPersist();
        SettingsStatus = added > 0
            ? L.T("Автопоиск завершён. Добавлено папок: {0}.", added)
            : L.T("Автопоиск завершён. Новых папок не найдено.");
        return added;
    }

    public void SaveSettings()
    {
        try
        {
            Persist();
            SettingsStatus = _settingsPath is null ? L.T("Настройки применены.") : L.T("Настройки сохранены.");
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SettingsStatus = L.T("Не удалось сохранить настройки: ") + exception.Message;
        }
    }

    private void TryPersist()
    {
        try
        {
            Persist();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SettingsStatus = L.T("Не удалось сохранить настройки: ") + exception.Message;
        }
    }

    public AppSettings ToSettings() => _stored with
    {
        SendReports = SendReports,
        SaveDirectories = [.. SaveDirectories],
        BackupDirectory = BackupDirectory,
        Language = SelectedLanguage.Code,
        SoundEnabled = SoundEnabled,
        SoundVolume = SoundVolume,
        MusicEnabled = MusicEnabled,
        ThemeId = ThemeId,
        AccentId = AccentId,
        UiScalePercent = UiScalePercent,
    };

    private void ApplyVisualPreferences()
    {
        StalkerTheme.ApplyAppearance(ThemeId, AccentId, UiScalePercent);
        lock (_persistGate)
        {
            _stored = _stored with { ThemeId = ThemeId, AccentId = AccentId, UiScalePercent = UiScalePercent };
            try
            {
                if (_settingsPath is not null) _stored.Save(_settingsPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SettingsStatus = L.T("Не удалось сохранить настройки: ") + exception.Message;
            }
        }
    }

    private static string NormalizeThemeId(string? id) => id is "clear-sky" or "day" ? id : "zone";
    private static string NormalizeAccentId(string? id) => id is "teal" or "blue" or "rust" ? id : "amber";
    private static int NormalizeUiScale(int percent) => percent is 125 or 150 ? percent : 100;

    /// <summary>Writes settings.json (only in the interactive app; tests and screenshots have no path).</summary>
    private void Persist()
    {
        lock (_persistGate)
        {
            _stored = ToSettings();
            if (_settingsPath is not null) _stored.Save(_settingsPath);
        }
    }
}
