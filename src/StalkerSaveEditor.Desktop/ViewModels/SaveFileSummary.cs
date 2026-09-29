using StalkerSaveEditor.Desktop.Services;
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
            ? L.T("Изменить баланс сталкера")
            : moneyDisabledReason ?? L.T("Редактирование денег не поддерживается данным форматом");
        FileSizeBytes = fileSizeBytes;
        LastModified = lastModified;

        ActorName = string.IsNullOrWhiteSpace(actorName) ? "—" : actorName;
        ActorHealth = actorHealth;
        ActorRank = actorRank;
        ActorReputation = actorReputation;
        GameTime = gameTime;
        TimeFactor = timeFactor;
        PlayerFaction = playerFaction;

        CanEditFaction = canEditFaction;
        FactionDisabledReason = canEditFaction
            ? L.T("Редактировать отношения с группировками")
            : factionDisabledReason ?? L.T("Редактирование отношений фракций не поддерживается данным форматом");

        CanEditUpgrades = canEditUpgrades;
        UpgradesDisabledReason = canEditUpgrades
            ? L.T("Редактировать апгрейды")
            : upgradesDisabledReason ?? L.T("Модификации оружия и брони не поддерживаются форматом");

        CanEditDurability = canEditDurability;
        DurabilityDisabledReason = canEditDurability
            ? L.T("Редактировать состояние предметов")
            : durabilityDisabledReason ?? L.T("Редактирование прочности не поддерживается форматом");

        CanEditPlacement = canEditPlacement;
        PlacementDisabledReason = canEditPlacement
            ? L.T("Перемещать предметы")
            : placementDisabledReason ?? L.T("Перемещение предметов не поддерживается данным форматом");

        CanEditStashes = canEditStashes;
        StashesDisabledReason = canEditStashes
            ? L.T("Переместить хабар из тайников")
            : stashesDisabledReason ?? L.T("В сохранении нет тайников с предметами или операция не поддерживается");

        CanAddItems = canAddItems;
        AddItemsDisabledReason = canAddItems
            ? L.T("Добавить предмет из каталога в инвентарь")
            : addItemsDisabledReason ?? L.T("Добавление предметов не поддерживается данным форматом");

        CanRemoveItems = canRemoveItems;
        RemoveItemsDisabledReason = canRemoveItems
            ? L.T("Удалить выбранный предмет из инвентаря")
            : removeItemsDisabledReason ?? L.T("Удаление предметов не поддерживается данным форматом");

        CrcOk = crcOk;

        Inventory = new ObservableCollection<InventoryLineViewModel>(inventory);
        Stashes = new ObservableCollection<StashViewModel>(stashes ?? []);
        Transitions = new ObservableCollection<TransitionViewModel>(transitions ?? []);
        FactionRelations = new ObservableCollection<FactionRelationViewModel>(factionRelations ?? []);
    }

    public string FilePath { get; }

    private bool _previewLoaded;
    private Avalonia.Media.Imaging.Bitmap? _preview;
    private string? _slotTitle;

    /// <summary>The game's own screenshot of this slot (X-Ray .dds / S2 thumbnail), decoded on first use; null when absent.</summary>
    public Avalonia.Media.Imaging.Bitmap? Preview
    {
        get
        {
            if (_previewLoaded) return _preview;
            _previewLoaded = true;
            try
            {
                if (StalkerSaveEditor.Core.Inspection.SavePreviewReader.Preview(FilePath, IsStalker2) is { } image)
                {
                    using var stream = new MemoryStream(image);
                    _preview = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 96);
                }
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                _preview = null;
            }

            return _preview;
        }
    }

    public bool HasPreview => Preview is not null;

    /// <summary>Second line in the library: S2 region and play time from the campaign index, else the X-Ray level.</summary>
    public string SlotTitle => _slotTitle ??= BuildSlotTitle();

    private bool IsStalker2 => ReleaseId.StartsWith("stalker2", StringComparison.Ordinal);

    private string BuildSlotTitle()
    {
        if (IsStalker2)
        {
            try
            {
                if (StalkerSaveEditor.Core.Inspection.SavePreviewReader.Stalker2Slot(FilePath) is { } slot)
                {
                    var region = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slot.RegionSlug.Replace('_', ' '));
                    return L.T("{0} · {1:0.#} ч", region, slot.PlayHours);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }

            return ReleaseName;
        }

        return LastModified is { } written ? $"{ReleaseName} · {written.ToString("g", CultureInfo.CurrentCulture)}" : ReleaseName;
    }
    public string DisplayName { get; }
    public string ReleaseName { get; }
    public string ReleaseId { get; }
    public string SourceSha256 { get; }
    public uint Money { get; }
    public bool CanEditMoney { get; }
    public string MoneyDisabledReason { get; }
    public long FileSizeBytes { get; }
    public DateTime? LastModified { get; }

    public string ActorName { get; }
    public float? ActorHealth { get; }
    public int? ActorRank { get; }
    public int? ActorReputation { get; }
    public ulong? GameTime { get; }

    /// <summary>PDA tasks and actor statistics (X-Ray only); null when the registries could not be read.</summary>
    public StalkerSaveEditor.Core.Formats.XRay.XRayProgress? Progress { get; init; }

    public string TasksDisplay => Progress is { Tasks.Count: > 0 } progress
        ? L.T("{0} выполнено · {1} активно · {2} провалено",
            progress.Tasks.Count(task => task.State == StalkerSaveEditor.Core.Formats.XRay.XRayTaskState.Completed),
            progress.Tasks.Count(task => task.State == StalkerSaveEditor.Core.Formats.XRay.XRayTaskState.InProgress),
            progress.Tasks.Count(task => task.State == StalkerSaveEditor.Core.Formats.XRay.XRayTaskState.Failed))
        : "—";

    public StalkerSaveEditor.Core.Formats.XRay.XRayWeather? Weather { get; init; }

    public string WeatherDisplay => Weather is { } weather
        ? L.T("{0} → {1}", WeatherName(weather.Current), WeatherName(weather.Next))
        : "—";

    private static string WeatherName(string state) => state switch
    {
        "clear" => L.T("ясно"),
        "cloudy" or "pasmurno" => L.T("облачно"),
        "rain" => L.T("дождь"),
        "thunder" or "groza" or "storm" => L.T("гроза"),
        "foggy" => L.T("туман"),
        _ => state,
    };

    public string KillsDisplay => Progress is { Statistics.Count: > 0 } progress
        ? L.T("сталкеров {0} · мутантов {1}", progress.Count("stalkerkills"), progress.Count("monsterkills"))
        : "—";

    /// <summary>Level-changer destinations the actor can be moved to (TP, experimental); empty when unsupported.</summary>
    public IReadOnlyList<RelocationAnchorViewModel> RelocationAnchors { get; init; } = [];

    public StalkerSaveEditor.Core.Formats.XRay.XRayActorLocation? ActorLocation { get; init; }

    public bool CanRelocate => RelocationAnchors.Count > 0;

    public string ActorLocationDisplay => ActorLocation is { } location
        ? L.T("X {0:0.0} · Y {1:0.0} · Z {2:0.0} · вершина {3}", location.Position.X, location.Position.Y, location.Position.Z, location.GameVertexId)
        : "—";

    public float? TimeFactor { get; }
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
    public bool HasTransitions => Transitions.Count > 0;
    public bool HasNoTransitions => Transitions.Count == 0;
    public ObservableCollection<FactionRelationViewModel> FactionRelations { get; }

    public string FileSizeDisplay => FileSizeBytes switch
    {
        > 1024 * 1024 => L.T("{0:F1} МБ", (double)FileSizeBytes / (1024 * 1024)),
        > 1024 => L.T("{0:F0} КБ", (double)FileSizeBytes / 1024),
        _ => L.T("{0} Б", FileSizeBytes),
    };

    public string LastModifiedDisplay => LastModified.HasValue
        ? LastModified.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
        : "—";

    public string MoneyDisplay => CanEditMoney || Money > 0 ? Money.ToString("N0", CultureInfo.CurrentCulture) + " RU" : "—";

    public string GameTimeDisplay
    {
        get
        {
            if (!GameTime.HasValue) return "—";
            // X-Ray game time: milliseconds since 01.01.0001 (the in-game calendar date).
            if (GameTime.Value / 1000 / 86400 < 3_650_000)
            {
                var moment = new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMilliseconds(GameTime.Value);
                if (moment.Year is >= 1990 and <= 2100) return moment.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            }

            return "—";
        }
    }

    public string HealthDisplay => ActorHealth.HasValue ? $"{(int)(ActorHealth.Value * 100)}%" : "—";

    public string RankDisplay => ActorRank.HasValue ? ActorRank.Value switch
    {
        >= 900 => L.T("Мастер"),
        >= 600 => L.T("Ветеран"),
        >= 300 => L.T("Опытный"),
        _ => L.T("Новичок"),
    } : "—";

    public string ReputationDisplay => ActorReputation.HasValue ? ActorReputation.Value switch
    {
        >= 100 => L.T("Отличная"),
        >= 20 => L.T("Хорошая"),
        >= -20 => L.T("Нейтральная"),
        >= -100 => L.T("Плохая"),
        _ => L.T("Очень плохая"),
    } : "—";
}

public sealed record RelocationAnchorViewModel(StalkerSaveEditor.Core.Formats.XRay.XRayRelocationAnchor Anchor)
{
    public string Display => $"{Anchor.DestinationLevel} · {Anchor.DestinationPoint}";
}
