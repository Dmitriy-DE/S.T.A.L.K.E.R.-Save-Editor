using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed partial class SaveLibraryViewModel : ObservableViewModel, IDisposable
{
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
                    // …and so does closing the window.
                    () => version != Volatile.Read(ref _libraryVersion) || _disposed);
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
        // The history is built once, when the whole library is in (rebuilding it per batch cost N²/batch rows).
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

    private int _backupListVersion;

    /// <summary>
    /// Lists the backups. The first listing of a session hashes every backup file, so in the running application it
    /// happens off the UI thread and the list is filled when it is ready; a newer request replaces an older one.
    /// </summary>
    public void RefreshBackups()
    {
        var backupDir = _backupDirectoryProvider();
        if (!InteractiveApp)
        {
            ShowBackups(ReadBackups(backupDir));
            return;
        }

        var version = ++_backupListVersion;
        BackgroundTask.Run(
            Task.Run(() =>
            {
                var records = ReadBackups(backupDir);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (version == _backupListVersion && !_disposed) ShowBackups(records);
                });
            }, _lifetime.Token),
            "backup list");
    }

    private static IReadOnlyList<LocalSaveBackupRecord> ReadBackups(string backupDir) =>
        Directory.Exists(backupDir) ? LocalSaveStorage.ListBackups([backupDir]) : [];

    private void ShowBackups(IReadOnlyList<LocalSaveBackupRecord> records)
    {
        var selectedJournalPath = SelectedBackup?.JournalPath;
        Backups.Clear();
        foreach (var record in records) Backups.Add(new BackupRecordViewModel(record));

        SelectedBackup = Backups.FirstOrDefault(backup => backup.JournalPath == selectedJournalPath)
            ?? Backups.FirstOrDefault(backup => backup.CanRestore)
            ?? Backups.FirstOrDefault();
        UpdateCompareSubject();
    }

    /// <summary>Remove + insert: Avalonia's virtualizing list throws on a Replace notification for the selected row.</summary>
    private bool _selectedSaveChangedOnDisk;
    private Avalonia.Threading.DispatcherTimer? _diskWatch;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <summary>Cancelled when the window that owns this view model closes; background work started here watches it.</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    /// <summary>Stops the disk watch and tells background work to stop. Called when the main window closes.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _diskWatch?.Stop();
        _diskWatch = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }


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
        SelectedTab = AppTabs.Compare;
        Compare.Selected = Compare.Candidates.FirstOrDefault(candidate => candidate.Path == previous.FilePath);
    }

    private bool CanAddEncyclopediaItem(string releaseId) =>
        SelectedSave is { CanAddItems: true } save &&
        string.Equals(save.ReleaseId.Replace("-ee", string.Empty, StringComparison.Ordinal), releaseId, StringComparison.Ordinal);

    private void AddEncyclopediaItem(string releaseId, string section)
    {
        if (CanAddEncyclopediaItem(releaseId)) StageItemAddition(section, 1);
    }
}
