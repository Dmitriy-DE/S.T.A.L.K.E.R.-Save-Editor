using System.Collections.ObjectModel;
using System.Globalization;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class SaveLibraryViewModel : ObservableViewModel
{
    private static readonly IReadOnlyDictionary<string, CatalogBundle> Catalogs = CatalogBundleReader.LoadEmbedded();
    private static readonly OfficialNamesCatalog OfficialNames = OfficialNamesCatalog.LoadEmbedded();
    private SaveFileSummary? _selectedSave;

    public SaveLibraryViewModel(bool discoverLocalSaves = true)
    {
        RefreshCommand = new RelayCommand(Refresh);
        if (discoverLocalSaves) Refresh();
    }

    public ObservableCollection<SaveFileSummary> Saves { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public SaveFileSummary? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (!SetProperty(ref _selectedSave, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            OnPropertyChanged(nameof(SelectedSaveName));
            OnPropertyChanged(nameof(SelectedReleaseName));
            OnPropertyChanged(nameof(SelectedMoneyDisplay));
            OnPropertyChanged(nameof(SelectedInventory));
        }
    }

    public bool HasSelection => SelectedSave is not null;

    public bool HasNoSelection => SelectedSave is null;

    public string SelectedSaveName => SelectedSave?.DisplayName ?? string.Empty;

    public string SelectedReleaseName => SelectedSave?.ReleaseName ?? string.Empty;

    public string SelectedMoneyDisplay => SelectedSave is null
        ? string.Empty
        : $"Деньги: {SelectedSave.Money.ToString("N0", CultureInfo.CurrentCulture)}";

    public IReadOnlyList<InventoryLineViewModel> SelectedInventory => SelectedSave?.Inventory ?? [];

    public void Refresh()
    {
        Saves.Clear();
        foreach (var path in EnumerateSaveFiles(SaveDirectoryDiscovery.GetExistingDirectories()))
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
                files = Directory.EnumerateFiles(directory, "*.sav", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MaxRecursionDepth = 4,
                }).ToArray();
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

            try
            {
                return FromXRay(XRayTrilogyReader.FromBytes(bytes), Path.GetFileName(path));
            }
            catch (XRayFormatException)
            {
                // Enhanced Edition has separate ALIFE versions and markers.
            }

            try
            {
                return FromXRay(XRayEnhancedReader.FromBytes(bytes), Path.GetFileName(path));
            }
            catch (XRayFormatException)
            {
                // Continue to the S.T.A.L.K.E.R. 2 container reader.
            }

            try
            {
                return FromStalker2(Stalker2SaveReader.FromBytes(bytes), Path.GetFileName(path));
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

    private static SaveFileSummary FromXRay(XRayTrilogySave save, string fileName)
    {
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            OfficialNames.Resolve(save.FormatId, "items", item.TypeKey, CultureInfo.CurrentUICulture.Name) ?? item.TypeKey,
            item.Count is > 1 ? $"× {item.Count.Value}" : string.Empty));
        return new SaveFileSummary(
            fileName,
            ReleaseName(save.FormatId),
            save.Money,
            inventory);
    }

    private static SaveFileSummary FromStalker2(Stalker2Save save, string fileName)
    {
        var catalog = Catalogs.TryGetValue("stalker2", out var bundle) ? bundle.Items : null;
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            item.DisplayName ?? catalog?.Resolve(item.TypeKey)?.DisplayName ?? item.TypeKey,
            item.Count > 1 ? $"× {item.Count}" : string.Empty));
        return new SaveFileSummary(fileName, "S.T.A.L.K.E.R. 2", save.Money, inventory);
    }

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
    string displayName,
    string releaseName,
    uint money,
    IEnumerable<InventoryLineViewModel> inventory)
{
    public string DisplayName { get; } = displayName;

    public string ReleaseName { get; } = releaseName;

    public uint Money { get; } = money;

    public IReadOnlyList<InventoryLineViewModel> Inventory { get; } = Array.AsReadOnly(inventory.ToArray());
}

public sealed class InventoryLineViewModel(string name, string countDisplay)
{
    public string Name { get; } = name;

    public string CountDisplay { get; } = countDisplay;
}
