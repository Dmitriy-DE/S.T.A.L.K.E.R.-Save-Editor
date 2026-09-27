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

    public SettingsViewModel(
        IEnumerable<string> saveDirectories,
        string backupDirectory,
        string currentLanguageCode = "ru")
    {
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

        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == currentLanguageCode) ?? Languages[0];

        AddSaveDirectoryCommand = new RelayCommand(AddSaveDirectory, () => !string.IsNullOrWhiteSpace(NewSaveDirectory));
        RemoveSaveDirectoryCommand = new RelayCommand<string>(RemoveSaveDirectory);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
    }

    public ObservableCollection<string> SaveDirectories { get; }
    public IReadOnlyList<LanguageOption> Languages { get; }

    public RelayCommand AddSaveDirectoryCommand { get; }
    public RelayCommand<string> RemoveSaveDirectoryCommand { get; }
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

    private void AddSaveDirectory()
    {
        var path = _newSaveDirectory.Trim();
        if (!string.IsNullOrEmpty(path) && !SaveDirectories.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            SaveDirectories.Add(path);
            NewSaveDirectory = string.Empty;
            SettingsStatus = "Папка добавлена в список поиска.";
        }
    }

    private void RemoveSaveDirectory(string? directory)
    {
        if (directory is not null && SaveDirectories.Remove(directory))
        {
            SettingsStatus = "Папка удалена из списка.";
        }
    }

    private void SaveSettings()
    {
        SettingsStatus = "Настройки сохранены.";
    }
}
