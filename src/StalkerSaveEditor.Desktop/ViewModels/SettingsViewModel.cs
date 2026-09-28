using StalkerSaveEditor.Desktop.Services;
using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class LanguageOption(string code, string name)
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

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
        if (stored is not null)
        {
            currentLanguageCode = stored.Language ?? currentLanguageCode;
            _soundEnabled = stored.SoundEnabled;
            _soundVolume = Math.Clamp(stored.SoundVolume, 0, 100);
            _musicEnabled = stored.MusicEnabled;
        }

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

    public AppSettings ToSettings() => new()
    {
        SaveDirectories = [.. SaveDirectories],
        BackupDirectory = BackupDirectory,
        Language = SelectedLanguage.Code,
        SoundEnabled = SoundEnabled,
        SoundVolume = SoundVolume,
        MusicEnabled = MusicEnabled,
    };

    /// <summary>Writes settings.json (only in the interactive app; tests and screenshots have no path).</summary>
    private void Persist()
    {
        if (_settingsPath is not null) ToSettings().Save(_settingsPath);
    }
}
