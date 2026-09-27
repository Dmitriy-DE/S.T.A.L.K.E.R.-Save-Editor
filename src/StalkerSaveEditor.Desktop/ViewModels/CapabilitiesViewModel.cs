using Avalonia.Media;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Desktop.Styles;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class CapabilityFormatColumnViewModel
{
    public CapabilityFormatColumnViewModel(
        string id,
        string shortName,
        string fullName,
        string edition)
    {
        Id = id;
        ShortName = shortName;
        FullName = fullName;
        Edition = edition;
    }

    public string Id { get; }
    public string ShortName { get; }
    public string FullName { get; }
    public string Edition { get; }
}

public sealed class CapabilityCellViewModel
{
    public CapabilityCellViewModel(
        string formatId,
        string capabilityKey,
        CapabilityMaturity maturity,
        bool writable,
        string? reason,
        bool isSafetyRestricted = false)
    {
        FormatId = formatId;
        CapabilityKey = capabilityKey;
        Maturity = maturity;
        Writable = writable && !isSafetyRestricted;
        Reason = reason;

        if (isSafetyRestricted)
        {
            StatusText = "Блок UI";
            BadgeBackground = new SolidColorBrush(Color.Parse("#2B2215"));
            BadgeForeground = StalkerTheme.BrushAccentAmber;
            TooltipText = "Запись для данного формата выключена в UI до верификации мутаций в живой игре.";
        }
        else
        {
            switch (maturity)
            {
                case CapabilityMaturity.Verified:
                    StatusText = "Запись";
                    BadgeBackground = new SolidColorBrush(Color.Parse("#1A2E1A"));
                    BadgeForeground = StalkerTheme.BrushSuccess;
                    TooltipText = reason ?? "Подтверждено и верифицировано в игре.";
                    break;
                case CapabilityMaturity.Experimental:
                    StatusText = "Эксперим.";
                    BadgeBackground = new SolidColorBrush(Color.Parse("#332A15"));
                    BadgeForeground = StalkerTheme.BrushWarning;
                    TooltipText = reason ?? "Экспериментальная поддержка (требуется проверка в игре).";
                    break;
                case CapabilityMaturity.Research:
                    StatusText = "Чтение";
                    BadgeBackground = new SolidColorBrush(Color.Parse("#16283B"));
                    BadgeForeground = new SolidColorBrush(Color.Parse("#64B5F6"));
                    TooltipText = reason ?? "Исследование / режим только для чтения.";
                    break;
                default:
                    StatusText = "Нет";
                    BadgeBackground = new SolidColorBrush(Color.Parse("#1E201C"));
                    BadgeForeground = StalkerTheme.BrushTextMuted;
                    TooltipText = reason ?? "Не поддерживается движком или форматом сохранения.";
                    break;
            }
        }
    }

    public string FormatId { get; }
    public string CapabilityKey { get; }
    public CapabilityMaturity Maturity { get; }
    public bool Writable { get; }
    public string StatusText { get; }
    public string? Reason { get; }
    public string TooltipText { get; }
    public IBrush BadgeBackground { get; }
    public IBrush BadgeForeground { get; }
}

public sealed class CapabilityRowViewModel
{
    public CapabilityRowViewModel(
        string key,
        string name,
        string description,
        IReadOnlyList<CapabilityCellViewModel> cells)
    {
        Key = key;
        Name = name;
        Description = description;
        Cells = cells;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<CapabilityCellViewModel> Cells { get; }
}

public sealed class CapabilitiesViewModel : ObservableViewModel
{
    private string? _selectedFormatId;

    public CapabilitiesViewModel(ICapabilityService? capabilityService = null)
    {
        var service = capabilityService ?? CapabilityService.Default;
        OracleRevision = (service as CapabilityService)?.OracleRevision ?? CapabilityService.Default.OracleRevision;

        Columns =
        [
            new("stalker-soc", "ТЧ", "Тень Чернобыля", "X-Ray 1.0"),
            new("stalker-cs", "ЧН", "Чистое Небо", "X-Ray 1.5"),
            new("stalker-cop", "ЗП", "Зов Припяти", "X-Ray 1.6"),
            new("stalker-soc-ee", "ТЧ EE", "Тень Чернобыля EE", "Enhanced"),
            new("stalker-cs-ee", "ЧН EE", "Чистое Небо EE", "Enhanced"),
            new("stalker-cop-ee", "ЗП EE", "Зов Припяти EE", "Enhanced"),
            new("stalker2", "S2", "S.T.A.L.K.E.R. 2", "Unreal Engine 5"),
        ];

        var operations = new (string Key, string Name, string Desc)[]
        {
            ("edit_money", "Деньги", "Изменение количества рублей у сталкера"),
            ("edit_stacks", "Стаки предметов", "Изменение количества в пачках патронов и расходников"),
            ("edit_durability", "Прочность снаряжения", "Состояние и износ оружия, бронекостюмов и шлемов"),
            ("edit_placement", "Размещение в слотах", "Слоты оружия, пояс для артефактов и рюкзак"),
            ("edit_upgrades", "Апгрейды и модификации", "Установка и снятие веток улучшений оружия и брони"),
            ("edit_relations", "Отношения группировок", "Редактирование очков репутации и враждебности фракций"),
            ("edit_player_faction", "Фракция игрока", "Смена принадлежности сталкера к группировке"),
            ("move_items", "Тайники (перемещение)", "Перемещение хабара из тайников в рюкзак и обратно"),
            ("add_items", "Добавление предметов", "Спавн новых предметов из каталога в инвентарь"),
            ("remove_items", "Удаление предметов", "Безопасное удаление объектов из инвентаря"),
            ("read_inventory", "Чтение инвентаря", "Парсинг предметов, патронов и экипировки"),
            ("catalog", "Каталог предметов", "Сопоставление идентификаторов с официальными именами"),
        };

        var rows = new List<CapabilityRowViewModel>();
        foreach (var (key, name, desc) in operations)
        {
            var cells = new List<CapabilityCellViewModel>();
            foreach (var col in Columns)
            {
                CapabilitySupport support;
                try
                {
                    if (key == "read_inventory")
                    {
                        var format = service.GetFormat(col.Id);
                        support = new CapabilitySupport(
                            format.Capabilities.ReadInventory ? CapabilityMaturity.Verified : CapabilityMaturity.Unsupported);
                    }
                    else if (key == "catalog")
                    {
                        var format = service.GetFormat(col.Id);
                        support = new CapabilitySupport(
                            format.Capabilities.Catalog ? CapabilityMaturity.Verified : CapabilityMaturity.Unsupported);
                    }
                    else
                    {
                        support = service.Get(col.Id, key);
                    }
                }
                catch (KeyNotFoundException)
                {
                    support = new CapabilitySupport(CapabilityMaturity.Unsupported);
                }

                // If EditService explicitly restricts writes for this release, mark as UI restricted
                var isRestricted = !EditService.CanEdit(col.Id) && support.Writable;
                cells.Add(new CapabilityCellViewModel(col.Id, key, support.Maturity, support.Writable, support.Reason, isRestricted));
            }
            rows.Add(new CapabilityRowViewModel(key, name, desc, cells));
        }

        Rows = rows.AsReadOnly();
    }

    public string OracleRevision { get; }

    public IReadOnlyList<CapabilityFormatColumnViewModel> Columns { get; }

    public IReadOnlyList<CapabilityRowViewModel> Rows { get; }

    public string? SelectedFormatId
    {
        get => _selectedFormatId;
        set => SetProperty(ref _selectedFormatId, value);
    }
}
