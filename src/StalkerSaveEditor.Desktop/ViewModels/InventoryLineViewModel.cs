using StalkerSaveEditor.Desktop.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class InventoryLineViewModel : ObservableViewModel
{
    private string _countInput;
    private int _conditionPercent;
    private string _placement;
    private bool _isDeleted;

    public InventoryLineViewModel(
        string name,
        string typeKey,
        uint handle,
        string category,
        uint? count,
        bool canEditCount,
        float? condition,
        bool canEditCondition,
        string? placement,
        bool canEditPlacement,
        IReadOnlyList<string>? upgrades,
        bool canEditUpgrades,
        IEnumerable<UpgradeDefinition>? availableUpgrades = null,
        string? countDisabledReason = null,
        string? conditionDisabledReason = null,
        string? placementDisabledReason = null,
        string? upgradesDisabledReason = null,
        int? baseSlot = null,
        string releaseId = "",
        string? iconKey = null)
    {
        ReleaseId = releaseId;
        IconKey = iconKey ?? typeKey;
        BaseSlot = baseSlot;
        Name = name;
        TypeKey = typeKey;
        Handle = handle;
        Category = NormalizeCategory(category);
        OriginalCount = count;
        CanEditCount = canEditCount && count is not null;
        CountDisabledReason = CanEditCount
            ? L.T("Изменить количество предметов в пачке")
            : count is null
                ? L.T("Предмет не стакается (уникальный или штучный объект)")
                : countDisabledReason ?? L.T("Редактирование количества предметов не поддерживается форматом");
        _countInput = count?.ToString(CultureInfo.InvariantCulture) ?? "1";

        OriginalCondition = condition;
        CanEditCondition = canEditCondition && condition is not null;
        ConditionDisabledReason = CanEditCondition
            ? L.T("Изменить состояние предмета (0–100%)")
            : condition is null
                ? L.T("Предмет не имеет шкалы состояния / износа")
                : conditionDisabledReason ?? L.T("Редактирование прочности не поддерживается форматом");
        _conditionPercent = condition.HasValue ? (int)Math.Round(condition.Value * 100f) : 100;

        _placement = placement ?? "ruck";
        OriginalPlacement = placement ?? "ruck";
        CanEditPlacement = canEditPlacement;
        PlacementDisabledReason = CanEditPlacement
            ? L.T("Переместить предмет (слот / пояс / рюкзак)")
            : placementDisabledReason ?? L.T("Перемещение предметов не поддерживается данным форматом");

        OriginalUpgrades = upgrades ?? [];
        CanEditUpgrades = canEditUpgrades;
        UpgradesDisabledReason = CanEditUpgrades
            ? L.T("Установить или снять апгрейд")
            : upgradesDisabledReason ?? L.T("Модификации оружия и брони не поддерживаются форматом");

        var availableList = availableUpgrades?.ToList() ?? [];
        var upgradeViewModels = new List<UpgradeItemViewModel>();
        foreach (var def in availableList)
        {
            var isInstalled = OriginalUpgrades.Contains(def.Key, StringComparer.Ordinal);
            var upgVm = new UpgradeItemViewModel(def, isInstalled, canEditUpgrades, UpgradesDisabledReason);
            upgVm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(UpgradeItems));
            upgradeViewModels.Add(upgVm);
        }
        foreach (var key in OriginalUpgrades)
        {
            if (upgradeViewModels.All(u => u.Key != key))
            {
                var upgVm = new UpgradeItemViewModel(key, key, L.T("Установленный апгрейд"), isInstalled: true, canEditUpgrades, UpgradesDisabledReason);
                upgVm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(UpgradeItems));
                upgradeViewModels.Add(upgVm);
            }
        }
        UpgradeItems = new ObservableCollection<UpgradeItemViewModel>(upgradeViewModels);
    }

    /// <summary>Installed upgrades differ from the save as a set (the save keeps install order, the UI tree order).</summary>
    public bool UpgradesChanged =>
        CanEditUpgrades && HasUpgrades &&
        !UpgradeItems.Where(u => u.IsInstalled).Select(u => u.Key).ToHashSet(StringComparer.Ordinal).SetEquals(OriginalUpgrades);

    /// <summary>Upgrades to write: the save's own order for kept ones, newly installed ones appended (install order).</summary>
    public List<string> UpgradesToWrite()
    {
        var installed = UpgradeItems.Where(u => u.IsInstalled).Select(u => u.Key).ToList();
        var kept = OriginalUpgrades.Where(key => installed.Contains(key, StringComparer.Ordinal)).ToList();
        kept.AddRange(installed.Where(key => !kept.Contains(key, StringComparer.Ordinal)));
        return kept;
    }

    public string Name { get; }
    public string TypeKey { get; }
    public string ReleaseId { get; }

    /// <summary>Key for the icon lookup: the X-Ray section or the S2 SID.</summary>
    public string IconKey { get; }

    public uint Handle { get; }
    public string Category { get; }
    public uint? OriginalCount { get; }
    public bool CanEditCount { get; }
    public string CountDisabledReason { get; }

    public float? OriginalCondition { get; }
    public bool CanEditCondition { get; }
    public string ConditionDisabledReason { get; }

    public string OriginalPlacement { get; }
    public int? BaseSlot { get; }
    public bool CanEditPlacement { get; }
    public string PlacementDisabledReason { get; }

    public IReadOnlyList<string> OriginalUpgrades { get; }
    public bool CanEditUpgrades { get; }
    public string UpgradesDisabledReason { get; }
    public ObservableCollection<UpgradeItemViewModel> UpgradeItems { get; }

    public string CountInput
    {
        get => _countInput;
        set
        {
            if (SetProperty(ref _countInput, value))
            {
                OnPropertyChanged(nameof(CountDisplay));
            }
        }
    }

    public string CountDisplay
    {
        get
        {
            if (uint.TryParse(_countInput, NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c > 1)
            {
                return $"× {c}";
            }
            return OriginalCount is > 1 ? $"× {OriginalCount.Value}" : string.Empty;
        }
    }

    public int ConditionPercent
    {
        get => _conditionPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _conditionPercent, clamped))
            {
                OnPropertyChanged(nameof(ConditionDisplay));
                OnPropertyChanged(nameof(ConditionColor));
                OnPropertyChanged(nameof(ConditionFraction));
            }
        }
    }

    public float ConditionFraction => _conditionPercent / 100f;

    public string ConditionDisplay => OriginalCondition.HasValue ? $"{_conditionPercent}%" : "—";

    public string ConditionColor => _conditionPercent switch
    {
        >= 75 => "#7BCB62",
        >= 40 => "#D6A62D",
        _ => "#D85A45",
    };

    public string Placement
    {
        get => _placement;
        set
        {
            if (SetProperty(ref _placement, value))
            {
                OnPropertyChanged(nameof(PlacementDisplay));
            }
        }
    }

    public string PlacementDisplay => _placement switch
    {
        "slot" => L.T("Слот"),
        "belt" => L.T("Пояс"),
        "ruck" => L.T("Рюкзак"),
        _ => _placement,
    };

    public bool IsDeleted
    {
        get => _isDeleted;
        set => SetProperty(ref _isDeleted, value);
    }

    public bool HasUpgrades => UpgradeItems.Count > 0;

    private static string NormalizeCategory(string category) => category.ToLowerInvariant() switch
    {
        "weapon" or "weapon_magazined" or "weapon_shotgun" or "weapon_wgl" => "weapon",
        "ammo" => "ammo",
        "outfit" or "armor" or "helmet" => "armor",
        "consumable" or "medkit" or "food" or "drink" => "consumable",
        "artefact" or "artifact" => "artifact",
        "quest" or "document" or "pda" or "key" => "quest",
        _ => "other",
    };
}
