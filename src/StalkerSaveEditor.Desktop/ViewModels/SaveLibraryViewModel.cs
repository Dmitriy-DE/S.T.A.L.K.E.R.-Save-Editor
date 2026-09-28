using StalkerSaveEditor.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class SaveLibraryViewModel : ObservableViewModel
{
    private static readonly IReadOnlyDictionary<string, CatalogBundle> Catalogs = CatalogBundleReader.LoadEmbedded();
    private static bool TryCatalog(string releaseId, out CatalogBundle bundle) =>
        GameContentRegistry.TryGetCatalog(releaseId, out bundle) || Catalogs.TryGetValue(releaseId, out bundle!);

    private static readonly OfficialNamesCatalog OfficialNames = OfficialNamesCatalog.LoadEmbedded();

    private readonly Func<IReadOnlyList<string>> _saveDirectoriesProvider;
    private readonly Func<string> _backupDirectoryProvider;
    private readonly DraftStore _draftStore;

    private SaveFileSummary? _selectedSave;
    private DraftJournal? _currentJournal;

    private string _selectedTab = "overview";
    private string _moneyInput = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isSaving;

    private string _inventorySearchText = string.Empty;
    private string _selectedCategory = "all";
    private InventoryLineViewModel? _selectedItem;

    public SaveLibraryViewModel(
        bool discoverLocalSaves = true,
        Func<IReadOnlyList<string>>? saveDirectoriesProvider = null,
        Func<string>? backupDirectoryProvider = null,
        string? draftsDirectory = null)
    {
        // The interactive app keeps preferences in settings.json; tests and screenshots never touch it.
        var settingsPath = InteractiveApp ? AppSettings.DefaultPath : null;
        var stored = settingsPath is null ? null : AppSettings.Load(settingsPath);
        _saveDirectoriesProvider = saveDirectoriesProvider ?? (() => Settings?.SaveDirectories.ToArray() ?? SaveDirectoryDiscovery.GetExistingDirectories());
        _backupDirectoryProvider = backupDirectoryProvider ?? (() => Settings is { BackupDirectory.Length: > 0 } settings ? settings.BackupDirectory : GetDefaultBackupDirectory());
        _draftStore = new DraftStore(draftsDirectory);
        if (!Directory.Exists(_draftStore.DirectoryPath))
        {
            Directory.CreateDirectory(_draftStore.DirectoryPath);
        }

        RefreshCommand = new RelayCommand(Refresh);
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
        WizardAutoDetectCommand = new RelayCommand(WizardAutoDetect);
        WizardAddDirectoryCommand = new RelayCommand(WizardAddDirectory, () => !string.IsNullOrWhiteSpace(WizardDirectoryInput));

        if (discoverLocalSaves) Refresh();

        Diagnostics = new DiagnosticsViewModel(pendingCrash: InteractiveApp ? CrashReporter.Pending() : null);

        if (InteractiveApp)
        {
            // Items, names and icons of the installed games (and their mods) arrive in the background.
            GameContentRegistry.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ReloadSelectedSave);
            _ = Task.Run(() => GameContentRegistry.LoadInstalled());
        }

        // Silent background update check; only the real interactive app goes online (not screenshots or tests).
        if (InteractiveApp) _ = Task.Run(() => Updates.CheckAsync(silent: true));
    }

    /// <summary>Set by <c>Program</c> for the interactive app only: network checks and the previous run's crash.</summary>
    public static bool InteractiveApp { get; set; }

    public DiagnosticsViewModel Diagnostics { get; }

    public ObservableCollection<SaveFileSummary> Saves { get; } = [];
    public ObservableCollection<InventoryLineViewModel> FilteredInventory { get; } = [];
    public ObservableCollection<BackupRecordViewModel> Backups { get; } = [];
    public SettingsViewModel Settings { get; }

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
    public UpdatesViewModel Updates { get; } = new();

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
                OnPropertyChanged(nameof(IsOverviewTab));
                OnPropertyChanged(nameof(IsInventoryTab));
                OnPropertyChanged(nameof(IsFactionsTab));
                OnPropertyChanged(nameof(IsStashesTab));
                OnPropertyChanged(nameof(IsTransitionsTab));
                OnPropertyChanged(nameof(IsBackupsTab));
                OnPropertyChanged(nameof(IsSettingsTab));
                OnPropertyChanged(nameof(IsCapabilitiesTab));
                OnPropertyChanged(nameof(IsCompanionTab));
                OnPropertyChanged(nameof(IsCloudTab));
                OnPropertyChanged(nameof(IsAchievementsTab));
                OnPropertyChanged(nameof(IsUpdatesTab));
                OnPropertyChanged(nameof(ShowOverviewScreen));
                OnPropertyChanged(nameof(ShowInventoryScreen));
                OnPropertyChanged(nameof(ShowFactionsScreen));
                OnPropertyChanged(nameof(ShowStashesScreen));
                OnPropertyChanged(nameof(ShowTransitionsScreen));
                OnPropertyChanged(nameof(ShowBackupsScreen));
                OnPropertyChanged(nameof(ShowSettingsScreen));
                OnPropertyChanged(nameof(ShowCapabilitiesScreen));
                OnPropertyChanged(nameof(ShowCompanionScreen));
                OnPropertyChanged(nameof(ShowCloudScreen));
                OnPropertyChanged(nameof(ShowAchievementsScreen));
                OnPropertyChanged(nameof(ShowUpdatesScreen));
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

    public bool IsFirstRunWizardVisible => !_isFirstRunWizardDismissed && Saves.Count == 0 && !IsSettingsTab && !IsCapabilitiesTab && !IsCompanionTab && !IsCloudTab && !IsAchievementsTab && !IsUpdatesTab;

    public RelayCommand DismissWizardCommand { get; }
    public RelayCommand WizardAutoDetectCommand { get; }
    public RelayCommand WizardAddDirectoryCommand { get; }

    public void DismissFirstRunWizard()
    {
        _isFirstRunWizardDismissed = true;
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
    }

    public void WizardAutoDetect()
    {
        Settings.AutoDetectSaveDirectories();
        Refresh();
        if (Saves.Count > 0)
        {
            DismissFirstRunWizard();
        }
    }

    public void WizardAddDirectory()
    {
        if (Settings.AddSaveDirectory(WizardDirectoryInput))
        {
            WizardDirectoryInput = string.Empty;
            Refresh();
            if (Saves.Count > 0)
            {
                DismissFirstRunWizard();
            }
        }
    }

    public bool IsOverviewTab => SelectedTab == "overview";
    public bool IsInventoryTab => SelectedTab == "inventory";
    public bool IsFactionsTab => SelectedTab == "factions";
    public bool IsStashesTab => SelectedTab == "stashes";
    public bool IsTransitionsTab => SelectedTab == "transitions";
    public bool IsBackupsTab => SelectedTab == "backups";
    public bool IsSettingsTab => SelectedTab == "settings";
    public bool IsCapabilitiesTab => SelectedTab == "capabilities";
    public bool IsCompanionTab => SelectedTab == "companion";
    public bool IsCloudTab => SelectedTab == "cloud";
    public bool IsAchievementsTab => SelectedTab == "achievements";
    public bool IsUpdatesTab => SelectedTab == "updates";

    public bool ShowOverviewScreen => HasSelection && IsOverviewTab;
    public bool ShowInventoryScreen => HasSelection && IsInventoryTab;
    public bool ShowFactionsScreen => HasSelection && IsFactionsTab;
    public bool ShowStashesScreen => HasSelection && IsStashesTab;
    public bool ShowTransitionsScreen => HasSelection && IsTransitionsTab;
    public bool ShowBackupsScreen => HasSelection && IsBackupsTab;
    public bool ShowSettingsScreen => IsSettingsTab;
    public bool ShowCapabilitiesScreen => IsCapabilitiesTab;
    public bool ShowCompanionScreen => IsCompanionTab;
    public bool ShowCloudScreen => IsCloudTab;
    public bool ShouldShowEmptyState => HasNoSelection && !IsSettingsTab && !IsCapabilitiesTab && !IsCompanionTab && !IsCloudTab && !IsAchievementsTab && !IsUpdatesTab && !IsFirstRunWizardVisible;
    public bool ShowAchievementsScreen => IsAchievementsTab;
    public bool ShowUpdatesScreen => IsUpdatesTab;


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
            OnPropertyChanged(nameof(ShowCapabilitiesScreen));
            OnPropertyChanged(nameof(ShouldShowEmptyState));

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
                return "Выберите сохранение для редактирования.";
            var plan = BuildCurrentEditPlan(SelectedSave);
            if (!EditService.CanEdit(SelectedSave.ReleaseId, plan.EditKinds))
            {
                return $"Эта правка для формата {SelectedSave.ReleaseName} не поддерживается (см. «Возможности»).";
            }
            if (!HasDraftChanges)
                return "Нет несохранённых изменений.";
            if (!InputsAreValid(SelectedSave))
                return "Введены некорректные значения (проверьте введённые числа).";
            return "Сохранить изменения в файл сейва (с созданием резервной копии).";
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
                return $"Черновик: {_currentJournal.Index} действ.";
            }
            if (SelectedSave is not null && HasPendingChanges(SelectedSave))
            {
                return "Есть несохранённые изменения";
            }
            return "Все изменения сохранены";
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
            if (SetProperty(ref _selectedItem, value))
            {
                RemoveSelectedItemCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Re-reads the library with the new catalogs, keeping the selected save; drafts live on disk and are restored.</summary>
    private void ReloadSelectedSave()
    {
        var selectedPath = SelectedSave?.FilePath;
        Refresh();
        if (selectedPath is not null && Saves.FirstOrDefault(save => save.FilePath == selectedPath) is { } same) SelectedSave = same;
    }

    public void Refresh()
    {
        Saves.Clear();
        foreach (var path in EnumerateSaveFiles(_saveDirectoriesProvider()))
        {
            var parsed = TryReadSave(path);
            if (parsed is not null) Saves.Add(parsed);
        }

        SelectedSave = Saves.FirstOrDefault();
        RefreshBackups();
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
    }

    public void RefreshBackups()
    {
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
    }

    public bool AddPreviewSave(string path)
    {
        var parsed = TryReadSave(path);
        if (parsed is null) return false;
        Saves.Add(parsed);
        SelectedSave = parsed;
        OnPropertyChanged(nameof(IsFirstRunWizardVisible));
        OnPropertyChanged(nameof(ShouldShowEmptyState));
        return true;
    }

    public void Undo()
    {
        if (_currentJournal is { CanUndo: true })
        {
            _currentJournal = _currentJournal.Undo();
            _draftStore.Save(_currentJournal);
            ApplyPlanToUI(_currentJournal.Current);
            UpdateDraftState();
        }
    }

    public void Redo()
    {
        if (_currentJournal is { CanRedo: true })
        {
            _currentJournal = _currentJournal.Redo();
            _draftStore.Save(_currentJournal);
            ApplyPlanToUI(_currentJournal.Current);
            UpdateDraftState();
        }
    }

    public void DiscardDraft()
    {
        if (SelectedSave is null) return;
        _draftStore.Remove(SelectedSave.SourceSha256);
        _currentJournal = new DraftJournal([new EditPlan(SelectedSave.SourceSha256)], 0);

        _moneyInput = SelectedSave.Money.ToString(CultureInfo.InvariantCulture);
        foreach (var item in SelectedSave.Inventory)
        {
            item.CountInput = item.OriginalCount?.ToString(CultureInfo.InvariantCulture) ?? "1";
            item.ConditionPercent = item.OriginalCondition.HasValue ? (int)Math.Round(item.OriginalCondition.Value * 100f) : 100;
            item.Placement = item.OriginalPlacement;
            item.IsDeleted = false;
            foreach (var up in item.UpgradeItems) up.IsInstalled = up.OriginalInstalled;
        }

        foreach (var relation in SelectedSave.FactionRelations)
        {
            relation.Goodwill = relation.OriginalGoodwill;
        }

        foreach (var stash in SelectedSave.Stashes)
        {
            foreach (var stashItem in stash.Items) stashItem.IsTaken = false;
        }

        UpdateDraftState();
        StatusMessage = "Черновик сброшен.";
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

    public void TakeStashItem(StashItemViewModel item)
    {
        item.IsTaken = !item.IsTaken;
        RecordDraftChange();
    }

    public void AdjustFactionRelation(FactionRelationViewModel relation, int delta)
    {
        var releaseId = SelectedSave?.ReleaseId ?? "stalker-cop";
        var catalog = TryCatalog(releaseId, out var bundle) ? bundle.Factions : null;
        var min = catalog?.GoodwillMin ?? -3000;
        var max = catalog?.GoodwillMax ?? 1000;
        relation.Goodwill = Math.Clamp(relation.Goodwill + delta, min, max);
        RecordDraftChange();
    }

    public AddItemViewModel CreateAddItemDialog()
    {
        var releaseId = SelectedSave?.ReleaseId ?? "stalker-cop";
        var catalog = TryCatalog(releaseId, out var bundle) ? bundle.Items : null;
        return new AddItemViewModel(catalog, OfficialNames, releaseId);
    }

    public void StageItemAddition(string sectionKey, uint quantity)
    {
        if (SelectedSave is null) return;
        var plan = BuildCurrentEditPlan(SelectedSave);
        var currentAdds = plan.Adds.ToList();
        currentAdds.Add(new ItemAddRequest(sectionKey, quantity, "inventory"));
        var nextPlan = new EditPlan(
            SelectedSave.SourceSha256,
            plan.Money,
            plan.StackCounts,
            plan.DetachHandles,
            currentAdds,
            plan.StashTakes,
            plan.StashPuts,
            plan.Upgrades,
            plan.PlayerFaction,
            plan.FactionRelations,
            plan.Stalker2StashTakeHandle,
            plan.Durability,
            plan.Placements);

        RecordPlan(nextPlan);
        StatusMessage = $"Предмет {sectionKey} ({quantity} шт.) добавлен в очередь на запись.";
    }

    public void RestoreBackup(BackupRecordViewModel backup, bool inPlace)
    {
        try
        {
            if (inPlace)
            {
                var receipt = LocalSaveStorage.RestoreInPlace(backup.JournalPath);
                StatusMessage = $"Восстановлено на место. Создан страховочный бэкап: {Path.GetFileName(receipt.SafetyBackupPath)}";
            }
            else
            {
                var targetPath = Path.Combine(Path.GetDirectoryName(backup.SourcePath)!, $"{Path.GetFileNameWithoutExtension(backup.SourceName)}_restored_{DateTime.Now:yyyyMMdd_HHmmss}.sav");
                var receipt = LocalSaveStorage.RestoreBackup(backup.JournalPath, targetPath);
                StatusMessage = $"Восстановлено в файл: {Path.GetFileName(receipt.OutputPath)}";
            }
            Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка восстановления: {ex.Message}";
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

            var refreshed = TryReadSave(selected.FilePath);
            if (refreshed is null || !string.Equals(refreshed.SourceSha256, receipt.OutputSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved file could not be reopened after write verification.");
            }

            var index = Saves.IndexOf(selected);
            if (index >= 0) Saves[index] = refreshed;
            else Saves.Add(refreshed);

            SelectedSave = refreshed;
            RefreshBackups();
            StatusMessage = $"Сохранено успешно. Backup: {Path.GetFileName(receipt.BackupPath)}";
            GameAudioService.Instance.Play(SoundEvent.Save);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось сохранить: {exception.Message}";
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

            if (item.CanEditUpgrades && item.HasUpgrades)
            {
                var currentInstalled = item.UpgradeItems.Where(u => u.IsInstalled).Select(u => u.Key).ToList();
                if (!currentInstalled.SequenceEqual(item.OriginalUpgrades, StringComparer.Ordinal))
                {
                    upgrades.Add((ushort)item.Handle, currentInstalled);
                }
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
        _currentJournal = (_currentJournal ?? new DraftJournal([new EditPlan(SelectedSave.SourceSha256)], 0)).Record(plan);
        _draftStore.Save(_currentJournal);
        UpdateDraftState();
    }

    private void ApplyPlanToUI(EditPlan plan)
    {
        if (SelectedSave is null) return;

        _moneyInput = plan.Money.HasValue
            ? plan.Money.Value.ToString(CultureInfo.InvariantCulture)
            : SelectedSave.Money.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(MoneyInput));

        foreach (var item in SelectedSave.Inventory)
        {
            if (plan.StackCounts.TryGetValue(item.Handle, out var count))
            {
                item.CountInput = count.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                item.CountInput = item.OriginalCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (plan.Durability.TryGetValue(item.Handle, out var cond))
            {
                item.ConditionPercent = (int)Math.Round(cond * 100);
            }
            else
            {
                item.ConditionPercent = item.OriginalCondition.HasValue
                    ? (int)Math.Round(item.OriginalCondition.Value * 100)
                    : 100;
            }

            item.IsDeleted = plan.DetachHandles.Contains((ushort)item.Handle);

            if (plan.Placements.FirstOrDefault(p => p.Handle == item.Handle) is { } plc)
            {
                item.Placement = plc.Type;
            }
            else
            {
                item.Placement = item.OriginalPlacement ?? "backpack";
            }

            if (plan.Upgrades.TryGetValue((ushort)item.Handle, out var ups))
            {
                foreach (var up in item.UpgradeItems)
                {
                    up.IsInstalled = ups.Contains(up.Key, StringComparer.Ordinal);
                }
            }
            else
            {
                foreach (var up in item.UpgradeItems)
                {
                    up.IsInstalled = item.OriginalUpgrades.Contains(up.Key, StringComparer.Ordinal);
                }
            }
        }

        foreach (var stash in SelectedSave.Stashes)
        {
            foreach (var stashItem in stash.Items)
            {
                stashItem.IsTaken = plan.StashTakes.Contains(stashItem.Handle);
            }
        }

        foreach (var rel in SelectedSave.FactionRelations)
        {
            if (plan.FactionRelations.TryGetValue(rel.Community, out var val))
            {
                rel.Goodwill = val;
            }
            else
            {
                rel.Goodwill = rel.OriginalGoodwill;
            }
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
        FilteredInventory.Clear();
        if (SelectedSave is null) return;

        var query = _inventorySearchText.Trim();
        foreach (var item in SelectedSave.Inventory)
        {
            if (item.IsDeleted) continue;

            if (_selectedCategory != "all" && !string.Equals(item.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query) &&
                !item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                !item.TypeKey.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            FilteredInventory.Add(item);
        }
    }

    private void OnInventoryItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RecordDraftChange();
    }

    private static IEnumerable<string> EnumerateSaveFiles(IEnumerable<string> directories)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MaxRecursionDepth = 4,
                }).Where(IsSupportedSaveFile).ToArray();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsBackupArtifact(file)) continue;
                var fullPath = Path.GetFullPath(file);
                if (seen.Add(fullPath)) yield return fullPath;
            }
        }
    }

    private static SaveFileSummary? TryReadSave(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 512L * 1024 * 1024) return null;
            var bytes = File.ReadAllBytes(path);
            var sourceSha256 = Sha256(bytes);

            try
            {
                return FromXRay(XRayTrilogyReader.FromBytes(bytes), path, sourceSha256, info.Length, info.LastWriteTime);
            }
            catch (XRayFormatException) { }

            try
            {
                return FromXRay(XRayEnhancedReader.FromBytes(bytes), path, sourceSha256, info.Length, info.LastWriteTime);
            }
            catch (XRayFormatException) { }

            try
            {
                return FromStalker2(Stalker2SaveReader.FromBytes(bytes), path, sourceSha256, info.Length, info.LastWriteTime);
            }
            catch (Stalker2FormatException)
            {
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (bool Writable, string? Reason) CheckCapability(string releaseId, string capability)
    {
        if (!EditService.CanEdit(releaseId))
        {
            var isS2 = string.Equals(releaseId, "stalker2", StringComparison.OrdinalIgnoreCase);
            var reason = isS2
                ? "Запись S.T.A.L.K.E.R. 2 выключена в UI до верификации мутаций в живой игре."
                : $"Запись для формата {releaseId} выключена в UI в целях безопасности.";
            return (false, reason);
        }

        try
        {
            var support = CapabilityRegistry.Get(releaseId, capability);
            return (support.Writable, support.Writable ? null : (support.Reason ?? "Операция не поддерживается данным форматом"));
        }
        catch (KeyNotFoundException)
        {
            return (false, "Операция не поддерживается данным форматом");
        }
    }

    private static bool HasCapability(string releaseId, string capability) =>
        CheckCapability(releaseId, capability).Writable;

    private static SaveFileSummary FromXRay(
        XRayTrilogySave save,
        string path,
        string sourceSha256,
        long fileSize,
        DateTime lastModified)
    {
        var formatId = save.FormatId;
        var (canEditMoney, moneyReason) = CheckCapability(formatId, "edit_money");
        var (canEditStacks, stacksReason) = CheckCapability(formatId, "edit_stacks");
        var (canEditDurability, durabilityReason) = CheckCapability(formatId, "edit_durability");
        var (canEditPlacement, placementReason) = CheckCapability(formatId, "edit_placement");
        var (canEditUpgrades, upgradesReason) = CheckCapability(formatId, "edit_upgrades");
        var (canEditRelations, relationsReason) = CheckCapability(formatId, "edit_relations");
        var (canEditPlayerFaction, playerFactionReason) = CheckCapability(formatId, "edit_player_faction");
        var canEditFactions = canEditRelations || canEditPlayerFaction;
        var factionReason = canEditFactions ? null : (relationsReason ?? playerFactionReason);
        var (canMoveItems, moveReason) = CheckCapability(formatId, "move_items");
        var canEditStashes = canMoveItems && save.Stashes.Count > 0;
        var stashesReason = !canMoveItems ? moveReason : (save.Stashes.Count == 0 ? "В сохранении нет тайников" : null);
        var (canAddItems, addReason) = CheckCapability(formatId, "add_items");
        var (canRemoveItems, removeReason) = CheckCapability(formatId, "remove_items");

        var catalog = TryCatalog(formatId, out var bundle) ? bundle : null;
        var upgradeCatalog = catalog?.Upgrades;

        var inventory = save.Inventory.Select(item =>
        {
            var localizedName = OfficialNames.Resolve(formatId, "items", item.TypeKey, CultureInfo.CurrentUICulture.Name)
                ?? item.TypeKey;
            var availableUpgrades = upgradeCatalog?.ForItem(item.TypeKey);
            return new InventoryLineViewModel(
                localizedName,
                item.TypeKey,
                item.Handle,
                item.Category,
                item.Count,
                canEditStacks && item.EditableCount,
                item.Condition,
                canEditDurability && item.ConditionEditable,
                item.PlacementType,
                canEditPlacement && item.PlacementEditable,
                item.Upgrades,
                canEditUpgrades,
                availableUpgrades,
                countDisabledReason: stacksReason,
                conditionDisabledReason: durabilityReason,
                placementDisabledReason: placementReason,
                upgradesDisabledReason: upgradesReason,
                baseSlot: item.PlacementBaseSlot,
                releaseId: formatId);
        });

        var stashes = save.Stashes.Select(s => new StashViewModel(
            s.Handle,
            s.Name,
            s.Level,
            s.Items.Select(i => new StashItemViewModel(
                i.Handle,
                i.TypeKey,
                OfficialNames.Resolve(formatId, "items", i.TypeKey, CultureInfo.CurrentUICulture.Name) ?? i.TypeKey,
                i.Count ?? 1,
                canEdit: canEditStashes,
                disabledReason: stashesReason))));

        var factionRelations = new List<FactionRelationViewModel>();
        var factionCatalog = catalog?.Factions;
        if (factionCatalog is not null)
        {
            foreach (var relation in save.FactionRelations)
            {
                var factionDef = factionCatalog.Factions.FirstOrDefault(f => f.NumericId == relation.CommunityIndex);
                var commKey = factionDef?.Key ?? $"faction_{relation.CommunityIndex}";
                var localizedFaction = OfficialNames.Resolve(formatId, "factions", commKey, CultureInfo.CurrentUICulture.Name)
                    ?? factionDef?.DisplayName
                    ?? commKey;
                factionRelations.Add(new FactionRelationViewModel(commKey, localizedFaction, relation.Value, canEditFactions, factionReason));
            }
        }

        string? playerFaction = null;
        if (save.PlayerFactionIndex.HasValue && factionCatalog is not null)
        {
            var def = factionCatalog.Factions.FirstOrDefault(f => f.NumericId == save.PlayerFactionIndex.Value);
            playerFaction = def?.DisplayName ?? def?.Key;
        }

        // Real level changers from X-Ray registry (read-only per AGENTS.md)
        var transitions = save.LevelChangers.Select(lc => new TransitionViewModel(
            lc.Handle,
            lc.Name,
            lc.NameReplace,
            lc.ParentId,
            lc.ObjectVersion)).ToList();

        var levelName = save.Stashes.FirstOrDefault(s => !string.IsNullOrEmpty(s.Level))?.Level;
        if (!string.IsNullOrEmpty(levelName))
        {
            levelName = OfficialNames.Resolve(formatId, "levels", levelName, CultureInfo.CurrentUICulture.Name) ?? levelName;
        }

        return new SaveFileSummary(
            path,
            ReleaseName(formatId),
            formatId,
            sourceSha256,
            save.Money,
            canEditMoney,
            inventory,
            fileSize,
            lastModified,
            save.ActorName,
            save.ActorHealth,
            save.ActorRank,
            save.ActorReputation,
            save.GameTime,
            save.TimeFactor,
            levelName,
            playerFaction,
            canEditFactions,
            canEditUpgrades,
            canEditDurability,
            canEditPlacement,
            canEditStashes,
            canAddItems,
            canRemoveItems,
            crcOk: true,
            stashes: stashes,
            transitions: transitions,
            factionRelations: factionRelations,
            moneyDisabledReason: moneyReason,
            factionDisabledReason: factionReason,
            upgradesDisabledReason: upgradesReason,
            durabilityDisabledReason: durabilityReason,
            placementDisabledReason: placementReason,
            stashesDisabledReason: stashesReason,
            addItemsDisabledReason: addReason,
            removeItemsDisabledReason: removeReason);
    }

    private static SaveFileSummary FromStalker2(
        Stalker2Save save,
        string path,
        string sourceSha256,
        long fileSize,
        DateTime lastModified)
    {
        const string formatId = "stalker2";
        var (canEditMoney, moneyReason) = CheckCapability(formatId, "edit_money");
        var (canEditStacks, stacksReason) = CheckCapability(formatId, "edit_stacks");
        var (canEditDurability, durabilityReason) = CheckCapability(formatId, "edit_durability");
        var (canEditPlacement, placementReason) = CheckCapability(formatId, "edit_placement");
        var (canEditUpgrades, upgradesReason) = CheckCapability(formatId, "edit_upgrades");
        var (canEditRelations, relationsReason) = CheckCapability(formatId, "edit_relations");
        var (canEditPlayerFaction, playerFactionReason) = CheckCapability(formatId, "edit_player_faction");
        var canEditFaction = canEditRelations || canEditPlayerFaction;
        var factionReason = canEditFaction ? null : (relationsReason ?? playerFactionReason);
        var (canEditStashes, stashesReason) = CheckCapability(formatId, "move_items");
        var (canAddItems, addReason) = CheckCapability(formatId, "add_items");
        var (canRemoveItems, removeReason) = CheckCapability(formatId, "remove_items");

        var catalog = TryCatalog(formatId, out var bundle) ? bundle.Items : null;
        var s2Items = Stalker2ItemCatalog.LoadEmbedded();
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            s2Items.Name(item.DisplayName, language) ?? item.DisplayName ?? catalog?.Resolve(item.TypeKey)?.DisplayName ?? item.TypeKey,
            item.TypeKey,
            item.Handle,
            item.Category,
            item.Count,
            canEditCount: canEditStacks,
            item.Condition,
            canEditCondition: canEditDurability,
            item.Storage,
            canEditPlacement: canEditPlacement,
            upgrades: item.Upgrades,
            canEditUpgrades: canEditUpgrades,
            availableUpgrades: null,
            countDisabledReason: stacksReason,
            conditionDisabledReason: durabilityReason,
            placementDisabledReason: placementReason,
            upgradesDisabledReason: upgradesReason,
            releaseId: formatId,
            iconKey: item.DisplayName ?? item.TypeKey));

        return new SaveFileSummary(
            path,
            ReleaseName(formatId),
            formatId,
            sourceSha256,
            save.Money,
            canEditMoney,
            inventory: inventory,
            fileSizeBytes: fileSize,
            lastModified: lastModified,
            canEditFaction: canEditFaction,
            canEditUpgrades: canEditUpgrades,
            canEditDurability: canEditDurability,
            canEditPlacement: canEditPlacement,
            canEditStashes: canEditStashes,
            canAddItems: canAddItems,
            canRemoveItems: canRemoveItems,
            crcOk: save.StoredCrc32 == save.ComputedCrc32,
            moneyDisabledReason: moneyReason,
            factionDisabledReason: factionReason,
            upgradesDisabledReason: upgradesReason,
            durabilityDisabledReason: durabilityReason,
            placementDisabledReason: placementReason,
            stashesDisabledReason: stashesReason,
            addItemsDisabledReason: addReason,
            removeItemsDisabledReason: removeReason);
    }


    private bool HasPendingChanges(SaveFileSummary save)
    {
        if (!string.Equals(save.Money.ToString(CultureInfo.InvariantCulture), MoneyInput, StringComparison.Ordinal))
            return true;

        if (save.Inventory.Any(i => i.IsDeleted ||
            (i.CanEditCount && !string.Equals(i.OriginalCount?.ToString(CultureInfo.InvariantCulture), i.CountInput, StringComparison.Ordinal)) ||
            (i.CanEditCondition && i.OriginalCondition.HasValue && Math.Abs(i.ConditionFraction - i.OriginalCondition.Value) > 0.005f) ||
            (i.CanEditPlacement && !string.Equals(i.Placement, i.OriginalPlacement, StringComparison.Ordinal)) ||
            (i.CanEditUpgrades && i.HasUpgrades && !i.UpgradeItems.Where(u => u.IsInstalled).Select(u => u.Key).SequenceEqual(i.OriginalUpgrades, StringComparer.Ordinal))))
            return true;

        if (save.Stashes.Any(s => s.Items.Any(i => i.IsTaken)))
            return true;

        if (save.FactionRelations.Any(r => r.Goodwill != r.OriginalGoodwill))
            return true;

        return false;
    }

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

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string GetDefaultBackupDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new IOException("The local application data directory is not available.");
        }

        return Path.Combine(localApplicationData, "StalkerSaveEditor", "backups");
    }

    /// <summary>
    /// Resolves the <c>mods/companion</c> directory for <see cref="CompanionServiceAdapter"/>.
    /// Looks next to the executable first (packaged build), then walks up the source tree.
    /// </summary>
    private static string ResolveModSourceRoot()
    {
        // Packaged: mods/companion sits next to the binary.
        var execDir = Path.GetDirectoryName(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
        var candidate = Path.Combine(execDir, "mods", "companion");
        if (Directory.Exists(candidate)) return candidate;

        // Development: walk up from executable directory to find repository root (has mods/).
        var current = execDir;
        for (var depth = 0; depth < 8; depth++)
        {
            var modsDir = Path.Combine(current!, "mods", "companion");
            if (Directory.Exists(modsDir)) return modsDir;
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current) break;
            current = parent;
        }

        // Fallback: return a non-existent path; CompanionInstaller.GetStatus() will report the issue.
        return Path.Combine(execDir, "mods", "companion");
    }

    private static bool IsBackupArtifact(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return stem.EndsWith("_ORIGINAL", StringComparison.OrdinalIgnoreCase) ||
            stem.EndsWith("_EDITED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedSaveFile(string path) =>
        Path.GetExtension(path) is { } extension &&
        (extension.Equals(".sav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".scop", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".scs", StringComparison.OrdinalIgnoreCase));

    private static string ReleaseName(string releaseId) => releaseId switch
    {
        "stalker-soc" => "Тень Чернобыля",
        "stalker-soc-ee" => "Тень Чернобыля (Enhanced Edition)",
        "stalker-cs" => "Чистое Небо",
        "stalker-cs-ee" => "Чистое Небо (Enhanced Edition)",
        "stalker-cop" => "Зов Припяти",
        "stalker-cop-ee" => "Зов Припяти (Enhanced Edition)",
        "stalker2" => "S.T.A.L.K.E.R. 2: Сердце Чернобыля",
        _ => releaseId,
    };
}
