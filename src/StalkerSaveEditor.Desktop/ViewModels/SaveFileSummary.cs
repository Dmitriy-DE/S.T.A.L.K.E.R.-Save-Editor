using System.Collections.ObjectModel;
using System.Globalization;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class SaveFileSummary
{
    public SaveFileSummary(
        string filePath,
        string releaseName,
        string releaseId,
        string sourceSha256,
        uint money,
        bool canEditMoney,
        IEnumerable<InventoryLineViewModel> inventory,
        long fileSizeBytes = 0,
        DateTime? lastModified = null,
        string? actorName = null,
        float? actorHealth = null,
        int? actorRank = null,
        int? actorReputation = null,
        ulong? gameTime = null,
        float? timeFactor = null,
        string? levelName = null,
        string? playerFaction = null,
        bool canEditFaction = false,
        bool canEditUpgrades = false,
        bool canEditDurability = false,
        bool canEditPlacement = false,
        bool canEditStashes = false,
        bool canAddItems = false,
        bool canRemoveItems = false,
        bool crcOk = true,
        IEnumerable<StashViewModel>? stashes = null,
        IEnumerable<TransitionViewModel>? transitions = null,
        IEnumerable<FactionRelationViewModel>? factionRelations = null,
        string? moneyDisabledReason = null,
        string? factionDisabledReason = null,
        string? upgradesDisabledReason = null,
        string? durabilityDisabledReason = null,
        string? placementDisabledReason = null,
        string? stashesDisabledReason = null,
        string? addItemsDisabledReason = null,
        string? removeItemsDisabledReason = null)
    {
        FilePath = Path.GetFullPath(filePath);
        DisplayName = Path.GetFileName(filePath);
        ReleaseName = releaseName;
        ReleaseId = releaseId;
        SourceSha256 = sourceSha256;
        Money = money;
        CanEditMoney = canEditMoney;
        MoneyDisabledReason = canEditMoney
            ? "Изменить баланс сталкера"
            : moneyDisabledReason ?? "Редактирование денег не поддерживается данным форматом";
        FileSizeBytes = fileSizeBytes;
        LastModified = lastModified;

        ActorName = actorName ?? "Дегтярёв / Сталкер";
        ActorHealth = actorHealth;
        ActorRank = actorRank;
        ActorReputation = actorReputation;
        GameTime = gameTime;
        TimeFactor = timeFactor;
        LevelName = levelName ?? "Зона";
        PlayerFaction = playerFaction;

        CanEditFaction = canEditFaction;
        FactionDisabledReason = canEditFaction
            ? "Редактировать отношения с группировками"
            : factionDisabledReason ?? "Редактирование отношений фракций не поддерживается данным форматом";

        CanEditUpgrades = canEditUpgrades;
        UpgradesDisabledReason = canEditUpgrades
            ? "Редактировать апгрейды"
            : upgradesDisabledReason ?? "Модификации оружия и брони не поддерживаются форматом";

        CanEditDurability = canEditDurability;
        DurabilityDisabledReason = canEditDurability
            ? "Редактировать состояние предметов"
            : durabilityDisabledReason ?? "Редактирование прочности не поддерживается форматом";

        CanEditPlacement = canEditPlacement;
        PlacementDisabledReason = canEditPlacement
            ? "Перемещать предметы"
            : placementDisabledReason ?? "Перемещение предметов не поддерживается данным форматом";

        CanEditStashes = canEditStashes;
        StashesDisabledReason = canEditStashes
            ? "Переместить хабар из тайников"
            : stashesDisabledReason ?? "В сохранении нет тайников с предметами или операция не поддерживается";

        CanAddItems = canAddItems;
        AddItemsDisabledReason = canAddItems
            ? "Добавить предмет из каталога в инвентарь"
            : addItemsDisabledReason ?? "Добавление предметов не поддерживается данным форматом";

        CanRemoveItems = canRemoveItems;
        RemoveItemsDisabledReason = canRemoveItems
            ? "Удалить выбранный предмет из инвентаря"
            : removeItemsDisabledReason ?? "Удаление предметов не поддерживается данным форматом";

        CrcOk = crcOk;

        Inventory = new ObservableCollection<InventoryLineViewModel>(inventory);
        Stashes = new ObservableCollection<StashViewModel>(stashes ?? []);
        Transitions = new ObservableCollection<TransitionViewModel>(transitions ?? []);
        FactionRelations = new ObservableCollection<FactionRelationViewModel>(factionRelations ?? []);
    }

    public string FilePath { get; }
    public string DisplayName { get; }
    public string ReleaseName { get; }
    public string ReleaseId { get; }
    public string SourceSha256 { get; }
    public uint Money { get; }
    public bool CanEditMoney { get; }
    public string MoneyDisabledReason { get; }
    public long FileSizeBytes { get; }
    public DateTime? LastModified { get; }

    public string? ActorName { get; }
    public float? ActorHealth { get; }
    public int? ActorRank { get; }
    public int? ActorReputation { get; }
    public ulong? GameTime { get; }
    public float? TimeFactor { get; }
    public string LevelName { get; }
    public string? PlayerFaction { get; }

    public bool CanEditFaction { get; }
    public string FactionDisabledReason { get; }
    public bool CanEditUpgrades { get; }
    public string UpgradesDisabledReason { get; }
    public bool CanEditDurability { get; }
    public string DurabilityDisabledReason { get; }
    public bool CanEditPlacement { get; }
    public string PlacementDisabledReason { get; }
    public bool CanEditStashes { get; }
    public string StashesDisabledReason { get; }
    public bool CanAddItems { get; }
    public string AddItemsDisabledReason { get; }
    public bool CanRemoveItems { get; }
    public string RemoveItemsDisabledReason { get; }
    public bool CrcOk { get; }

    public ObservableCollection<InventoryLineViewModel> Inventory { get; }
    public ObservableCollection<StashViewModel> Stashes { get; }
    public ObservableCollection<TransitionViewModel> Transitions { get; }
    public ObservableCollection<FactionRelationViewModel> FactionRelations { get; }

    public string FileSizeDisplay => FileSizeBytes switch
    {
        > 1024 * 1024 => $"{(double)FileSizeBytes / (1024 * 1024):F1} МБ",
        > 1024 => $"{(double)FileSizeBytes / 1024:F0} КБ",
        _ => $"{FileSizeBytes} Б",
    };

    public string LastModifiedDisplay => LastModified.HasValue
        ? LastModified.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
        : "—";

    public string GameTimeDisplay
    {
        get
        {
            if (!GameTime.HasValue) return "—";
            // X-Ray game time in milliseconds:
            var totalSeconds = GameTime.Value / 1000.0;
            var days = (int)(totalSeconds / 86400);
            var hours = (int)((totalSeconds % 86400) / 3600);
            var minutes = (int)((totalSeconds % 3600) / 60);
            return days > 0 ? $"День {days + 1}, {hours:D2}:{minutes:D2}" : $"{hours:D2}:{minutes:D2}";
        }
    }

    public string HealthDisplay => ActorHealth.HasValue ? $"{(int)(ActorHealth.Value * 100)}%" : "—";

    public string RankDisplay => ActorRank.HasValue ? ActorRank.Value switch
    {
        >= 900 => "Мастер",
        >= 600 => "Ветеран",
        >= 300 => "Опытный",
        _ => "Новичок",
    } : "Опытный";

    public string ReputationDisplay => ActorReputation.HasValue ? ActorReputation.Value switch
    {
        >= 100 => "Отличная",
        >= 20 => "Хорошая",
        >= -20 => "Нейтральная",
        >= -100 => "Плохая",
        _ => "Очень плохая",
    } : "Нейтральная";
}
