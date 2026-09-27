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

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class SaveLibraryViewModel : ObservableViewModel
{
    private static readonly IReadOnlyDictionary<string, CatalogBundle> Catalogs = CatalogBundleReader.LoadEmbedded();
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
        _saveDirectoriesProvider = saveDirectoriesProvider ?? SaveDirectoryDiscovery.GetExistingDirectories;
        _backupDirectoryProvider = backupDirectoryProvider ?? GetDefaultBackupDirectory;
        _draftStore = new DraftStore(draftsDirectory);

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
            _saveDirectoriesProvider(),
            _backupDirectoryProvider());

        if (discoverLocalSaves) Refresh();
    }

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

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsOverviewTab));
                OnPropertyChanged(nameof(IsInventoryTab));
                OnPropertyChanged(nameof(IsFactionsTab));
                OnPropertyChanged(nameof(IsStashesTab));
                OnPropertyChanged(nameof(IsTransitionsTab));
                OnPropertyChanged(nameof(IsBackupsTab));
                OnPropertyChanged(nameof(IsSettingsTab));
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
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(HasDraftChanges));
            OnPropertyChanged(nameof(DraftStatusText));

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

    public bool CanSave => !_isSaving && SelectedSave is not null && HasDraftChanges && InputsAreValid(SelectedSave);

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
        relation.Goodwill = Math.Clamp(relation.Goodwill + delta, -5000, 5000);
        RecordDraftChange();
    }

    public AddItemViewModel CreateAddItemDialog()
    {
        var releaseId = SelectedSave?.ReleaseId ?? "stalker-cop";
        var catalog = Catalogs.TryGetValue(releaseId, out var bundle) ? bundle.Items : null;
        return new AddItemViewModel(catalog, OfficialNames, releaseId);
    }

    public void StageItemAddition(string sectionKey, uint quantity)
    {
        if (SelectedSave is null) return;
        var plan = BuildCurrentEditPlan(SelectedSave);
        var currentAdds = plan.Adds.ToList();
        currentAdds.Add(new ItemAddRequest(sectionKey, quantity, "actor_inventory"));
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
            var catalog = Catalogs.TryGetValue(selected.ReleaseId, out var bundle) ? bundle : null;
            var prepared = PrepareSaveEdit(source, plan, selected.ReleaseId, catalog);

            var receipt = LocalSaveReplacement.ReplaceLocal(
                selected.FilePath,
                prepared,
                _backupDirectoryProvider(),
                readBack => VerifyReadBack(readBack.Span, selected.ReleaseId, plan));

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
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось сохранить: {exception.Message}";
        }
        finally
        {
            _isSaving = false;
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private PreparedEdit PrepareSaveEdit(byte[] source, EditPlan plan, string releaseId, CatalogBundle? catalog)
    {
        if (releaseId == "stalker2")
        {
            var working = source;
            PreparedEdit? last = null;
            if (plan.Money.HasValue)
            {
                last = Stalker2MoneyWriter.Prepare(working, new EditPlan(Sha256(working), money: plan.Money));
                working = last.Data.ToArray();
            }
            if (plan.StackCounts.Count > 0)
            {
                last = Stalker2StackWriter.Prepare(working, new EditPlan(Sha256(working), stackCounts: plan.StackCounts));
                working = last.Data.ToArray();
            }
            if (plan.Durability.Count > 0)
            {
                last = Stalker2DurabilityWriter.Prepare(working, new EditPlan(Sha256(working), durability: plan.Durability));
                working = last.Data.ToArray();
            }
            if (plan.Stalker2StashTakeHandle.HasValue)
            {
                last = Stalker2StashWriter.Prepare(working, new EditPlan(Sha256(working), stalker2StashTakeHandle: plan.Stalker2StashTakeHandle));
            }
            return last ?? throw new InvalidOperationException("No valid S2 edits to apply.");
        }

        // X-Ray
        if (plan.DetachHandles.Count > 0)
        {
            return XRayDeleteWriter.Prepare(source, new EditPlan(plan.SourceSha256, detachHandles: plan.DetachHandles));
        }

        if (plan.Adds.Count > 0)
        {
            var itemsCatalog = catalog?.Items ?? throw new InvalidOperationException("Item addition requires catalog.");
            return XRayAddWriter.Prepare(source, new EditPlan(plan.SourceSha256, adds: plan.Adds), itemsCatalog);
        }

        return XRayEditWriter.Prepare(source, plan, catalog);
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
                placements.Add(new XRayPlacementChange(item.Handle, item.Placement, item.Placement == "slot" ? 1 : null));
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
            adds: _currentJournal?.Current.Adds,
            stashTakes: stashTakes.Count > 0 ? stashTakes : null,
            stashPuts: _currentJournal?.Current.StashPuts,
            upgrades: upgrades.Count > 0 ? upgrades : null,
            playerFaction: save.PlayerFaction,
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

        if (plan.Money.HasValue)
        {
            _moneyInput = plan.Money.Value.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(MoneyInput));
        }

        foreach (var item in SelectedSave.Inventory)
        {
            if (plan.StackCounts.TryGetValue(item.Handle, out var count))
            {
                item.CountInput = count.ToString(CultureInfo.InvariantCulture);
            }
            if (plan.Durability.TryGetValue(item.Handle, out var cond))
            {
                item.ConditionPercent = (int)Math.Round(cond * 100);
            }
            if (plan.DetachHandles.Contains((ushort)item.Handle))
            {
                item.IsDeleted = true;
            }
            if (plan.Placements.FirstOrDefault(p => p.Handle == item.Handle) is { } plc)
            {
                item.Placement = plc.Type;
            }
            if (plan.Upgrades.TryGetValue((ushort)item.Handle, out var ups))
            {
                foreach (var up in item.UpgradeItems)
                {
                    up.IsInstalled = ups.Contains(up.Key, StringComparer.Ordinal);
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

        if (plan.FactionRelations.Count > 0)
        {
            foreach (var rel in SelectedSave.FactionRelations)
            {
                if (plan.FactionRelations.TryGetValue(rel.Community, out var val))
                {
                    rel.Goodwill = val;
                }
            }
        }
    }

    private void UpdateDraftState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanSave));
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

    private static bool HasCapability(string releaseId, string capability)
    {
        try
        {
            return CapabilityRegistry.Get(releaseId, capability).Writable;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static SaveFileSummary FromXRay(
        XRayTrilogySave save,
        string path,
        string sourceSha256,
        long fileSize,
        DateTime lastModified)
    {
        var formatId = save.FormatId;
        var canEditMoney = HasCapability(formatId, "edit_money");
        var canEditStacks = HasCapability(formatId, "edit_stacks");
        var canEditDurability = HasCapability(formatId, "edit_durability");
        var canEditPlacement = HasCapability(formatId, "edit_placement");
        var canEditUpgrades = formatId is "stalker-cs" or "stalker-cs-ee" or "stalker-cop" or "stalker-cop-ee";
        var canEditFactions = HasCapability(formatId, "edit_relations") || HasCapability(formatId, "edit_player_faction");
        var canEditStashes = save.Stashes.Count > 0;
        var canAddItems = HasCapability(formatId, "add_items");
        var canRemoveItems = HasCapability(formatId, "remove_items");

        var catalog = Catalogs.TryGetValue(formatId, out var bundle) ? bundle : null;
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
                availableUpgrades);
        });

        var stashes = save.Stashes.Select(s => new StashViewModel(
            s.Handle,
            s.Name,
            s.Level,
            s.Items.Select(i => new StashItemViewModel(
                i.Handle,
                i.TypeKey,
                OfficialNames.Resolve(formatId, "items", i.TypeKey, CultureInfo.CurrentUICulture.Name) ?? i.TypeKey,
                i.Count ?? 1))));

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
                factionRelations.Add(new FactionRelationViewModel(commKey, localizedFaction, relation.Value, canEditFactions));
            }
        }

        string? playerFaction = null;
        if (save.PlayerFactionIndex.HasValue && factionCatalog is not null)
        {
            var def = factionCatalog.Factions.FirstOrDefault(f => f.NumericId == save.PlayerFactionIndex.Value);
            playerFaction = def?.DisplayName ?? def?.Key;
        }

        // Canonical transitions
        var transitions = GetCanonicalTransitions(formatId);

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
            factionRelations: factionRelations);
    }

    private static SaveFileSummary FromStalker2(
        Stalker2Save save,
        string path,
        string sourceSha256,
        long fileSize,
        DateTime lastModified)
    {
        var catalog = Catalogs.TryGetValue("stalker2", out var bundle) ? bundle.Items : null;
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            item.DisplayName ?? catalog?.Resolve(item.TypeKey)?.DisplayName ?? item.TypeKey,
            item.TypeKey,
            item.Handle,
            item.Category,
            item.Count,
            canEditCount: false,
            item.Condition,
            canEditCondition: false,
            item.Storage,
            canEditPlacement: false,
            upgrades: item.Upgrades,
            canEditUpgrades: false));

        return new SaveFileSummary(
            path,
            "S.T.A.L.K.E.R. 2",
            "stalker2",
            sourceSha256,
            save.Money,
            canEditMoney: false,
            inventory: inventory,
            fileSizeBytes: fileSize,
            lastModified: lastModified,
            crcOk: save.StoredCrc32 == save.ComputedCrc32);
    }

    private static IReadOnlyList<TransitionViewModel> GetCanonicalTransitions(string releaseId) => releaseId switch
    {
        "stalker-cop" or "stalker-cop-ee" =>
        [
            new TransitionViewModel("Затон", "Окрестности Юпитера", "Станция Янов", 298.5f, 5.2f, -120.4f, false),
            new TransitionViewModel("Окрестности Юпитера", "Затон", "Скадовск", 112.0f, -4.1f, 180.3f, false),
            new TransitionViewModel("Окрестности Юпитера", "Припять", "Прачечная", 18.2f, 0.0f, -22.5f, true),
            new TransitionViewModel("Припять", "Окрестности Юпитера", "Станция Янов", -85.1f, 3.4f, 92.0f, false),
        ],
        "stalker-cs" or "stalker-cs-ee" =>
        [
            new TransitionViewModel("Болота", "Кордон", "Южный блокпост", -160.0f, 2.5f, -340.0f, false),
            new TransitionViewModel("Кордон", "Свалка", "Северный блокпост", 35.0f, 0.0f, 280.0f, false),
            new TransitionViewModel("Свалка", "Тёмная Долина", "Восточный переход", 240.0f, 1.2f, 15.0f, false),
            new TransitionViewModel("Свалка", "Агропром", "Западный переход", -210.0f, -1.0f, -50.0f, false),
            new TransitionViewModel("Свалка", "Военные Склады", "Северные холмы", 12.0f, 4.0f, 310.0f, false),
            new TransitionViewModel("Военные Склады", "Рыжий Лес", "Мост через реку", -180.0f, -2.5f, 220.0f, false),
            new TransitionViewModel("Рыжий Лес", "Лиманск", "Мост в город", 45.0f, 0.5f, 195.0f, true),
        ],
        _ =>
        [
            new TransitionViewModel("Кордон", "Свалка", "Северный блокпост", 38.0f, 1.0f, 290.0f, false),
            new TransitionViewModel("Свалка", "Агропром", "Западные ворота", -220.0f, 0.0f, -60.0f, false),
            new TransitionViewModel("Свалка", "Тёмная Долина", "Восточный туннель", 250.0f, 2.0f, 20.0f, false),
            new TransitionViewModel("Свалка", "Бар «100 Рентген»", "Южная застава", 15.0f, 0.0f, 320.0f, false),
            new TransitionViewModel("Бар", "Дикая Территория", "Западная стройка", -140.0f, 1.5f, 80.0f, false),
            new TransitionViewModel("Дикая Территория", "Янтарь", "Лаборатория Сахарова", -290.0f, -5.0f, 40.0f, false),
            new TransitionViewModel("Бар", "Армейские Склады", "Северный блокпост", 20.0f, 3.0f, 340.0f, false),
            new TransitionViewModel("Армейские Склады", "Радар", "Выжигатель Мозгов", 80.0f, 8.0f, 390.0f, false),
            new TransitionViewModel("Радар", "Припять", "Южная окраина", 10.0f, 2.0f, 450.0f, false),
            new TransitionViewModel("Припять", "ЧАЭС", "Саркофаг", 0.0f, 0.0f, 500.0f, true),
        ],
    };

    private static void VerifyReadBack(ReadOnlySpan<byte> data, string expectedReleaseId, EditPlan plan)
    {
        if (expectedReleaseId == "stalker2")
        {
            var parsedS2 = Stalker2SaveReader.FromBytes(data);
            if (plan.Money is { } expectedMoney && parsedS2.Money != expectedMoney)
            {
                throw new InvalidDataException("S2 readback money does not match.");
            }
            return;
        }

        XRayTrilogySave parsed;
        try
        {
            parsed = XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException)
        {
            parsed = XRayEnhancedReader.FromBytes(data);
        }

        if (!string.Equals(parsed.FormatId, expectedReleaseId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The replaced save changed its detected release.");
        }

        if (plan.Money is { } money && parsed.Money != money)
        {
            throw new InvalidDataException("The replaced save money does not match the requested value.");
        }
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
