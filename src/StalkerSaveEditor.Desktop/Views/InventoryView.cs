using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class InventoryView
{
    private static readonly BooleanNotConverter Not = new();
    private static readonly FuncValueConverter<bool, IBrush> ItemKeyForegroundConverter = new(
        isSelected => isSelected ? StalkerTheme.BrushAccentForeground : StalkerTheme.BrushTextSecondary);

    public static Control Build(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(12),
            RowSpacing = 8,
            DataContext = vm,
        };

        var moneyPanel = BuildMoneyPanel(vm);
        root.Children.Add(moneyPanel);

        var filterPanel = BuildFilterPanel(vm);
        Grid.SetRow(filterPanel, 1);
        root.Children.Add(filterPanel);

        var workspace = BuildWorkspace(vm);
        var addPanel = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            IsVisible = false,
        };
        Control actionBar = null!;

        var screenLayer = new Grid { Children = { workspace, addPanel } };
        void CloseAddPanel()
        {
            addPanel.Child = null;
            addPanel.IsVisible = false;
            workspace.IsVisible = true;
            moneyPanel.IsVisible = true;
            filterPanel.IsVisible = true;
            actionBar.IsVisible = true;
        }

        void OpenAddPanel()
        {
            var addVm = vm.CreateAddItemDialog();
            var content = AddItemDialog.BuildContent(addVm, vm, "inventory", CloseAddPanel);
            addPanel.Child = StalkerTheme.Card(content, L.T("Добавить предмет"));
            addPanel.IsVisible = true;
            workspace.IsVisible = false;
            moneyPanel.IsVisible = false;
            filterPanel.IsVisible = false;
            actionBar.IsVisible = false;
        }

        actionBar = BuildActionBar(vm, OpenAddPanel);
        Grid.SetRow(screenLayer, 2);
        root.Children.Add(screenLayer);

        Grid.SetRow(actionBar, 3);
        root.Children.Add(actionBar);
        return root;
    }

    private static Control BuildMoneyPanel(SaveLibraryViewModel vm)
    {
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = L.T("ДЕНЬГИ:"),
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });

        var currentMoney = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        currentMoney.Bind(TextBlock.TextProperty, new Binding("SelectedSave.MoneyDisplay"));
        row.Children.Add(currentMoney);

        var moneyBox = new TextBox
        {
            Width = 118,
            MinHeight = 34,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = "RU",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(moneyBox, L.T("Деньги в сохранении"));
        moneyBox.Bind(TextBox.TextProperty, new Binding(nameof(SaveLibraryViewModel.MoneyInput))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        moneyBox.Bind(TextBox.IsEnabledProperty, new Binding(nameof(SaveLibraryViewModel.CanEditMoney)));
        moneyBox.Bind(ToolTip.TipProperty, new Binding("SelectedSave.MoneyDisabledReason"));
        row.Children.Add(moneyBox);

        row.Children.Add(MakeQuickMoneyButton(vm, "+10 000", "10000"));
        row.Children.Add(MakeQuickMoneyButton(vm, "+50 000", "50000"));
        row.Children.Add(MakeQuickMoneyButton(vm, "+100 000", "100000"));

        var reason = DisabledReason("SelectedSave.MoneyDisabledReason", nameof(SaveLibraryViewModel.CanEditMoney));
        reason.Margin = new Thickness(0, 2, 0, 0);
        var content = new StackPanel { Spacing = 3, Children = { row, reason } };
        return StalkerTheme.Card(content, L.T("Баланс сохранения"), margin: new Thickness(0));
    }

    private static Control BuildFilterPanel(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 6,
        };

        var searchRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 6,
        };
        var searchBox = new TextBox
        {
            MinHeight = 34,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = L.T("Поиск предметов…"),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(searchBox, L.T("Поиск предметов"));
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(SaveLibraryViewModel.InventorySearchText))
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        searchRow.Children.Add(searchBox);

        var clearSearch = StalkerTheme.StalkerButton(L.T("Очистить"), isPrimary: false, minWidth: 82);
        clearSearch.Bind(Button.IsEnabledProperty, new Binding(nameof(SaveLibraryViewModel.InventorySearchText))
        {
            Source = vm,
            Converter = new StringHasValueConverter(),
        });
        clearSearch.Bind(ToolTip.TipProperty, new Binding(nameof(SaveLibraryViewModel.InventorySearchText)) { Source = vm });
        clearSearch.Click += (_, _) => vm.InventorySearchText = string.Empty;
        Grid.SetColumn(clearSearch, 1);
        searchRow.Children.Add(clearSearch);

        var count = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 110,
            TextAlignment = TextAlignment.Right,
        };
        count.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.FilteredInventory.Count))
        {
            Source = vm,
            StringFormat = L.T("Найдено: {0}"),
        });
        Grid.SetColumn(count, 2);
        searchRow.Children.Add(count);
        root.Children.Add(searchRow);

        var options = new (string Label, string Category)[]
        {
            (L.T("ВСЕ"), "all"),
            (L.T("ОРУЖИЕ"), "weapon"),
            (L.T("БОЕПРИПАСЫ"), "ammo"),
            (L.T("СНАРЯЖЕНИЕ"), "armor"),
            (L.T("РАСХОДНИКИ"), "consumable"),
            (L.T("АРТЕФАКТЫ"), "artifact"),
            (L.T("КЛЮЧИ"), "quest"),
            (L.T("ПРОЧЕЕ"), "other"),
        };
        var categoryButtons = options
            .Select(option => (Button: MakeCategoryChip(vm, option.Label, option.Category), option.Category))
            .ToArray();
        var categoryBar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (button, _) in categoryButtons) categoryBar.Children.Add(button);
        Grid.SetRow(categoryBar, 1);
        root.Children.Add(categoryBar);

        void UpdateCategoryState()
        {
            foreach (var (button, category) in categoryButtons)
            {
                var selected = string.Equals(vm.SelectedCategory, category, StringComparison.Ordinal);
                button.Background = selected ? StalkerTheme.BrushAccentAmber : StalkerTheme.BrushBgElevated;
                button.Foreground = selected ? StalkerTheme.BrushAccentForeground : StalkerTheme.BrushTextSecondary;
                button.BorderBrush = selected ? StalkerTheme.BrushBorderFocus : StalkerTheme.BrushBorderSubtle;
            }
        }
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SaveLibraryViewModel.SelectedCategory)) UpdateCategoryState();
        };
        UpdateCategoryState();
        return root;
    }

    private static Grid BuildWorkspace(SaveLibraryViewModel vm)
    {
        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,340"),
            ColumnSpacing = 10,
            RowDefinitions = new RowDefinitions("*"),
        };

        var itemsPane = BuildItemsPane(vm);
        var inspector = BuildItemInspector(vm);
        itemsPane.MinHeight = 220;
        inspector.MinHeight = 220;
        workspace.Children.Add(itemsPane);
        Grid.SetColumn(inspector, 1);
        workspace.Children.Add(inspector);

        var stacked = false;
        workspace.SizeChanged += (_, args) =>
        {
            var shouldStack = args.NewSize.Width < 760;
            if (shouldStack == stacked) return;
            stacked = shouldStack;
            workspace.ColumnDefinitions.Clear();
            workspace.RowDefinitions.Clear();
            if (stacked)
            {
                workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                workspace.RowSpacing = 8;
                Grid.SetColumn(inspector, 0);
                Grid.SetRow(inspector, 1);
            }
            else
            {
                workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
                workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                workspace.RowSpacing = 0;
                Grid.SetColumn(inspector, 1);
                Grid.SetRow(inspector, 0);
            }
        };
        return workspace;
    }

    private static Control BuildItemsPane(SaveLibraryViewModel vm)
    {
        var list = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemTemplate = StalkerTheme.Template<InventoryLineViewModel>(_ => MakeItemRow()),
        };
        list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(2, 0)),
            },
        });
        Avalonia.Automation.AutomationProperties.SetName(list, L.T("Предметы в инвентаре"));
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.FilteredInventory)) { Source = vm });
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedItem))
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
        });

        var noInventory = BuildEmptyState(L.T("◇"), L.T("Инвентарь пуст"), L.T("В этом сохранении нет предметов."));
        var noResults = (Border)BuildEmptyState(L.T("⌕"), L.T("Предметы не найдены"), L.T("Измените поиск или категорию."));
        var reset = StalkerTheme.StalkerButton(L.T("Сбросить фильтры"), isPrimary: false, minWidth: 150);
        reset.Click += (_, _) =>
        {
            vm.InventorySearchText = string.Empty;
            vm.SelectedCategory = "all";
        };
        ((StackPanel)noResults.Child!).Children.Add(reset);

        var body = new Grid { Children = { list, noInventory, noResults } };
        void UpdateEmptyStates()
        {
            var hasInventory = vm.SelectedSave is { Inventory.Count: > 0 };
            var hasFilteredItems = vm.FilteredInventory.Count > 0;
            list.IsVisible = hasFilteredItems;
            noInventory.IsVisible = !hasInventory;
            noResults.IsVisible = hasInventory && !hasFilteredItems;
        }
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SaveLibraryViewModel.SelectedSave)) UpdateEmptyStates();
        };
        vm.FilteredInventory.CollectionChanged += (_, _) => UpdateEmptyStates();
        UpdateEmptyStates();

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6,
            Children =
            {
                SectionHeader(L.T("Предметы"), nameof(SaveLibraryViewModel.FilteredInventory.Count), vm, "{0}"),
                body,
            },
        };
        Grid.SetRow(body, 1);

        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = content,
        };
    }

    private static Control MakeItemRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("36,*,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(4, 2),
            MinHeight = 40,
        };

        var iconFrame = new Border
        {
            Name = "inventory-item-icon-frame",
            Width = 32,
            Height = 32,
            Background = StalkerTheme.BrushBgBase,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new Grid(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var iconHost = (Grid)iconFrame.Child!;
        iconHost.Children.Add(new TextBlock
        {
            Text = "◇",
            FontSize = 18,
            Foreground = StalkerTheme.BrushAccentDim,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var icon = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(2) };
        icon.DataContextChanged += (_, _) =>
        {
            if (icon.DataContext is InventoryLineViewModel item)
                ItemIconService.Show(icon, item.ReleaseId, item.IconKey);
            else
                icon.Source = null;
        };
        iconHost.Children.Add(icon);
        row.Children.Add(iconFrame);

        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(InventoryLineViewModel.Name)));
        name.Bind(ToolTip.TipProperty, new Binding(nameof(InventoryLineViewModel.Name)));
        labels.Children.Add(name);
        var key = new TextBlock
        {
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        key.Bind(TextBlock.TextProperty, new Binding(nameof(InventoryLineViewModel.TypeKey)));
        key.Bind(TextBlock.ForegroundProperty, new Binding(nameof(InventoryLineViewModel.IsSelected))
        {
            Converter = ItemKeyForegroundConverter,
        });
        labels.Children.Add(key);
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);

        var values = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var placement = StalkerTheme.Badge(string.Empty, StalkerTheme.BrushBgElevated, StalkerTheme.BrushAccentAmber, 10);
        placement.Bind(Visual.IsVisibleProperty, new Binding(nameof(InventoryLineViewModel.ShowPlacementBadge)));
        ((TextBlock)placement.Child!).Bind(TextBlock.TextProperty, new Binding(nameof(InventoryLineViewModel.PlacementDisplay)));
        values.Children.Add(placement);

        var condition = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        condition.Bind(TextBlock.TextProperty, new Binding(nameof(InventoryLineViewModel.ConditionDisplay)));
        condition.Bind(TextBlock.ForegroundProperty, new Binding(nameof(InventoryLineViewModel.ConditionColor)) { Converter = StalkerTheme.ColorToBrush });
        values.Children.Add(condition);

        var count = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        count.Bind(TextBlock.TextProperty, new Binding(nameof(InventoryLineViewModel.CountDisplay)));
        values.Children.Add(count);
        Grid.SetColumn(values, 2);
        row.Children.Add(values);
        return row;
    }

    private static Control BuildItemInspector(SaveLibraryViewModel vm)
    {
        var noSelection = BuildEmptyState(L.T("◇"), L.T("Предмет не выбран"), L.T("Выберите предмет для редактирования характеристик"));
        var details = new StackPanel { Spacing = 8 };

        var header = new StackPanel { Spacing = 3 };
        var title = new TextBlock
        {
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 18,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        title.Bind(TextBlock.TextProperty, new Binding("SelectedItem.Name"));
        header.Children.Add(title);
        var type = new TextBlock { FontSize = 11, Foreground = StalkerTheme.BrushTextMuted, TextWrapping = TextWrapping.Wrap };
        type.Bind(TextBlock.TextProperty, new Binding("SelectedItem.TypeKey"));
        header.Children.Add(type);
        details.Children.Add(StalkerTheme.Card(header, L.T("Выбранный предмет"), margin: new Thickness(0)));

        details.Children.Add(BuildConditionCard(vm));
        details.Children.Add(BuildCountCard());
        details.Children.Add(BuildPlacementCard());
        details.Children.Add(BuildUpgradesCard());

        var scroll = new ScrollViewer
        {
            Content = details,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var selected = new Grid();
        selected.Children.Add(scroll);
        selected.Children.Add(noSelection);
        scroll.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.SelectedItem))
        {
            Source = vm,
            Converter = new NullToBoolConverter(invert: false),
        });
        noSelection.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.SelectedItem))
        {
            Source = vm,
            Converter = new NullToBoolConverter(invert: true),
        });
        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = selected,
        };
    }

    private static Control BuildConditionCard(SaveLibraryViewModel vm)
    {
        var value = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.Bind(TextBlock.TextProperty, new Binding("SelectedItem.ConditionDisplay"));
        value.Bind(TextBlock.ForegroundProperty, new Binding("SelectedItem.ConditionColor") { Converter = StalkerTheme.ColorToBrush });

        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = L.T("Состояние / прочность"), Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 },
                value,
            },
        };
        Grid.SetColumn(value, 1);

        var slider = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6) };
        slider.Bind(Slider.ValueProperty, new Binding("SelectedItem.ConditionPercent") { Mode = BindingMode.TwoWay });
        slider.Bind(Slider.IsEnabledProperty, new Binding("SelectedItem.CanEditCondition"));
        slider.Bind(ToolTip.TipProperty, new Binding("SelectedItem.ConditionDisabledReason"));
        slider.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem.OriginalCondition") { Converter = new NullToBoolConverter(invert: false) });

        var reason = DisabledReason("SelectedItem.ConditionDisabledReason", "SelectedItem.CanEditCondition");
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        quick.Children.Add(MakeQuickCondButton(vm, "100%", "100"));
        quick.Children.Add(MakeQuickCondButton(vm, "75%", "75"));
        quick.Children.Add(MakeQuickCondButton(vm, "50%", "50"));
        quick.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem.OriginalCondition") { Converter = new NullToBoolConverter(invert: false) });

        var content = new StackPanel { Spacing = 5, Children = { heading, slider, quick, reason } };
        return StalkerTheme.Card(content, L.T("Прочность"), margin: new Thickness(0));
    }

    private static Control BuildCountCard()
    {
        var countBox = new TextBox
        {
            MinHeight = 34,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        countBox.Bind(TextBox.TextProperty, new Binding("SelectedItem.CountInput") { Mode = BindingMode.TwoWay });
        countBox.Bind(TextBox.IsEnabledProperty, new Binding("SelectedItem.CanEditCount"));
        countBox.Bind(ToolTip.TipProperty, new Binding("SelectedItem.CountDisabledReason"));
        countBox.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem.CanEditCount"));

        var readOnlyValue = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 34,
        };
        readOnlyValue.Bind(TextBlock.TextProperty, new Binding("SelectedItem.OriginalCountDisplay"));
        readOnlyValue.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem.CanEditCount") { Converter = Not });

        var content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = L.T("Количество в пачке"), Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 },
                countBox,
                readOnlyValue,
                DisabledReason("SelectedItem.CountDisabledReason", "SelectedItem.CanEditCount"),
            },
        };
        return StalkerTheme.Card(content, L.T("Количество"), margin: new Thickness(0));
    }

    private static Control BuildPlacementCard()
    {
        var current = new TextBlock { FontSize = 12, Foreground = StalkerTheme.BrushTextPrimary };
        current.Bind(TextBlock.TextProperty, new Binding("SelectedItem.PlacementDisplay"));
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        options.Children.Add(MakePlacementButton(L.T("Слот"), "slot"));
        options.Children.Add(MakePlacementButton(L.T("Пояс"), "belt"));
        options.Children.Add(MakePlacementButton(L.T("Рюкзак"), "ruck"));

        var content = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L.T("Текущее размещение"), Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 },
                current,
                options,
                DisabledReason("SelectedItem.PlacementDisabledReason", "SelectedItem.CanEditPlacement"),
            },
        };
        return StalkerTheme.Card(content, L.T("Размещение"), margin: new Thickness(0));
    }

    private static Control BuildUpgradesCard()
    {
        var list = new ItemsControl
        {
            ItemTemplate = StalkerTheme.Template<UpgradeItemViewModel>(_ =>
            {
                var checkbox = new CheckBox
                {
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    Margin = new Thickness(0, 2),
                };
                checkbox.Bind(CheckBox.ContentProperty, new Binding(nameof(UpgradeItemViewModel.DisplayName)));
                checkbox.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(UpgradeItemViewModel.IsInstalled)) { Mode = BindingMode.TwoWay });
                checkbox.Bind(CheckBox.IsEnabledProperty, new Binding(nameof(UpgradeItemViewModel.CanEdit)));
                checkbox.Bind(ToolTip.TipProperty, new Binding(nameof(UpgradeItemViewModel.DisabledReason)));
                return checkbox;
            }),
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedItem.UpgradeItems"));

        var none = new TextBlock { Text = "—", Foreground = StalkerTheme.BrushTextMuted, FontSize = 12 };
        none.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem.HasUpgrades") { Converter = Not });
        var content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                list,
                none,
                DisabledReason("SelectedItem.UpgradesDisabledReason", "SelectedItem.CanEditUpgrades"),
            },
        };
        return StalkerTheme.Card(content, L.T("Модификации"), margin: new Thickness(0));
    }

    private static Control BuildActionBar(SaveLibraryViewModel vm, Action openAddPanel)
    {
        var status = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var draft = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
        };
        draft.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.DraftStatusText)) { Source = vm });
        status.Children.Add(draft);
        var hint = new TextBlock
        {
            Text = L.T("Правки остаются в черновике до нажатия «Сохранить»."),
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        };
        status.Children.Add(hint);

        var remove = StalkerTheme.StalkerButton(L.T("Удалить предмет"), isPrimary: false, minWidth: 136);
        remove.Foreground = StalkerTheme.BrushDanger;
        remove.BorderBrush = StalkerTheme.BrushDanger;
        remove.Bind(Button.IsEnabledProperty, new Binding("SelectedSave.CanRemoveItems") { Source = vm });
        remove.Bind(ToolTip.TipProperty, new Binding("SelectedSave.RemoveItemsDisabledReason") { Source = vm });
        remove.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RemoveSelectedItemCommand)) { Source = vm });

        var add = StalkerTheme.StalkerButton(L.T("+ Добавить предмет"), isPrimary: true, minWidth: 158);
        Avalonia.Automation.AutomationProperties.SetName(add, L.T("Добавить предмет"));
        add.Bind(Button.IsEnabledProperty, new Binding("SelectedSave.CanAddItems") { Source = vm });
        add.Bind(ToolTip.TipProperty, new Binding("SelectedSave.AddItemsDisabledReason") { Source = vm });
        add.Click += (_, _) =>
        {
            if (vm.SelectedSave is { CanAddItems: true }) openAddPanel();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(remove);
        buttons.Children.Add(add);
        var capabilityReasons = new StackPanel
        {
            Spacing = 2,
            MaxWidth = 330,
            Children =
            {
                DisabledReason("SelectedSave.RemoveItemsDisabledReason", "SelectedSave.CanRemoveItems"),
                DisabledReason("SelectedSave.AddItemsDisabledReason", "SelectedSave.CanAddItems"),
            },
        };
        var actions = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { buttons, capabilityReasons },
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            MinHeight = 50,
            Children = { status, actions },
        };
        Grid.SetColumn(actions, 1);
        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = grid,
        };
    }

    private static Control BuildEmptyState(string icon, string title, string message)
    {
        var stack = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
        };
        stack.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 28,
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 280,
        });
        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            Child = stack,
        };
    }

    private static Control SectionHeader(string title, string countPath, SaveLibraryViewModel vm, string format)
    {
        var count = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        count.Bind(TextBlock.TextProperty, new Binding(countPath) { Source = vm, StringFormat = format });
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(4, 0, 4, 2),
            Children =
            {
                new TextBlock
                {
                    Text = title.ToUpperInvariant(),
                    FontFamily = StalkerTheme.HeadingFont,
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    LetterSpacing = 0.8,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                count,
            },
        };
        Grid.SetColumn(count, 1);
        return header;
    }

    private static Button MakeCategoryChip(SaveLibraryViewModel vm, string label, string category)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(9, 6),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushTextSecondary,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            MinHeight = 30,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => vm.SelectedCategory = category;
        return button;
    }

    private static Button MakeQuickMoneyButton(SaveLibraryViewModel vm, string label, string amount)
    {
        var button = StalkerTheme.StalkerButton(label, isPrimary: false, minWidth: 74);
        button.Bind(Button.IsEnabledProperty, new Binding(nameof(SaveLibraryViewModel.CanEditMoney)) { Source = vm });
        button.Bind(ToolTip.TipProperty, new Binding("SelectedSave.MoneyDisabledReason") { Source = vm });
        button.Click += (_, _) =>
        {
            if (vm.CanEditMoney) vm.AddMoney(amount);
        };
        return button;
    }

    private static Button MakeQuickCondButton(SaveLibraryViewModel vm, string label, string percent)
    {
        var button = StalkerTheme.StalkerButton(label, isPrimary: false, minWidth: 52);
        button.Bind(Button.IsEnabledProperty, new Binding("SelectedItem.CanEditCondition") { Source = vm });
        button.Bind(ToolTip.TipProperty, new Binding("SelectedItem.ConditionDisabledReason") { Source = vm });
        button.Click += (_, _) =>
        {
            if (vm.SelectedItem is { CanEditCondition: true }) vm.SetItemCondition(percent);
        };
        return button;
    }

    private static Button MakePlacementButton(string label, string target)
    {
        var button = StalkerTheme.StalkerButton(label, isPrimary: false, minWidth: 58);
        button.Bind(Button.IsEnabledProperty, new Binding("SelectedItem.CanEditPlacement"));
        button.Bind(ToolTip.TipProperty, new Binding("SelectedItem.PlacementDisabledReason"));
        button.Click += (_, _) =>
        {
            if (button.DataContext is SaveLibraryViewModel { SelectedItem: { CanEditPlacement: true } } vm)
                vm.SelectedItem.Placement = target;
        };
        return button;
    }

    private static TextBlock DisabledReason(string reasonPath, string canEditPath)
    {
        var text = new TextBlock
        {
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        };
        text.Bind(TextBlock.TextProperty, new Binding(reasonPath));
        text.Bind(Visual.IsVisibleProperty, new Binding(canEditPath) { Converter = Not });
        return text;
    }

    private sealed class NullToBoolConverter(bool invert) : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            invert ? value is null : value is not null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class BooleanNotConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool enabled && !enabled;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class StringHasValueConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string { Length: > 0 };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
