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
        string? upgradesDisabledReason = null)
    {
        Name = name;
        TypeKey = typeKey;
        Handle = handle;
        Category = NormalizeCategory(category);
        OriginalCount = count;
        CanEditCount = canEditCount && count is not null;
        CountDisabledReason = CanEditCount
            ? "Изменить количество предметов в пачке"
            : count is null
                ? "Предмет не стакается (уникальный или штучный объект)"
                : countDisabledReason ?? "Редактирование количества предметов не поддерживается форматом";
        _countInput = count?.ToString(CultureInfo.InvariantCulture) ?? "1";

        OriginalCondition = condition;
        CanEditCondition = canEditCondition && condition is not null;
        ConditionDisabledReason = CanEditCondition
            ? "Изменить состояние предмета (0–100%)"
            : condition is null
                ? "Предмет не имеет шкалы состояния / износа"
                : conditionDisabledReason ?? "Редактирование прочности не поддерживается форматом";
        _conditionPercent = condition.HasValue ? (int)Math.Round(condition.Value * 100f) : 100;

        _placement = placement ?? "ruck";
        OriginalPlacement = placement ?? "ruck";
        CanEditPlacement = canEditPlacement;
        PlacementDisabledReason = CanEditPlacement
            ? "Переместить предмет (слот / пояс / рюкзак)"
            : placementDisabledReason ?? "Перемещение предметов не поддерживается данным форматом";

        OriginalUpgrades = upgrades ?? [];
        CanEditUpgrades = canEditUpgrades;
        UpgradesDisabledReason = CanEditUpgrades
            ? "Установить или снять апгрейд"
            : upgradesDisabledReason ?? "Модификации оружия и брони не поддерживаются форматом";

        var availableList = availableUpgrades?.ToList() ?? [];
        var upgradeViewModels = new List<UpgradeItemViewModel>();
        foreach (var def in availableList)
        {
            var isInstalled = OriginalUpgrades.Contains(def.Key, StringComparer.Ordinal);
            upgradeViewModels.Add(new UpgradeItemViewModel(def, isInstalled, canEditUpgrades, UpgradesDisabledReason));
        }
        UpgradeItems = new ObservableCollection<UpgradeItemViewModel>(upgradeViewModels);
    }

    public string Name { get; }
    public string TypeKey { get; }
    public uint Handle { get; }
    public string Category { get; }
    public uint? OriginalCount { get; }
    public bool CanEditCount { get; }
    public string CountDisabledReason { get; }

    public float? OriginalCondition { get; }
    public bool CanEditCondition { get; }
    public string ConditionDisabledReason { get; }

    public string OriginalPlacement { get; }
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
        "slot" => "Слот",
        "belt" => "Пояс",
        "ruck" => "Рюкзак",
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
