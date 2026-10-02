using StalkerSaveEditor.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed partial class SaveLibraryViewModel : ObservableViewModel, IDisposable
{
    // Catalogue and name lookups live in SaveNaming (the loader uses them too); these forward for existing callers.
    internal static bool TryCatalog(string releaseId, out CatalogBundle bundle) => SaveNaming.TryCatalog(releaseId, out bundle);

    internal static OfficialNamesCatalog OfficialNames => SaveNaming.OfficialNames;

    internal static string NamesLanguage => SaveNaming.NamesLanguage;

    private readonly Func<IReadOnlyList<string>> _saveDirectoriesProvider;
    private readonly Func<string> _backupDirectoryProvider;
    private readonly DraftStore _draftStore;
    private readonly DraftSession _draft;

    private SaveFileSummary? _selectedSave;
    private BackupRecordViewModel? _selectedBackup;

    private string _selectedTab = AppTabs.Overview;
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
            SelectedTab = AppTabs.GameFixes;
        };
        _draftStore = new DraftStore(draftsDirectory);
        _draft = new DraftSession(_draftStore);
        if (!Directory.Exists(_draftStore.DirectoryPath))
        {
            Directory.CreateDirectory(_draftStore.DirectoryPath);
        }

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        SaveCommand = new RelayCommand(SaveSelected, () => CanSave);
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        DiscardDraftCommand = new RelayCommand(DiscardDraft, () => HasDraftChanges);

        SelectTabCommand = new RelayCommand<string>(tab => SelectedTab = AppTabs.Normalize(tab));
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
            localSaveFilesProvider: () => Saves.Select(s => new LocalSaveReference(s.FilePath, s.ReleaseId)).ToArray(),
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
            // An id that names no screen (a caller's typo) shows the overview instead of an empty window.
            value = AppTabs.Normalize(value);
            if (SetProperty(ref _selectedTab, value))
            {
                if (InteractiveApp) GameAudioService.Instance.Play(SoundEvent.Tab);
                if (value == AppTabs.SaveDoctor && SelectedSave is { } selectedSave)
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
                _draft.Close();
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
            if (_isSaving || SelectedSave is null || _draft.HasUnsupportedEdits) return false;
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
            if (_draft.HasUnsupportedEdits)
                return L.T("В черновике есть правки из другой версии редактора, которые эта версия не понимает. Сбросьте черновик, чтобы продолжить (он сохранится рядом).");
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

    public bool CanUndo => _draft.CanUndo;
    public bool CanRedo => _draft.CanRedo;

    public bool HasDraftChanges
    {
        get
        {
            if (SelectedSave is null) return false;
            if (_draft.Steps > 0 || _draft.HasUnsupportedEdits) return true;
            return HasPendingChanges(SelectedSave);
        }
    }

    public string DraftStatusText
    {
        get
        {
            if (_draft.Steps > 0)
            {
                return L.T("Черновик: {0} действ.", _draft.Steps);
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
