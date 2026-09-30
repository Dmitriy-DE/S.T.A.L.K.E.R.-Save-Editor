using StalkerSaveEditor.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class SaveLibraryViewModel : ObservableViewModel
{
    private static readonly IReadOnlyDictionary<string, CatalogBundle> Catalogs = CatalogBundleReader.LoadEmbedded();
    internal static bool TryCatalog(string releaseId, out CatalogBundle bundle) =>
        GameContentRegistry.TryGetCatalog(releaseId, out bundle) || Catalogs.TryGetValue(releaseId, out bundle!);

    internal static readonly OfficialNamesCatalog OfficialNames = OfficialNamesCatalog.LoadEmbedded();

    /// <summary>The editor's language as the name catalogs spell it (zh_CN, pt_BR).</summary>
    internal static string NamesLanguage => I18nService.Instance.CurrentLanguage.Replace('-', '_');

    private readonly Func<IReadOnlyList<string>> _saveDirectoriesProvider;
    private readonly Func<string> _backupDirectoryProvider;
    private readonly DraftStore _draftStore;

    private SaveFileSummary? _selectedSave;
    private BackupRecordViewModel? _selectedBackup;
    private DraftJournal? _currentJournal;

    private string _selectedTab = "overview";
    private string _moneyInput = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isSaving;

    private string _inventorySearchText = string.Empty;
    private string _selectedCategory = "all";
    private InventoryLineViewModel? _selectedItem;

    /// <summary>Set while a draft is applied to the rows, so the rows' change events do not record it again.</summary>
    private bool _applyingPlan;

    /// <summary>Row properties that are edits; the rest (displays, colours) only follow them.</summary>
    private static readonly HashSet<string> EditableItemProperties =
    [
        nameof(InventoryLineViewModel.CountInput),
        nameof(InventoryLineViewModel.ConditionPercent),
        nameof(InventoryLineViewModel.Placement),
        nameof(InventoryLineViewModel.IsDeleted),
        nameof(InventoryLineViewModel.UpgradeItems),
    ];

    public SaveLibraryViewModel(
        bool discoverLocalSaves = true,
        Func<IReadOnlyList<string>>? saveDirectoriesProvider = null,
        Func<string>? backupDirectoryProvider = null,
        string? draftsDirectory = null)
    {
        // The interactive app keeps preferences in settings.json; tests and screenshots never touch it.
        var settingsPath = InteractiveApp ? AppSettings.DefaultPath : null;
        var stored = settingsPath is null ? null : AppSettings.Load(settingsPath);
        if (stored?.Language is { } language) I18nService.Instance.SetLanguage(language);
        _saveDirectoriesProvider = saveDirectoriesProvider ?? (HostPlatform.IsBrowser
            ? () => [HostPlatform.OpenedSavesDirectory]
            : () => Settings?.SaveDirectories.ToArray() ?? SaveDirectoryDiscovery.GetExistingDirectories());
        _backupDirectoryProvider = backupDirectoryProvider ?? (() => Settings is { BackupDirectory.Length: > 0 } settings ? settings.BackupDirectory : GetDefaultBackupDirectory());
        SaveDoctor = new SaveDoctorViewModel(_backupDirectoryProvider);
        SaveDoctor.SaveRepaired += OnSaveRepaired;
        SaveDoctor.OpenGameFixRequested += fixId =>
        {
            GameFixes.ShowFix(fixId);
            SelectedTab = "game-fixes";
        };
        _draftStore = new DraftStore(draftsDirectory);
        if (!Directory.Exists(_draftStore.DirectoryPath))
        {
            Directory.CreateDirectory(_draftStore.DirectoryPath);
        }

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        SaveCommand = new RelayCommand(SaveSelected, () => CanSave);
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        DiscardDraftCommand = new RelayCommand(DiscardDraft, () => HasDraftChanges);

        SelectTabCommand = new RelayCommand<string>(tab => SelectedTab = tab ?? "overview");
        AddMoneyCommand = new RelayCommand<string>(AddMoney);
        RemoveSelectedItemCommand = new RelayCommand(RemoveSelectedItem, () => SelectedItem is not null);
        RestoreConditionCommand = new RelayCommand<string>(SetItemCondition);

        Settings = new SettingsViewModel(
            saveDirectoriesProvider is null ? stored?.SaveDirectories ?? SaveDirectoryDiscovery.GetExistingDirectories() : saveDirectoriesProvider(),
            backupDirectoryProvider is null ? stored?.BackupDirectory ?? GetDefaultBackupDirectory() : backupDirectoryProvider(),
            settingsPath: settingsPath,
            stored: stored);
        Settings.Saved += (_, _) => GameAudioService.Instance.Apply(Settings.SoundEnabled, Settings.SoundVolume, Settings.MusicEnabled);
        GameAudioService.Instance.Apply(Settings.SoundEnabled, Settings.SoundVolume, InteractiveApp && Settings.MusicEnabled);

        Cloud = new CloudViewModel(
            backupDirectoryProvider: _backupDirectoryProvider,
            localSaveFilesProvider: () => Saves.Select(s => s.FilePath).ToArray(),
            onSaveDownloaded: path => AddPreviewSave(path));
        DismissWizardCommand = new RelayCommand(DismissFirstRunWizard);
        WizardAutoDetectCommand = new RelayCommand(async () => await WizardAutoDetectAsync());
        WizardAddDirectoryCommand = new RelayCommand(async () => await WizardAddDirectoryAsync(), () => !string.IsNullOrWhiteSpace(WizardDirectoryInput));

        // Loading selects a save, which updates the comparison: it must exist first.
        Compare = new CompareViewModel(releaseId => TryCatalog(releaseId, out var bundle) ? bundle : null);
        Timeline = new SaveTimelineViewModel(CompareTimelinePair);

        Diagnostics = new DiagnosticsViewModel(
            pendingCrash: InteractiveApp ? CrashReporter.Pending() : null,
            upload: InteractiveApp ? bundle => DiagnosticsUploader.SendAsync(bundle) : null,
            onSent: Settings.MarkReportSent,
            lastSent: () => Settings.LastReportUtc);
        AcknowledgeReportsCommand = new RelayCommand(() => AnswerReportsNotice(true));
        DisableReportsCommand = new RelayCommand(() => AnswerReportsNotice(false));
        SendReportIfDue();

        Encyclopedia = new EncyclopediaViewModel(
            GameContentRegistry.GetLoadedContents,
            CanAddEncyclopediaItem,
            AddEncyclopediaItem,
            Companion.CanSpawnForGame,
            Companion.GiveItemAsync);
        ToolkitEnvironment = new ToolkitEnvironmentViewModel(() => (GameFixes.SelectedTarget.Target, GameFixes.GameDirectory));
        GameFixes.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or nameof(GameFixesViewModel.SelectedTarget) or nameof(GameFixesViewModel.GameDirectory) or nameof(GameFixesViewModel.Status))
                ToolkitEnvironment.Refresh();
        };
        Companion.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or nameof(CompanionViewModel.SelectedGame) or nameof(CompanionViewModel.State))
                Encyclopedia.RefreshAvailability();
        };
        Encyclopedia.RefreshAvailability();

        if (discoverLocalSaves && InteractiveApp)
        {
            // Items, names and icons of the installed games (and their mods) first, then the saves read
            // with them — all off the interface thread.
            var contentLanguage = I18nService.Instance.CurrentLanguage;
            BackgroundTask.Run(RefreshInstalledGameContentAsync(contentLanguage), "game content");
        }
        else if (discoverLocalSaves)
        {
            Refresh();
        }

        // Silent background update check; only the real interactive app goes online (not screenshots or tests).
        if (InteractiveApp)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var installs = StalkerSaveEditor.Core.Diagnostics.GameDoctor.DiscoverInstallations();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _installations = installs;
                        OnPropertyChanged(nameof(GameBuildDisplay));
                        FollowSelectedGame();
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn("game install discovery for build check failed", exception);
                }
            });
        }

        // Started on the UI thread: the view model raises CanExecuteChanged, which Avalonia buttons accept only there.
        if (InteractiveApp) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Updates.CheckAsync(silent: true));
        if (InteractiveApp) Avalonia.Threading.Dispatcher.UIThread.Post(StartDiskWatch);
    }

    /// <summary>Set by <c>Program</c> for the interactive app only: network checks and the previous run's crash.</summary>
    public static bool InteractiveApp { get; set; }

    public DiagnosticsViewModel Diagnostics { get; }

    public RelayCommand AcknowledgeReportsCommand { get; }
    public RelayCommand DisableReportsCommand { get; }

    private void AnswerReportsNotice(bool send)
    {
        Settings.AnswerReportsNotice(send);
        SendReportIfDue();
    }

    /// <summary>Daily redacted report (and one after a crash), only after the user has seen the notice.</summary>
    private void SendReportIfDue()
    {
        if (!InteractiveApp || !Settings.SendReports || Settings.ReportsNoticeVisible) return;
        var last = Settings.LastReportUtc;
        var unreportedCrash = CrashReporter.PendingSinceUtc() is { } crashed && (last is null || crashed > last);
        if (!DiagnosticsUploader.IsDue(last, DateTime.UtcNow, unreportedCrash)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Diagnostics.SendAsync(automatic: true));
    }

    public CompareViewModel Compare { get; }

    public SaveTimelineViewModel Timeline { get; }

    public EncyclopediaViewModel Encyclopedia { get; }

    private void UpdateCompareSubject()
    {
        var save = SelectedSave;
        if (save is null)
        {
            Compare.SetSubject(null, string.Empty, []);
            return;
        }

        var family = save.ReleaseId.Replace("-ee", string.Empty, StringComparison.Ordinal);
        var others = Saves
            .Where(other => other.ReleaseId.Replace("-ee", string.Empty, StringComparison.Ordinal) == family)
            .Select(other => new CompareCandidate(other.DisplayName, other.FilePath));
        var backups = Backups
            .Where(backup => backup.SourcePath == save.FilePath && File.Exists(backup.BackupPath))
            .Select(backup => new CompareCandidate(L.T("Бэкап ") + backup.CreatedAt, backup.BackupPath, IsBackup: true));
        Compare.SetSubject(save.FilePath, save.ReleaseId, backups.Concat(others).ToArray(), save.DisplayName);
    }

    public ObservableCollection<SaveFileSummary> Saves { get; } = [];
    public BulkObservableCollection<InventoryLineViewModel> FilteredInventory { get; } = [];
    public ObservableCollection<BackupRecordViewModel> Backups { get; } = [];
    public SettingsViewModel Settings { get; }

    public BackupRecordViewModel? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                OnPropertyChanged(nameof(HasSelectedBackup));
            }
        }
    }

    public bool HasSelectedBackup => SelectedBackup is not null;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DiscardDraftCommand { get; }
    public RelayCommand<string> SelectTabCommand { get; }
    public RelayCommand<string> AddMoneyCommand { get; }
    public RelayCommand RemoveSelectedItemCommand { get; }
    public RelayCommand<string> RestoreConditionCommand { get; }

    public CapabilitiesViewModel Capabilities { get; } = new();
    public CloudViewModel Cloud { get; }
    public AchievementsViewModel Achievements { get; } = new();
    public GameDoctorViewModel GameDoctor { get; } = new();
    public SaveDoctorViewModel SaveDoctor { get; }
    public GameFixesViewModel GameFixes { get; } = new();
    public UpdatesViewModel Updates { get; } = new();
    public ToolkitEnvironmentViewModel ToolkitEnvironment { get; }

    /// <summary>
    /// Companion screen ViewModel — backed by real Core services in normal runs,
    /// Mock only in --screenshot mode (via the no-arg CompanionViewModel() constructor).
    /// </summary>
    public CompanionViewModel Companion { get; } = new CompanionViewModel(
        new CompanionServiceAdapter(ResolveModSourceRoot()));

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                if (InteractiveApp) GameAudioService.Instance.Play(SoundEvent.Tab);
                if (value == "save-doctor" && SelectedSave is { } selectedSave)
                {
                    SaveDoctor.SavePath = selectedSave.FilePath;
                    // Read-only check: run it at once instead of showing "choose a save" next to a chosen one.
                    if (!SaveDoctor.HasReport && SaveDoctor.AnalyzeCommand.CanExecute(null)) SaveDoctor.AnalyzeCommand.Execute(null);
                }
                OnPropertyChanged(nameof(IsOverviewTab));
                OnPropertyChanged(nameof(IsInventoryTab));
                OnPropertyChanged(nameof(IsFactionsTab));
                OnPropertyChanged(nameof(IsStashesTab));
                OnPropertyChanged(nameof(IsTransitionsTab));
                OnPropertyChanged(nameof(IsBackupsTab));
                OnPropertyChanged(nameof(IsCompareTab));
                OnPropertyChanged(nameof(IsSettingsTab));
                OnPropertyChanged(nameof(IsCapabilitiesTab));
                OnPropertyChanged(nameof(IsCompanionTab));
                OnPropertyChanged(nameof(IsCloudTab));
                OnPropertyChanged(nameof(IsAchievementsTab));
                OnPropertyChanged(nameof(IsGameDoctorTab));
                OnPropertyChanged(nameof(IsSaveDoctorTab));
                OnPropertyChanged(nameof(IsGameFixesTab));
                OnPropertyChanged(nameof(IsUpdatesTab));
                OnPropertyChanged(nameof(IsTimelineTab));
                OnPropertyChanged(nameof(IsEncyclopediaTab));
                OnPropertyChanged(nameof(IsToolkitEnvironmentTab));
                OnPropertyChanged(nameof(IsGamesOverviewTab));
                OnPropertyChanged(nameof(IsSaveWorkspace));
                OnPropertyChanged(nameof(CurrentGroupTitle));
                OnPropertyChanged(nameof(CurrentPageTitle));
                OnPropertyChanged(nameof(ShowOverviewScreen));
                OnPropertyChanged(nameof(ShowInventoryScreen));
                OnPropertyChanged(nameof(ShowFactionsScreen));
                OnPropertyChanged(nameof(ShowStashesScreen));
                OnPropertyChanged(nameof(ShowTransitionsScreen));
                OnPropertyChanged(nameof(ShowBackupsScreen));
                OnPropertyChanged(nameof(ShowCompareScreen));
                OnPropertyChanged(nameof(ShowSettingsScreen));
                OnPropertyChanged(nameof(ShowCapabilitiesScreen));
                OnPropertyChanged(nameof(ShowCompanionScreen));
                OnPropertyChanged(nameof(ShowCloudScreen));
                OnPropertyChanged(nameof(ShowAchievementsScreen));
                OnPropertyChanged(nameof(ShowGameDoctorScreen));
                OnPropertyChanged(nameof(ShowSaveDoctorScreen));
                OnPropertyChanged(nameof(ShowGameFixesScreen));
                OnPropertyChanged(nameof(ShowUpdatesScreen));
                OnPropertyChanged(nameof(ShowTimelineScreen));
                OnPropertyChanged(nameof(ShowEncyclopediaScreen));
                OnPropertyChanged(nameof(ShowToolkitEnvironmentScreen));
                OnPropertyChanged(nameof(ShowGamesOverviewScreen));
                OnPropertyChanged(nameof(IsFirstRunWizardVisible));
                OnPropertyChanged(nameof(ShouldShowEmptyState));
            }
        }
    }

    private bool _isFirstRunWizardDismissed;
    private string _wizardDirectoryInput = string.Empty;

    public string WizardDirectoryInput
    {
        get => _wizardDirectoryInput;
        set
        {
            if (SetProperty(ref _wizardDirectoryInput, value))
            {
                WizardAddDirectoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsFirstRunWizardVisible => !_isFirstRunWizardDismissed && !_isLoadingLibrary && Saves.Count == 0 && !IsSettingsTab && !IsCapabilitiesTab && !IsCompanionTab && !IsCloudTab && !IsAchievementsTab && !IsGameDoctorTab && !IsSaveDoctorTab && !IsGameFixesTab && !IsUpdatesTab && !IsTimelineTab && !IsEncyclopediaTab && !IsToolkitEnvironmentTab && !IsGamesOverviewTab && !IsCompareTab;

    public RelayCommand DismissWizardCommand { get; }
    public RelayCommand WizardAutoDetectCommand { get; }
    public RelayCommand WizardAddDirectoryCommand { get; }

    public void DismissFirstRunWizard()
    {
        _isFirstRunWizardDismissed = true;
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
    }

    public async Task WizardAutoDetectAsync()
    {
        Settings.AutoDetectSaveDirectories();
        await RefreshAsync();
        if (Saves.Count > 0) DismissFirstRunWizard();
    }

    public async Task WizardAddDirectoryAsync()
    {
        if (!Settings.AddSaveDirectory(WizardDirectoryInput)) return;
        WizardDirectoryInput = string.Empty;
        await RefreshAsync();
        if (Saves.Count > 0) DismissFirstRunWizard();
    }

    public bool IsOverviewTab => SelectedTab == "overview";
    public bool IsInventoryTab => SelectedTab == "inventory";
    public bool IsFactionsTab => SelectedTab == "factions";
    public bool IsStashesTab => SelectedTab == "stashes";
    public bool IsTransitionsTab => SelectedTab == "transitions";
    public bool IsBackupsTab => SelectedTab == "backups";
    public bool IsCompareTab => SelectedTab == "compare";
    public bool IsSettingsTab => SelectedTab == "settings";
    public bool IsCapabilitiesTab => SelectedTab == "capabilities";
    public bool IsCompanionTab => SelectedTab == "companion";
    public bool IsCloudTab => SelectedTab == "cloud";
    public bool IsAchievementsTab => SelectedTab == "achievements";
    public bool IsGameDoctorTab => SelectedTab == "game-doctor";
    public bool IsSaveDoctorTab => SelectedTab == "save-doctor";
    public bool IsGameFixesTab => SelectedTab == "game-fixes";
    public bool IsUpdatesTab => SelectedTab == "updates";
    public bool IsTimelineTab => SelectedTab == "timeline";
    public bool IsEncyclopediaTab => SelectedTab == "encyclopedia";
    public bool IsToolkitEnvironmentTab => SelectedTab == "toolkit-environment";
    public bool IsGamesOverviewTab => SelectedTab == "games";

    public bool IsSaveWorkspace => SelectedTab is "overview" or "inventory" or "factions" or "stashes" or "transitions" or "backups" or "compare" or "timeline" or "save-doctor";

    public string CurrentGroupTitle => SelectedTab switch
    {
        "games" or "game-fixes" or "game-doctor" or "toolkit-environment" or "companion" or "achievements" => L.T("ИГРЫ"),
        "encyclopedia" or "capabilities" or "updates" or "settings" or "cloud" => L.T("ИНСТРУМЕНТЫ"),
        _ => L.T("СОХРАНЕНИЯ"),
    };

    public string CurrentPageTitle => SelectedTab switch
    {
        "inventory" => L.T("ИНВЕНТАРЬ"),
        "factions" => L.T("ФРАКЦИИ"),
        "stashes" => L.T("ТАЙНИКИ"),
        "transitions" => L.T("ПЕРЕХОДЫ"),
        "backups" => L.T("БЭКАПЫ"),
        "compare" => L.T("СРАВНЕНИЕ"),
        "timeline" => L.T("ИСТОРИЯ СОХРАНЕНИЙ"),
        "save-doctor" => L.T("ДОКТОР СОХРАНЕНИЯ"),
        "game-fixes" => L.T("ИСПРАВЛЕНИЯ ИГРЫ"),
        "games" => L.T("ИГРЫ И ИНСТРУМЕНТЫ"),
        "game-doctor" => L.T("ДОКТОР ИГРЫ"),
        "toolkit-environment" => L.T("СРЕДА ИГРЫ"),
        "companion" => L.T("КОМПАНЬОН"),
        "achievements" => L.T("ДОСТИЖЕНИЯ"),
        "cloud" => L.T("ОБЛАКО"),
        "encyclopedia" => L.T("ЭНЦИКЛОПЕДИЯ"),
        "capabilities" => L.T("ВОЗМОЖНОСТИ"),
        "updates" => L.T("ОБНОВЛЕНИЯ"),
        "settings" => L.T("НАСТРОЙКИ"),
        _ => L.T("ОБЗОР"),
    };

    public bool ShowOverviewScreen => HasSelection && IsOverviewTab;
    public bool ShowInventoryScreen => HasSelection && IsInventoryTab;
    public bool ShowFactionsScreen => HasSelection && IsFactionsTab;
    public bool ShowStashesScreen => HasSelection && IsStashesTab;
    public bool ShowTransitionsScreen => HasSelection && IsTransitionsTab;
    public bool ShowBackupsScreen => HasSelection && IsBackupsTab;
    public bool ShowCompareScreen => IsCompareTab;
    public bool ShowSettingsScreen => IsSettingsTab;
    public bool ShowCapabilitiesScreen => IsCapabilitiesTab;
    public bool ShowCompanionScreen => IsCompanionTab;
    public bool ShowCloudScreen => IsCloudTab;
    public bool ShouldShowEmptyState => HasNoSelection && !IsSettingsTab && !IsCapabilitiesTab && !IsCompanionTab && !IsCloudTab && !IsAchievementsTab && !IsGameDoctorTab && !IsSaveDoctorTab && !IsGameFixesTab && !IsUpdatesTab && !IsTimelineTab && !IsEncyclopediaTab && !IsToolkitEnvironmentTab && !IsGamesOverviewTab && !IsCompareTab && !IsFirstRunWizardVisible;
    public bool ShowAchievementsScreen => IsAchievementsTab;
    public bool ShowGameDoctorScreen => IsGameDoctorTab;
    public bool ShowSaveDoctorScreen => IsSaveDoctorTab;
    public bool ShowGameFixesScreen => IsGameFixesTab;
    public bool ShowUpdatesScreen => IsUpdatesTab;
    public bool ShowTimelineScreen => IsTimelineTab;
    public bool ShowEncyclopediaScreen => IsEncyclopediaTab;
    public bool ShowToolkitEnvironmentScreen => IsToolkitEnvironmentTab;
    public bool ShowGamesOverviewScreen => IsGamesOverviewTab;

    public SaveFileSummary? SelectedSave
    {
        get => _selectedSave;
        set
        {
            var previous = _selectedSave;
            if (!SetProperty(ref _selectedSave, value)) return;

            if (previous is not null)
            {
                foreach (var item in previous.Inventory) item.PropertyChanged -= OnInventoryItemPropertyChanged;
            }

            Capabilities.SelectedFormatId = value?.ReleaseId;
            if (InteractiveApp && value is not null) GameAudioService.Instance.UseGame(value.ReleaseId);
            FollowSelectedGame();
            SaveDoctor.SavePath = value?.FilePath ?? string.Empty;
            OnPropertyChanged(nameof(GameBuildDisplay));

            if (value is not null)
            {
                _moneyInput = value.Money.ToString(CultureInfo.InvariantCulture);
                foreach (var item in value.Inventory) item.PropertyChanged += OnInventoryItemPropertyChanged;
                LoadDraft(value);
            }
            else
            {
                _currentJournal = null;
            }

            UpdateCompareSubject();

            ApplyInventoryFilter();
            SelectedItem = FilteredInventory.FirstOrDefault();

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            OnPropertyChanged(nameof(SelectedSaveName));
            OnPropertyChanged(nameof(SelectedReleaseName));
            OnPropertyChanged(nameof(SelectedMoneyDisplay));
            OnPropertyChanged(nameof(SelectedInventory));
            OnPropertyChanged(nameof(MoneyInput));
            OnPropertyChanged(nameof(CanEditMoney));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(SaveDisabledReason));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(HasDraftChanges));
            OnPropertyChanged(nameof(DraftStatusText));
            OnPropertyChanged(nameof(ShowOverviewScreen));
            OnPropertyChanged(nameof(ShowInventoryScreen));
            OnPropertyChanged(nameof(ShowFactionsScreen));
            OnPropertyChanged(nameof(ShowStashesScreen));
            OnPropertyChanged(nameof(ShowTransitionsScreen));
            OnPropertyChanged(nameof(ShowBackupsScreen));
            OnPropertyChanged(nameof(ShowSettingsScreen));
            OnPropertyChanged(nameof(ShowTimelineScreen));
            OnPropertyChanged(nameof(ShowEncyclopediaScreen));
            OnPropertyChanged(nameof(ShowCapabilitiesScreen));
            OnPropertyChanged(nameof(ShouldShowEmptyState));
            Encyclopedia.RefreshAvailability();

            SaveCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            DiscardDraftCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedSave is not null;
    public bool HasNoSelection => SelectedSave is null;
    public string SelectedSaveName => SelectedSave?.DisplayName ?? string.Empty;
    public string SelectedReleaseName => SelectedSave?.ReleaseName ?? string.Empty;

    public string MoneyInput
    {
        get => _moneyInput;
        set
        {
            if (SetProperty(ref _moneyInput, value))
            {
                RecordDraftChange();
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveDisabledReason));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanEditMoney => SelectedSave?.CanEditMoney == true;

    public bool CanSave
    {
        get
        {
            if (_isSaving || SelectedSave is null) return false;
            var plan = BuildCurrentEditPlan(SelectedSave);
            if (!EditService.CanEdit(SelectedSave.ReleaseId, plan.EditKinds)) return false;
            return HasDraftChanges && InputsAreValid(SelectedSave);
        }
    }

    public string SaveDisabledReason
    {
        get
        {
            if (SelectedSave is null)
                return L.T("Выберите сохранение для редактирования.");
            var plan = BuildCurrentEditPlan(SelectedSave);
            if (!EditService.CanEdit(SelectedSave.ReleaseId, plan.EditKinds))
            {
                return L.T("Эта правка для формата {0} не поддерживается (см. «Возможности»).", SelectedSave.ReleaseName);
            }
            if (!HasDraftChanges)
                return L.T("Нет несохранённых изменений.");
            if (!InputsAreValid(SelectedSave))
                return L.T("Введены некорректные значения (проверьте введённые числа).");
            return L.T("Сохранить изменения в файл сейва (с созданием резервной копии).");
        }
    }

    public bool CanUndo => _currentJournal?.CanUndo == true;
    public bool CanRedo => _currentJournal?.CanRedo == true;

    public bool HasDraftChanges
    {
        get
        {
            if (SelectedSave is null) return false;
            if (_currentJournal is not null && _currentJournal.Index > 0) return true;
            return HasPendingChanges(SelectedSave);
        }
    }

    public string DraftStatusText
    {
        get
        {
            if (_currentJournal is not null && _currentJournal.Index > 0)
            {
                return L.T("Черновик: {0} действ.", _currentJournal.Index);
            }
            if (SelectedSave is not null && HasPendingChanges(SelectedSave))
            {
                return L.T("Есть несохранённые изменения");
            }
            return L.T("Все изменения сохранены");
        }
    }

    public string SelectedMoneyDisplay => SelectedSave is null
        ? string.Empty
        : $"{SelectedSave.Money:N0} RU";

    public IReadOnlyList<InventoryLineViewModel> SelectedInventory => SelectedSave?.Inventory ?? [];

    public string InventorySearchText
    {
        get => _inventorySearchText;
        set
        {
            if (SetProperty(ref _inventorySearchText, value))
            {
                ApplyInventoryFilter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyInventoryFilter();
            }
        }
    }

    public InventoryLineViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            var previous = _selectedItem;
            if (!SetProperty(ref _selectedItem, value)) return;
            if (previous is not null) previous.IsSelected = false;
            if (value is not null) value.IsSelected = true;
            RemoveSelectedItemCommand.NotifyCanExecuteChanged();
        }
    }

    private Dictionary<string, SaveLibraryLoader.CachedSave> _libraryCache = new(StringComparer.Ordinal);
    private bool _isLoadingLibrary;
    private int _libraryVersion;
    private int _appliedLibraryVersion = -1;

    /// <summary>Re-reads the save folders now (tests, restores): only new or changed files are parsed.</summary>
    public void Refresh() => ApplyLibrary(SaveLibraryLoader.LoadLibrary(_saveDirectoriesProvider().ToArray(), _libraryCache));

    /// <summary>
    /// Re-reads the save folders off the interface thread (the app: hundreds of saves take seconds);
    /// <paramref name="before"/> runs first on the same thread (the installed games' content).
    /// </summary>
    public async Task RefreshAsync(Action? before = null)
    {
        var directories = _saveDirectoriesProvider().ToArray();
        var cache = _libraryCache;
        var version = ++_libraryVersion;
        SetLoadingLibrary(true);
        try
        {
            // A first load shows each finished batch at once (newest saves first); later loads come from the cache.
            var progressive = Saves.Count == 0;
            var loaded = await Task.Run(() =>
            {
                before?.Invoke();
                return SaveLibraryLoader.LoadLibrary(directories, cache, progressive
                    ? batch => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendBatch(batch, version))
                    : null,
                    // A newer refresh supersedes this one: stop parsing instead of finishing work nobody will show.
                    () => version != Volatile.Read(ref _libraryVersion));
            });
            if (version == _libraryVersion)
            {
                _appliedLibraryVersion = version;
                ApplyLibrary(loaded);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            AppLog.Error("save library not loaded", exception);
            StatusMessage = L.T("Не удалось прочитать папки сохранений: {0}", exception.Message);
        }
        finally
        {
            if (version == _libraryVersion) SetLoadingLibrary(false);
        }
    }

    private async Task RefreshInstalledGameContentAsync(string contentLanguage)
    {
        await RefreshAsync(() => GameContentRegistry.LoadInstalled(uiLanguage: contentLanguage));
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Encyclopedia.Refresh);
    }

    /// <summary>Game Doctor and Game Fixes start on the selected save's game and its detected install.</summary>
    private void FollowSelectedGame()
    {
        if (_installations is null || SelectedSave is null ||
            GameBuildFingerprints.TargetForFormat(SelectedSave.ReleaseId) is not { } target)
        {
            return;
        }

        GameDoctor.FollowGame(target, _installations);
        GameFixes.FollowGame(target, _installations);
    }

    private int IndexOfSave(string path)
    {
        for (var index = 0; index < Saves.Count; index++)
        {
            if (string.Equals(Saves[index].FilePath, path, StringComparison.Ordinal)) return index;
        }

        return -1;
    }

    private void SetLoadingLibrary(bool loading)
    {
        _isLoadingLibrary = loading;
        if (loading) StatusMessage = L.T("Чтение сохранений…");
        else if (StatusMessage == L.T("Чтение сохранений…")) StatusMessage = string.Empty;
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
    }

    private void AppendBatch(IReadOnlyList<SaveFileSummary> batch, int version)
    {
        // A batch can arrive after the finished list (the continuation may run first): then it is already shown.
        if (version != _libraryVersion || version == _appliedLibraryVersion) return;
        foreach (var save in batch) Saves.Add(save);
        Timeline.SetSaves(Saves);
        SelectedSave ??= Saves.FirstOrDefault();
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
    }

    /// <summary>Shows a loaded library, keeping the selected save when it is still there.</summary>
    private void ApplyLibrary((List<SaveFileSummary> Saves, Dictionary<string, SaveLibraryLoader.CachedSave> Cache) library)
    {
        var selectedPath = SelectedSave?.FilePath;
        _libraryCache = library.Cache;
        if (!Saves.SequenceEqual(library.Saves))
        {
            Saves.Clear();
            foreach (var save in library.Saves) Saves.Add(save);
        }

        Timeline.SetSaves(Saves);
        SelectedSave = Saves.FirstOrDefault(save => save.FilePath == selectedPath) ?? Saves.FirstOrDefault();
        RefreshBackups();
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
    }

    public void RefreshBackups()
    {
        var selectedJournalPath = SelectedBackup?.JournalPath;
        Backups.Clear();
        var backupDir = _backupDirectoryProvider();
        if (Directory.Exists(backupDir))
        {
            var records = LocalSaveStorage.ListBackups([backupDir]);
            foreach (var r in records)
            {
                Backups.Add(new BackupRecordViewModel(r));
            }
        }

        SelectedBackup = Backups.FirstOrDefault(backup => backup.JournalPath == selectedJournalPath)
            ?? Backups.FirstOrDefault(backup => backup.CanRestore)
            ?? Backups.FirstOrDefault();
        UpdateCompareSubject();
    }

    /// <summary>Remove + insert: Avalonia's virtualizing list throws on a Replace notification for the selected row.</summary>
    private bool _selectedSaveChangedOnDisk;
    private Avalonia.Threading.DispatcherTimer? _diskWatch;

    /// <summary>The selected save's file changed after it was read (the game or another tool saved over it).</summary>
    public bool SelectedSaveChangedOnDisk
    {
        get => _selectedSaveChangedOnDisk;
        private set => SetProperty(ref _selectedSaveChangedOnDisk, value);
    }

    /// <summary>Starts a cheap 3-second size/mtime check of the selected save (interactive app only).</summary>
    public void StartDiskWatch()
    {
        if (_diskWatch is not null) return;
        _diskWatch = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _diskWatch.Tick += (_, _) => CheckSelectedSaveOnDisk();
        _diskWatch.Start();
    }

    public void CheckSelectedSaveOnDisk()
    {
        if (SelectedSave is not { } save || _isSaving)
        {
            SelectedSaveChangedOnDisk = false;
            return;
        }

        try
        {
            var info = new FileInfo(save.FilePath);
            SelectedSaveChangedOnDisk = info.Exists &&
                (info.Length != save.FileSizeBytes || save.LastModified is { } known && info.LastWriteTime != known);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SelectedSaveChangedOnDisk = false;
        }
    }

    /// <summary>Re-reads the changed save; the draft of the old contents is dropped (its source SHA no longer matches).</summary>
    public void ReloadChangedSave()
    {
        if (SelectedSave is not { } save) return;
        if (HasDraftChanges) DiscardDraft();
        OnSaveRepaired(save.FilePath);
        SelectedSaveChangedOnDisk = false;
        StatusMessage = L.T("Сейв перечитан с диска.");
    }

    private void OnSaveRepaired(string path)
    {
        var index = IndexOfSave(path);
        if (index < 0 || SaveLibraryLoader.TryReadSave(path) is not { } refreshed) return;
        var wasSelected = ReferenceEquals(SelectedSave, Saves[index]);
        ReplaceSave(index, refreshed);
        if (wasSelected) SelectedSave = refreshed;
        RefreshBackups();
    }

    private void ReplaceSave(int index, SaveFileSummary save)
    {
        Saves.RemoveAt(index);
        Saves.Insert(index, save);
    }

    public bool AddPreviewSave(string path) => ShowOpenedSave(SaveLibraryLoader.TryReadSave(path));

    /// <summary>Same as <see cref="AddPreviewSave"/>, but parses off the UI thread (a large S2 save takes ~0.7 s).</summary>
    public async Task<bool> AddPreviewSaveAsync(string path) => ShowOpenedSave(await Task.Run(() => SaveLibraryLoader.TryReadSave(path)));

    private bool ShowOpenedSave(SaveFileSummary? parsed)
    {
        if (parsed is null) return false;
        // Opening a file that is already listed (or re-opening it after a change) replaces its entry.
        var index = IndexOfSave(parsed.FilePath);
        if (index >= 0) ReplaceSave(index, parsed);
        else Saves.Add(parsed);
        Timeline.SetSaves(Saves);
        SelectedSave = parsed;
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
        return true;
    }

    private void CompareTimelinePair(SaveFileSummary current, SaveFileSummary previous)
    {
        SelectedSave = current;
        SelectedTab = "compare";
        Compare.Selected = Compare.Candidates.FirstOrDefault(candidate => candidate.Path == previous.FilePath);
    }

    private bool CanAddEncyclopediaItem(string releaseId) =>
        SelectedSave is { CanAddItems: true } save &&
        string.Equals(save.ReleaseId.Replace("-ee", string.Empty, StringComparison.Ordinal), releaseId, StringComparison.Ordinal);

    private void AddEncyclopediaItem(string releaseId, string section)
    {
        if (CanAddEncyclopediaItem(releaseId)) StageItemAddition(section, 1);
    }

    public void Undo()
    {
        if (_currentJournal is { CanUndo: true } journal) MoveInJournal(journal.Undo());
    }

    public void Redo()
    {
        if (_currentJournal is { CanRedo: true } journal) MoveInJournal(journal.Redo());
    }

    private void MoveInJournal(DraftJournal journal)
    {
        _currentJournal = journal;
        _draftStore.Save(journal);
        ApplyPlanToUI(journal.Current);
        UpdateDraftState();
    }

    public void DiscardDraft()
    {
        if (SelectedSave is null) return;
        _draftStore.Remove(SelectedSave.SourceSha256);
        _currentJournal = new DraftJournal([new EditPlan(SelectedSave.SourceSha256)], 0);
        ApplyPlanToUI(_currentJournal.Current);
        UpdateDraftState();
        StatusMessage = L.T("Черновик сброшен.");
    }

    public void AddMoney(string? amountText)
    {
        if (int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta))
        {
            if (uint.TryParse(MoneyInput, NumberStyles.None, CultureInfo.InvariantCulture, out var current))
            {
                var next = Math.Clamp((long)current + delta, 0, 2_000_000_000);
                MoneyInput = next.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public void SetItemCondition(string? percentText)
    {
        if (SelectedItem is { CanEditCondition: true } &&
            int.TryParse(percentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            SelectedItem.ConditionPercent = percent;
            RecordDraftChange();
        }
    }

    public void RemoveSelectedItem()
    {
        if (SelectedItem is not null)
        {
            SelectedItem.IsDeleted = true;
            RecordDraftChange();
            ApplyInventoryFilter();
            SelectedItem = FilteredInventory.FirstOrDefault();
        }
    }

    private RelocationAnchorViewModel? _selectedRelocationAnchor;

    public RelocationAnchorViewModel? SelectedRelocationAnchor
    {
        get => _selectedRelocationAnchor;
        set => SetProperty(ref _selectedRelocationAnchor, value);
    }

    /// <summary>
    /// TP (experimental): moves the actor to a level-changer destination of this save and writes the save at once,
    /// through the journaled backup and a read-back of the actor location. Refused while the draft has changes.
    /// </summary>
    public void RelocateActor()
    {
        if (SelectedSave is not { } save || SelectedRelocationAnchor is not { } target) return;
        if (CanSave)
        {
            StatusMessage = L.T("Сначала сохраните или отмените текущие правки, затем переносите персонажа.");
            return;
        }

        try
        {
            var prepared = XRayRelocation.Prepare(File.ReadAllBytes(save.FilePath), target.Anchor);
            var receipt = LocalSaveReplacement.ReplaceLocal(save.FilePath, prepared, _backupDirectoryProvider(), readBack =>
            {
                var location = XRayRelocation.ReadActorLocation(XRayTrilogyReader.FromBytes(readBack.Span));
                if (location.Position != target.Anchor.Position || location.GameVertexId != target.Anchor.GameVertexId)
                    throw new InvalidDataException("The written save does not place the actor at the destination.");
            });
            OnSaveRepaired(save.FilePath);
            StatusMessage = L.T("Персонаж перенесён: {0}. Backup: {1}", target.Display, Path.GetFileName(receipt.BackupPath));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XRayFormatException or UnauthorizedAccessException)
        {
            StatusMessage = L.T("Не удалось перенести персонажа: {0}", exception.Message);
        }
    }

    private IReadOnlyList<GameDoctorInstallation>? _installations;

    /// <summary>RL-6: the installed game's Steam build for the selected save and whether the editor was checked on it.</summary>
    public string GameBuildDisplay
    {
        get
        {
            if (_installations is null || SelectedSave is null ||
                GameBuildFingerprints.TargetForFormat(SelectedSave.ReleaseId) is not { } target)
            {
                return "—";
            }

            var build = GameBuildFingerprints.Detect(target, _installations);
            return build.Status switch
            {
                GameBuildStatus.Verified => L.T("{0} · проверенная", build.BuildId!),
                GameBuildStatus.Unknown => L.T("{0} · не проверялась: сверьте результат в игре", build.BuildId!),
                _ => L.T("игра не найдена"),
            };
        }
    }

    public void TakeStashItem(StashItemViewModel item)
    {
        item.IsTaken = !item.IsTaken;
        RecordDraftChange();
    }

    public void AdjustFactionRelation(FactionRelationViewModel relation, int delta) =>
        SetFactionRelation(relation, relation.Goodwill + delta);

    /// <summary>Sets a faction's goodwill within the game's range and records it in the draft.</summary>
    public void SetFactionRelation(FactionRelationViewModel relation, int goodwill)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var releaseId = SelectedSave?.ReleaseId ?? "stalker-cop";
        var catalog = TryCatalog(releaseId, out var bundle) ? bundle.Factions : null;
        relation.Goodwill = Math.Clamp(goodwill, catalog?.GoodwillMin ?? -3000, catalog?.GoodwillMax ?? 1000);
        RecordDraftChange();
    }

    public AddItemViewModel CreateAddItemDialog()
    {
        var releaseId = SelectedSave?.ReleaseId ?? "stalker-cop";
        var catalog = TryCatalog(releaseId, out var bundle) ? bundle.Items : null;
        return new AddItemViewModel(catalog, OfficialNames, releaseId, NamesLanguage);
    }

    /// <summary>Queues a new item; <paramref name="destination"/> is "inventory" or "stash:&lt;box id&gt;".</summary>
    public void StageItemAddition(string sectionKey, uint quantity, string destination = "inventory")
    {
        if (SelectedSave is null) return;
        var plan = BuildCurrentEditPlan(SelectedSave);
        var currentAdds = plan.Adds.ToList();
        currentAdds.Add(new ItemAddRequest(sectionKey, quantity, destination));
        RecordStashPlan(plan, currentAdds, plan.StashPuts);
        StatusMessage = destination == "inventory"
            ? L.T("Предмет {0} ({1} шт.) добавлен в очередь на запись.", sectionKey, quantity)
            : L.T("Предмет {0} ({1} шт.) будет создан в тайнике.", sectionKey, quantity);
        RefreshStashQueues();
    }

    /// <summary>Queues (or un-queues) moving an inventory item into a stash box.</summary>
    public void ToggleStashPut(InventoryLineViewModel item, StashViewModel stash)
    {
        if (SelectedSave is null || item.Handle > ushort.MaxValue) return;
        var plan = BuildCurrentEditPlan(SelectedSave);
        var handle = (ushort)item.Handle;
        var puts = plan.StashPuts.Where(put => put.ObjectId != handle).ToList();
        var removed = puts.Count != plan.StashPuts.Count;
        if (!removed) puts.Add(new StashPutRequest(handle, stash.Handle));
        RecordStashPlan(plan, plan.Adds.ToList(), puts);
        StatusMessage = removed
            ? L.T("Перенос {0} в тайник отменён.", item.Name)
            : L.T("{0} будет перенесён в тайник «{1}».", item.Name, stash.Name);
        RefreshStashQueues();
    }

    /// <summary>Fills each stash's "will be put / created here" list from the pending plan.</summary>
    public void RefreshStashQueues()
    {
        if (SelectedSave is not { } save) return;
        var journal = _currentJournal?.Current;
        foreach (var stash in save.Stashes)
        {
            stash.Pending.Clear();
            foreach (var put in journal?.StashPuts.Where(put => put.BoxId == stash.Handle) ?? [])
            {
                var name = save.Inventory.FirstOrDefault(line => line.Handle == put.ObjectId)?.Name ?? $"0x{put.ObjectId:X4}";
                stash.Pending.Add(L.T("← из рюкзака: {0}", name));
            }
            foreach (var add in journal?.Adds.Where(add => add.Destination == "stash:" + stash.Handle.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? [])
            {
                stash.Pending.Add(L.T("+ новый: {0} × {1}", add.ItemKey, add.Quantity));
            }
        }
    }

    private void RecordStashPlan(EditPlan plan, IReadOnlyList<ItemAddRequest> adds, IReadOnlyList<StashPutRequest> puts)
    {
        var nextPlan = new EditPlan(
            SelectedSave!.SourceSha256,
            plan.Money,
            plan.StackCounts,
            plan.DetachHandles,
            adds,
            plan.StashTakes,
            puts,
            plan.Upgrades,
            plan.PlayerFaction,
            plan.FactionRelations,
            plan.Stalker2StashTakeHandle,
            plan.Durability,
            plan.Placements);

        RecordPlan(nextPlan);
    }

    public void RestoreBackup(BackupRecordViewModel backup, bool inPlace)
    {
        try
        {
            if (inPlace)
            {
                var receipt = LocalSaveStorage.RestoreInPlace(backup.JournalPath);
                StatusMessage = L.T("Восстановлено на место. Создан страховочный бэкап: {0}", Path.GetFileName(receipt.SafetyBackupPath));
            }
            else
            {
                var targetPath = Path.Combine(Path.GetDirectoryName(backup.SourcePath)!, $"{Path.GetFileNameWithoutExtension(backup.SourceName)}_restored_{DateTime.Now:yyyyMMdd_HHmmss}.sav");
                var receipt = LocalSaveStorage.RestoreBackup(backup.JournalPath, targetPath);
                StatusMessage = L.T("Восстановлено в файл: {0}", Path.GetFileName(receipt.OutputPath));
            }
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("restore failed", ex);
            StatusMessage = L.T("Ошибка восстановления: {0}", ex.Message);
        }
    }

    /// <summary>Web host: the written save goes back to the user as a download.</summary>
    private async Task ExportSavedAsync(Func<string, Task> export, string path)
    {
        try
        {
            await export(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StatusMessage = L.T("Сохранено, но скачивание не началось: ") + exception.Message;
        }
    }

    public void SaveSelected()
    {
        var selected = SelectedSave;
        if (!CanSave || selected is null) return;

        var plan = BuildCurrentEditPlan(selected);
        _isSaving = true;
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();

        try
        {
            var source = File.ReadAllBytes(selected.FilePath);
            var catalog = TryCatalog(selected.ReleaseId, out var bundle) ? bundle : null;
            var prepared = EditService.PrepareEdit(source, plan, selected.ReleaseId, catalog);

            var receipt = LocalSaveReplacement.ReplaceLocal(
                selected.FilePath,
                prepared,
                _backupDirectoryProvider(),
                readBack => EditService.VerifyReadBack(readBack.Span, selected.ReleaseId, plan));

            _draftStore.Remove(selected.SourceSha256);

            var refreshed = SaveLibraryLoader.TryReadSave(selected.FilePath);
            if (refreshed is null || !string.Equals(refreshed.SourceSha256, receipt.OutputSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved file could not be reopened after write verification.");
            }

            var index = Saves.IndexOf(selected);
            if (index >= 0) ReplaceSave(index, refreshed);
            else Saves.Add(refreshed);

            SelectedSave = refreshed;
            RefreshBackups();
            StatusMessage = L.T("Сохранено успешно. Backup: {0}", Path.GetFileName(receipt.BackupPath));
            GameAudioService.Instance.Play(SoundEvent.Save);
            if (HostPlatform.ExportFile is { } export) _ = ExportSavedAsync(export, refreshed.FilePath);
        }
        catch (Exception exception)
        {
            StatusMessage = L.T("Не удалось сохранить: {0}", exception.Message);
            AppLog.Error("save failed", exception);
            GameAudioService.Instance.Play(SoundEvent.Error);
        }
        finally
        {
            _isSaving = false;
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private EditPlan BuildCurrentEditPlan(SaveFileSummary save)
    {
        var money = save.CanEditMoney &&
            uint.TryParse(MoneyInput, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMoney) &&
            parsedMoney != save.Money
                ? parsedMoney
                : (uint?)null;

        var stackCounts = new Dictionary<uint, uint>();
        var detaches = new List<ushort>();
        var durability = new Dictionary<uint, double>();
        var placements = new List<XRayPlacementChange>();
        var upgrades = new Dictionary<ushort, IReadOnlyList<string>>();

        foreach (var item in save.Inventory)
        {
            if (item.IsDeleted)
            {
                detaches.Add((ushort)item.Handle);
                continue;
            }

            if (item.CanEditCount &&
                uint.TryParse(item.CountInput, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
                item.OriginalCount != count)
            {
                stackCounts.Add(item.Handle, count);
            }

            if (item.CanEditCondition && item.OriginalCondition.HasValue)
            {
                var curCond = item.ConditionFraction;
                if (Math.Abs(curCond - item.OriginalCondition.Value) > 0.005f)
                {
                    durability.Add(item.Handle, curCond);
                }
            }

            if (item.CanEditPlacement && !string.Equals(item.Placement, item.OriginalPlacement, StringComparison.Ordinal))
            {
                placements.Add(new XRayPlacementChange(item.Handle, item.Placement, item.Placement == "slot" ? (item.BaseSlot ?? 1) : null));
            }

            if (item.UpgradesChanged)
            {
                upgrades.Add((ushort)item.Handle, item.UpgradesToWrite());
            }
        }

        var stashTakes = new List<ushort>();
        foreach (var stash in save.Stashes)
        {
            foreach (var item in stash.Items.Where(i => i.IsTaken))
            {
                stashTakes.Add(item.Handle);
            }
        }

        var factionRelations = new Dictionary<string, int>();
        foreach (var rel in save.FactionRelations)
        {
            if (rel.Goodwill != rel.OriginalGoodwill)
            {
                factionRelations.Add(rel.Community, rel.Goodwill);
            }
        }

        return new EditPlan(
            save.SourceSha256,
            money: money,
            stackCounts: stackCounts.Count > 0 ? stackCounts : null,
            detachHandles: detaches.Count > 0 ? detaches : null,
            adds: _currentJournal?.Current.Adds is { Count: > 0 } addsList ? addsList : null,
            stashTakes: stashTakes.Count > 0 ? stashTakes : null,
            stashPuts: _currentJournal?.Current.StashPuts is { Count: > 0 } putsList ? putsList : null,
            upgrades: upgrades.Count > 0 ? upgrades : null,
            playerFaction: null,
            factionRelations: factionRelations.Count > 0 ? factionRelations : null,
            durability: durability.Count > 0 ? durability : null,
            placements: placements.Count > 0 ? placements : null);
    }

    private void LoadDraft(SaveFileSummary save)
    {
        var existing = _draftStore.Load(save.SourceSha256);
        if (existing is not null)
        {
            _currentJournal = existing;
            ApplyPlanToUI(existing.Current);
        }
        else
        {
            _currentJournal = new DraftJournal([new EditPlan(save.SourceSha256)], 0);
        }
        UpdateDraftState();
    }

    private void RecordDraftChange()
    {
        if (SelectedSave is null) return;
        var nextPlan = BuildCurrentEditPlan(SelectedSave);
        RecordPlan(nextPlan);
    }

    private void RecordPlan(EditPlan plan)
    {
        if (SelectedSave is null) return;
        var journal = _currentJournal ?? new DraftJournal([new EditPlan(SelectedSave.SourceSha256)], 0);
        if (journal.Current.HasSameEdits(plan)) return;
        _currentJournal = journal.Record(plan);
        _draftStore.Save(_currentJournal);
        UpdateDraftState();
    }

    private void ApplyPlanToUI(EditPlan plan)
    {
        if (SelectedSave is null) return;
        _applyingPlan = true;
        try
        {
            ApplyPlanToRows(SelectedSave, plan);
        }
        finally
        {
            _applyingPlan = false;
        }

        // Undoing a removal brings the row back into the list.
        var selected = SelectedItem;
        ApplyInventoryFilter();
        SelectedItem = selected is not null && FilteredInventory.Contains(selected) ? selected : FilteredInventory.FirstOrDefault();
    }

    private void ApplyPlanToRows(SaveFileSummary save, EditPlan plan)
    {
        _moneyInput = (plan.Money ?? save.Money).ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(MoneyInput));

        foreach (var item in save.Inventory)
        {
            item.CountInput = plan.StackCounts.TryGetValue(item.Handle, out var count)
                ? count.ToString(CultureInfo.InvariantCulture)
                : item.OriginalCount?.ToString(CultureInfo.InvariantCulture) ?? "1";
            item.ConditionPercent = plan.Durability.TryGetValue(item.Handle, out var condition)
                ? (int)Math.Round(condition * 100)
                : item.OriginalCondition is { } original ? (int)Math.Round(original * 100) : 100;
            item.IsDeleted = plan.DetachHandles.Contains((ushort)item.Handle);
            item.Placement = plan.Placements.FirstOrDefault(change => change.Handle == item.Handle)?.Type ?? item.OriginalPlacement;
            var installed = plan.Upgrades.TryGetValue((ushort)item.Handle, out var upgrades) ? upgrades : item.OriginalUpgrades;
            foreach (var upgrade in item.UpgradeItems) upgrade.IsInstalled = installed.Contains(upgrade.Key, StringComparer.Ordinal);
        }

        foreach (var stashItem in save.Stashes.SelectMany(stash => stash.Items))
        {
            stashItem.IsTaken = plan.StashTakes.Contains(stashItem.Handle);
        }

        RefreshStashQueues();

        foreach (var relation in save.FactionRelations)
        {
            relation.Goodwill = plan.FactionRelations.TryGetValue(relation.Community, out var goodwill) ? goodwill : relation.OriginalGoodwill;
        }
    }

    private void UpdateDraftState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SaveDisabledReason));
        OnPropertyChanged(nameof(HasDraftChanges));
        OnPropertyChanged(nameof(DraftStatusText));

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DiscardDraftCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ApplyInventoryFilter()
    {
        if (SelectedSave is null)
        {
            FilteredInventory.ReplaceAll([]);
            return;
        }

        var query = _inventorySearchText.Trim();
        FilteredInventory.ReplaceAll(SelectedSave.Inventory.Where(item =>
            !item.IsDeleted &&
            (_selectedCategory == "all" || string.Equals(item.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(query) ||
             item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             item.TypeKey.Contains(query, StringComparison.CurrentCultureIgnoreCase))));
    }

    private void OnInventoryItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_applyingPlan || e.PropertyName is null || !EditableItemProperties.Contains(e.PropertyName)) return;
        RecordDraftChange();
    }

    /// <summary>The rows differ from the save (an unparsable input counts as a change the user has to fix).</summary>
    private bool HasPendingChanges(SaveFileSummary save) =>
        BuildCurrentEditPlan(save).EditKinds != EditKind.None || !InputsAreValid(save);

    private bool InputsAreValid(SaveFileSummary save)
    {
        if (!uint.TryParse(MoneyInput, NumberStyles.None, CultureInfo.InvariantCulture, out var money) ||
            money > 2_000_000_000)
        {
            return false;
        }

        return save.Inventory.Where(item => item.CanEditCount && !item.IsDeleted).All(item =>
            uint.TryParse(item.CountInput, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
            count is > 0 and <= ushort.MaxValue);
    }

    // One data folder for the app, the CLI and the cloud screen (STALKER_SAVE_EDITOR_DATA overrides it).
    private static string GetDefaultBackupDirectory() => AppPaths.Backups;

    /// <summary>
    /// Resolves the <c>mods/companion</c> directory for <see cref="CompanionServiceAdapter"/>.
    /// Looks next to the executable first (packaged build), then walks up the source tree.
    /// </summary>
    private static string ResolveModSourceRoot()
    {
        return CompanionAssetLocator.ResolveSourceRoot();
    }

}
