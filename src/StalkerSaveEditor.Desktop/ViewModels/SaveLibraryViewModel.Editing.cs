using StalkerSaveEditor.Core.Diagnostics;
using System.Globalization;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed partial class SaveLibraryViewModel : ObservableViewModel, IDisposable
{
    public void Undo()
    {
        if (_draft.Undo() is { } plan) ShowPlan(plan);
    }

    public void Redo()
    {
        if (_draft.Redo() is { } plan) ShowPlan(plan);
    }

    private void ShowPlan(EditPlan plan)
    {
        ApplyPlanToUI(plan);
        UpdateDraftState();
    }

    public void DiscardDraft()
    {
        if (SelectedSave is null) return;
        ShowPlan(_draft.Discard(SelectedSave.SourceSha256));
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
            var pile = SelectedItem.GroupKey;
            SelectedItem.IsDeleted = true;
            RecordDraftChange();
            ApplyInventoryFilter();
            // Removing one of several identical objects leaves the rest of the pile selected.
            SelectedItem = FilteredInventory.FirstOrDefault(item => pile is not null && item.GroupKey == pile)
                ?? FilteredInventory.FirstOrDefault();
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
            var receipt = SaveEditSession.Relocate(save, target.Anchor, _backupDirectoryProvider());
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

    /// <summary>
    /// The checkbox in the list has already written <see cref="StashItemViewModel.IsTaken"/> through its binding;
    /// only the draft is recorded. (Toggling here as well undid the click.)
    /// </summary>
    public void StashSelectionChanged() => RecordDraftChange();

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
        var journal = _draft.Current;
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
            var (receipt, refreshed) = SaveEditSession.WritePlan(
                selected,
                plan,
                TryCatalog(selected.ReleaseId, out var bundle) ? bundle : null,
                _backupDirectoryProvider(),
                _draftStore);

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
            adds: _draft.Current?.Adds is { Count: > 0 } addsList ? addsList : null,
            stashTakes: stashTakes.Count > 0 ? stashTakes : null,
            stashPuts: _draft.Current?.StashPuts is { Count: > 0 } putsList ? putsList : null,
            upgrades: upgrades.Count > 0 ? upgrades : null,
            playerFaction: null,
            factionRelations: factionRelations.Count > 0 ? factionRelations : null,
            durability: durability.Count > 0 ? durability : null,
            placements: placements.Count > 0 ? placements : null);
    }

    private void LoadDraft(SaveFileSummary save)
    {
        if (_draft.Open(save.SourceSha256) is { } plan) ApplyPlanToUI(plan);
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
        if (_draft.HasUnsupportedEdits)
        {
            // Nothing may be edited on top of edits we cannot read: put the fields back and say why.
            if (_draft.Current is { } current && !current.HasSameEdits(plan)) ApplyPlanToUI(current);
            StatusMessage = L.T("В черновике есть правки из другой версии редактора, которые эта версия не понимает. Сбросьте черновик, чтобы продолжить (он сохранится рядом).");
            UpdateDraftState();
            return;
        }
        if (_draft.Record(SelectedSave.SourceSha256, plan)) UpdateDraftState();
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
        FilteredInventory.ReplaceAll(InventoryLineViewModel.GroupPiles(
            SelectedSave.Inventory.Where(item =>
                !item.IsDeleted &&
                (_selectedCategory == "all" || string.Equals(item.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(query) ||
                 item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 item.TypeKey.Contains(query, StringComparison.CurrentCultureIgnoreCase))),
            SelectedItem));
    }

    private void OnInventoryItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_applyingPlan || e.PropertyName is null || !EditableItemProperties.Contains(e.PropertyName)) return;
        RecordDraftChange();
        // An edited object no longer equals the rest of its pile: the others get a row of their own.
        if (sender is InventoryLineViewModel { GroupSize: > 1 } line && e.PropertyName != nameof(InventoryLineViewModel.IsDeleted))
        {
            ApplyInventoryFilter();
            SelectedItem = line;
        }
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
}
