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
                var upgVm = new UpgradeItemViewModel(key, UpgradeName(key), L.T("Установленный апгрейд"), isInstalled: true, canEditUpgrades, UpgradesDisabledReason);
                upgVm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(UpgradeItems));
                upgradeViewModels.Add(upgVm);
            }
        }
        UpgradeItems = new ObservableCollection<UpgradeItemViewModel>(upgradeViewModels);
    }

    private static string WeaponUpgradeName(StalkerSaveEditor.Core.Catalogs.Stalker2WeaponUpgrade upgrade)
    {
        var part = upgrade.Part switch
        {
            "Barrel" => L.T("Ствол"),
            "Handguard" => L.T("Цевьё"),
            "Body" => L.T("Ствольная коробка"),
            "PistolGrip" => L.T("Рукоять"),
            "Stock" => L.T("Приклад"),
            _ => upgrade.Part,
        };
        var effect = upgrade.Effect switch
        {
            "Recoil" => L.T("отдача"),
            "AimingSpeed" => L.T("скорость прицеливания"),
            "AimingAccuracy" => L.T("точность прицеливания"),
            "MaxSpread" => L.T("максимальный разброс"),
            "SpreadReduction" => L.T("снижение разброса"),
            "SlowingSpread" => L.T("разброс при стрельбе очередью"),
            "AttachmentSystem" => L.T("крепление обвеса"),
            "MovementAiming" => L.T("прицеливание в движении"),
            "ArmorPiercing" => L.T("бронебойность"),
            "Depreciation" => L.T("износ"),
            "ShootingDepreciation" => L.T("износ при стрельбе"),
            "Falloff" => L.T("падение урона с расстоянием"),
            "Range" => L.T("дальность"),
            "DropDamage" => L.T("урон"),
            "Readiness" => L.T("скорость готовности"),
            "Velocity" => L.T("скорость пули"),
            "CaliberChange" => L.T("смена калибра"),
            "AimingReturn" => L.T("возврат прицела"),
            "WeaponGrip" => L.T("хват"),
            "Autosh" => L.T("автоматический огонь"),
            _ => upgrade.Effect,
        };
        return $"{part}: {effect}";
    }

    /// <summary>S2 armour upgrades read as "effect · tier" (from the localization key); anything else keeps its key.</summary>
    internal static string UpgradeName(string key)
    {
        if (StalkerSaveEditor.Core.Catalogs.Stalker2ArmorUpgrades.FindWeapon(key) is { } weapon) return WeaponUpgradeName(weapon);
        if (StalkerSaveEditor.Core.Catalogs.Stalker2ArmorUpgrades.Find(key) is not { } upgrade) return key;
        var effect = upgrade.Effect switch
        {
            "rad" => L.T("Радиационная защита"),
            "psy" => L.T("Пси-защита"),
            "rad_psy" => L.T("Защита от радиации и пси"),
            "fire" => L.T("Термозащита"),
            "chem" => L.T("Химзащита"),
            "electr" => L.T("Электрозащита"),
            "fire_chem" => L.T("Термо- и химзащита"),
            "fire_electr" => L.T("Термо- и электрозащита"),
            "electr_chem" => L.T("Электро- и химзащита"),
            "el_chem_fire" => L.T("Защита от аномалий"),
            "bp" => L.T("Пулестойкость"),
            "exo_bp" => L.T("Пулестойкость экзоскелета"),
            "durable" => L.T("Прочность"),
            "exo_durable" => L.T("Прочность экзоскелета"),
            "weight" => L.T("Переносимый вес"),
            "pockets" => L.T("Карманы"),
            "backpack" => L.T("Рюкзак"),
            "container" => L.T("Контейнеры для артефактов"),
            "container_radconsume" => L.T("Контейнеры: поглощение радиации"),
            "stamina" => L.T("Выносливость"),
            "exo_stamina" => L.T("Выносливость экзоскелета"),
            "exo_sprint" => L.T("Бег в экзоскелете"),
            "exo_hands" => L.T("Сервоприводы рук"),
            _ => upgrade.Effect,
        };
        return L.T("{0} · ур. {1}", effect, upgrade.Tier);
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
                OnPropertyChanged(nameof(ShowPlacementBadge));
            }
        }
    }

    public string PlacementDisplay => _placement switch
    {
        "slot" or "carried" => L.T("Слот"),
        "belt" => L.T("Пояс"),
        "ruck" or "inventory" => L.T("Рюкзак"),
        "equipped" => L.T("Надето"),
        _ => _placement,
    };

    /// <summary>The row shows where the item is only when it is not simply in the backpack.</summary>
    public bool ShowPlacementBadge => _placement is not ("ruck" or "inventory");

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
