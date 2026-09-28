using StalkerSaveEditor.Desktop.Services;
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
            StatusText = L.T("Блок UI");
            BadgeBackground = new SolidColorBrush(Color.Parse("#2B2215"));
            BadgeForeground = StalkerTheme.BrushAccentAmber;
            TooltipText = L.T("Запись для данного формата выключена в UI до верификации мутаций в живой игре.");
        }
        else
        {
            switch (maturity)
            {
                case CapabilityMaturity.Verified:
                    StatusText = L.T("Запись");
                    BadgeBackground = new SolidColorBrush(Color.Parse("#1A2E1A"));
                    BadgeForeground = StalkerTheme.BrushSuccess;
                    TooltipText = reason ?? L.T("Подтверждено и верифицировано в игре.");
                    break;
                case CapabilityMaturity.Experimental:
                    StatusText = L.T("Эксперим.");
                    BadgeBackground = new SolidColorBrush(Color.Parse("#332A15"));
                    BadgeForeground = StalkerTheme.BrushWarning;
                    TooltipText = reason ?? L.T("Экспериментальная поддержка (требуется проверка в игре).");
                    break;
                case CapabilityMaturity.Research:
                    StatusText = L.T("Чтение");
                    BadgeBackground = new SolidColorBrush(Color.Parse("#16283B"));
                    BadgeForeground = new SolidColorBrush(Color.Parse("#64B5F6"));
                    TooltipText = reason ?? L.T("Исследование / режим только для чтения.");
                    break;
                default:
                    StatusText = L.T("Нет");
                    BadgeBackground = new SolidColorBrush(Color.Parse("#1E201C"));
                    BadgeForeground = StalkerTheme.BrushTextMuted;
                    TooltipText = reason ?? L.T("Не поддерживается движком или форматом сохранения.");
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
            new("stalker-soc", L.T("ТЧ"), L.T("Тень Чернобыля"), "X-Ray 1.0"),
            new("stalker-cs", L.T("ЧН"), L.T("Чистое Небо"), "X-Ray 1.5"),
            new("stalker-cop", L.T("ЗП"), L.T("Зов Припяти"), "X-Ray 1.6"),
            new("stalker-soc-ee", L.T("ТЧ EE"), L.T("Тень Чернобыля EE"), "Enhanced"),
            new("stalker-cs-ee", L.T("ЧН EE"), L.T("Чистое Небо EE"), "Enhanced"),
            new("stalker-cop-ee", L.T("ЗП EE"), L.T("Зов Припяти EE"), "Enhanced"),
            new("stalker2", "S2", "S.T.A.L.K.E.R. 2", "Unreal Engine 5"),
        ];

        var operations = new (string Key, string Name, string Desc)[]
        {
            ("edit_money", L.T("Деньги"), L.T("Изменение количества рублей у сталкера")),
            ("edit_stacks", L.T("Стаки предметов"), L.T("Изменение количества в пачках патронов и расходников")),
            ("edit_durability", L.T("Прочность снаряжения"), L.T("Состояние и износ оружия, бронекостюмов и шлемов")),
            ("edit_placement", L.T("Размещение в слотах"), L.T("Слоты оружия, пояс для артефактов и рюкзак")),
            ("edit_upgrades", L.T("Апгрейды и модификации"), L.T("Установка и снятие веток улучшений оружия и брони")),
            ("edit_relations", L.T("Отношения группировок"), L.T("Редактирование очков репутации и враждебности фракций")),
            ("edit_player_faction", L.T("Фракция игрока"), L.T("Смена принадлежности сталкера к группировке")),
            ("move_items", L.T("Тайники (перемещение)"), L.T("Перемещение хабара из тайников в рюкзак и обратно")),
            ("add_items", L.T("Добавление предметов"), L.T("Спавн новых предметов из каталога в инвентарь")),
            ("remove_items", L.T("Удаление предметов"), L.T("Безопасное удаление объектов из инвентаря")),
            ("read_inventory", L.T("Чтение инвентаря"), L.T("Парсинг предметов, патронов и экипировки")),
            ("catalog", L.T("Каталог предметов"), L.T("Сопоставление идентификаторов с официальными именами")),
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
