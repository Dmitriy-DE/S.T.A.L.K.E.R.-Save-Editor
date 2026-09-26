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
    private SaveFileSummary? _selectedSave;
    private string _moneyInput = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isSaving;
    private readonly Func<IReadOnlyList<string>> _saveDirectoriesProvider;
    private readonly Func<string> _backupDirectoryProvider;

    public SaveLibraryViewModel(
        bool discoverLocalSaves = true,
        Func<IReadOnlyList<string>>? saveDirectoriesProvider = null,
        Func<string>? backupDirectoryProvider = null)
    {
        _saveDirectoriesProvider = saveDirectoriesProvider ?? SaveDirectoryDiscovery.GetExistingDirectories;
        _backupDirectoryProvider = backupDirectoryProvider ?? GetDefaultBackupDirectory;
        RefreshCommand = new RelayCommand(Refresh);
        SaveCommand = new RelayCommand(SaveSelected);
        if (discoverLocalSaves) Refresh();
    }

    public ObservableCollection<SaveFileSummary> Saves { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand SaveCommand { get; }

    public SaveFileSummary? SelectedSave
    {
        get => _selectedSave;
        set
        {
            var previous = _selectedSave;
            if (!SetProperty(ref _selectedSave, value)) return;
            if (previous is not null)
            {
                foreach (var item in previous.Inventory) item.PropertyChanged -= OnInventoryItemChanged;
            }

            if (value is not null)
            {
                _moneyInput = value.Money.ToString(CultureInfo.InvariantCulture);
                foreach (var item in value.Inventory) item.PropertyChanged += OnInventoryItemChanged;
            }

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            OnPropertyChanged(nameof(SelectedSaveName));
            OnPropertyChanged(nameof(SelectedReleaseName));
            OnPropertyChanged(nameof(SelectedMoneyDisplay));
            OnPropertyChanged(nameof(SelectedInventory));
            OnPropertyChanged(nameof(MoneyInput));
            OnPropertyChanged(nameof(CanEditMoney));
            OnPropertyChanged(nameof(CanSave));
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
            if (!SetProperty(ref _moneyInput, value)) return;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool CanEditMoney => SelectedSave?.CanEditMoney == true;

    public bool CanSave => !_isSaving && SelectedSave is { CanEditMoney: true } save &&
        HasPendingChanges(save) && InputsAreValid(save);

    public string SelectedMoneyDisplay => SelectedSave is null
        ? string.Empty
        : $"Деньги: {SelectedSave.Money.ToString("N0", CultureInfo.CurrentCulture)}";

    public IReadOnlyList<InventoryLineViewModel> SelectedInventory => SelectedSave?.Inventory ?? [];

    public void Refresh()
    {
        Saves.Clear();
        foreach (var path in EnumerateSaveFiles(_saveDirectoriesProvider()))
        {
            var parsed = TryReadSave(path);
            if (parsed is not null) Saves.Add(parsed);
        }

        SelectedSave = Saves.FirstOrDefault();
    }

    public bool AddPreviewSave(string path)
    {
        var parsed = TryReadSave(path);
        if (parsed is null) return false;
        Saves.Add(parsed);
        SelectedSave = parsed;
        return true;
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
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
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
                return FromXRay(XRayTrilogyReader.FromBytes(bytes), path, sourceSha256);
            }
            catch (XRayFormatException)
            {
                // Enhanced Edition has separate ALIFE versions and markers.
            }

            try
            {
                return FromXRay(XRayEnhancedReader.FromBytes(bytes), path, sourceSha256);
            }
            catch (XRayFormatException)
            {
                // Continue to the S.T.A.L.K.E.R. 2 container reader.
            }

            try
            {
                return FromStalker2(Stalker2SaveReader.FromBytes(bytes), path, sourceSha256);
            }
            catch (Stalker2FormatException)
            {
                return null;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private static SaveFileSummary FromXRay(XRayTrilogySave save, string path, string sourceSha256)
    {
        var canEditStacks = CapabilityRegistry.Get(save.FormatId, "edit_stacks").Writable;
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            OfficialNames.Resolve(save.FormatId, "items", item.TypeKey, CultureInfo.CurrentUICulture.Name) ?? item.TypeKey,
            item.Handle,
            item.Count,
            canEditStacks && item.EditableCount));
        return new SaveFileSummary(
            path,
            ReleaseName(save.FormatId),
            save.FormatId,
            sourceSha256,
            save.Money,
            CapabilityRegistry.Get(save.FormatId, "edit_money").Writable,
            inventory);
    }

    private static SaveFileSummary FromStalker2(Stalker2Save save, string path, string sourceSha256)
    {
        var catalog = Catalogs.TryGetValue("stalker2", out var bundle) ? bundle.Items : null;
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            item.DisplayName ?? catalog?.Resolve(item.TypeKey)?.DisplayName ?? item.TypeKey,
            item.Handle,
            item.Count,
            canEditCount: false));
        return new SaveFileSummary(path, "S.T.A.L.K.E.R. 2", "stalker2", sourceSha256, save.Money, false, inventory);
    }

    public void SaveSelected()
    {
        var selected = SelectedSave;
        if (!CanSave || selected is null) return;

        var money = selected.CanEditMoney &&
            uint.TryParse(MoneyInput, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMoney) &&
            parsedMoney != selected.Money
                ? parsedMoney
                : (uint?)null;
        var stackCounts = new Dictionary<ushort, uint>();
        foreach (var item in selected.Inventory.Where(item => item.CanEditCount))
        {
            if (uint.TryParse(item.CountInput, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
                item.OriginalCount != count)
            {
                stackCounts.Add(checked((ushort)item.Handle), count);
            }
        }

        var plan = new EditPlan(selected.SourceSha256, money, stackCounts);
        _isSaving = true;
        OnPropertyChanged(nameof(CanSave));
        try
        {
            var source = File.ReadAllBytes(selected.FilePath);
            var prepared = XRayEditWriter.Prepare(source, plan);
            var receipt = LocalSaveReplacement.ReplaceLocal(
                selected.FilePath,
                prepared,
                _backupDirectoryProvider(),
                readBack => VerifyReadBack(readBack.Span, selected.ReleaseId, plan));
            var refreshed = TryReadSave(selected.FilePath);
            if (refreshed is null || !string.Equals(refreshed.SourceSha256, receipt.OutputSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved file could not be reopened after write verification.");
            }

            var index = Saves.IndexOf(selected);
            if (index >= 0) Saves[index] = refreshed;
            else Saves.Add(refreshed);
            SelectedSave = refreshed;
            StatusMessage = $"Сохранено. Backup: {Path.GetFileName(receipt.BackupPath)}; recovery: {Path.GetFileName(receipt.RecoveryPath)}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                FormatException or OverflowException or ArgumentException)
        {
            StatusMessage = $"Не удалось сохранить: {exception.Message}";
        }
        finally
        {
            _isSaving = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    private static void VerifyReadBack(ReadOnlySpan<byte> data, string expectedReleaseId, EditPlan plan)
    {
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

        foreach (var (handle, count) in plan.StackCounts)
        {
            if (parsed.Inventory.FirstOrDefault(item => item.Handle == handle)?.Count != count)
            {
                throw new InvalidDataException($"The replaced save stack 0x{handle:X4} does not match the requested value.");
            }
        }
    }

    private bool HasPendingChanges(SaveFileSummary save) =>
        !string.Equals(save.Money.ToString(CultureInfo.InvariantCulture), MoneyInput, StringComparison.Ordinal) ||
        save.Inventory.Any(item => item.CanEditCount &&
            !string.Equals(item.OriginalCount?.ToString(CultureInfo.InvariantCulture), item.CountInput, StringComparison.Ordinal));

    private bool InputsAreValid(SaveFileSummary save)
    {
        if (!uint.TryParse(MoneyInput, NumberStyles.None, CultureInfo.InvariantCulture, out var money) ||
            money > 2_000_000_000)
        {
            return false;
        }

        return save.Inventory.Where(item => item.CanEditCount).All(item =>
            uint.TryParse(item.CountInput, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
            count is > 0 and <= ushort.MaxValue);
    }

    private void OnInventoryItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(InventoryLineViewModel.CountInput))
        {
            OnPropertyChanged(nameof(CanSave));
        }
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
        "stalker-soc" => "Shadow of Chernobyl",
        "stalker-soc-ee" => "Shadow of Chernobyl Enhanced Edition",
        "stalker-cs" => "Clear Sky",
        "stalker-cs-ee" => "Clear Sky Enhanced Edition",
        "stalker-cop" => "Call of Pripyat",
        "stalker-cop-ee" => "Call of Pripyat Enhanced Edition",
        _ => releaseId,
    };
}

public sealed class SaveFileSummary(
    string filePath,
    string releaseName,
    string releaseId,
    string sourceSha256,
    uint money,
    bool canEditMoney,
    IEnumerable<InventoryLineViewModel> inventory)
{
    public string FilePath { get; } = Path.GetFullPath(filePath);

    public string DisplayName { get; } = Path.GetFileName(filePath);

    public string ReleaseName { get; } = releaseName;

    public string ReleaseId { get; } = releaseId;

    public string SourceSha256 { get; } = sourceSha256;

    public uint Money { get; } = money;

    public bool CanEditMoney { get; } = canEditMoney;

    public IReadOnlyList<InventoryLineViewModel> Inventory { get; } = Array.AsReadOnly(inventory.ToArray());
}

public sealed class InventoryLineViewModel : ObservableViewModel
{
    private string _countInput;

    public InventoryLineViewModel(string name, uint handle, uint? count, bool canEditCount)
    {
        Name = name;
        Handle = handle;
        OriginalCount = count;
        CanEditCount = canEditCount && count is not null;
        _countInput = count?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CountDisplay = count is > 1 ? $"× {count.Value}" : string.Empty;
    }

    public string Name { get; }

    public uint Handle { get; }

    public uint? OriginalCount { get; }

    public bool CanEditCount { get; }

    public string CountInput
    {
        get => _countInput;
        set => SetProperty(ref _countInput, value);
    }

    public string CountDisplay { get; }
}
