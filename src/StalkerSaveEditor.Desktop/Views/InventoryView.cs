using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class InventoryView
{
    public static Control Build(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(12),
        };

        // 1. Money & Quick Actions Bar
        var moneyPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        moneyPanel.Children.Add(new TextBlock
        {
            Text = "ДЕНЬГИ:",
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        });

        var moneyBox = new TextBox
        {
            Width = 130,
            Height = 32,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = "RU",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        moneyBox.Bind(TextBox.TextProperty, new Binding(nameof(SaveLibraryViewModel.MoneyInput))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        moneyPanel.Children.Add(moneyBox);

        moneyPanel.Children.Add(MakeQuickMoneyButton(vm, "+10 000", "10000"));
        moneyPanel.Children.Add(MakeQuickMoneyButton(vm, "+50 000", "50000"));
        moneyPanel.Children.Add(MakeQuickMoneyButton(vm, "+100 000", "100000"));

        var addBtn = StalkerTheme.StalkerButton("+ Добавить предмет", isPrimary: true, minWidth: 150);
        addBtn.HorizontalAlignment = HorizontalAlignment.Right;
        addBtn.Click += (_, _) =>
        {
            var addVm = vm.CreateAddItemDialog();
            var dialog = new AddItemDialog(addVm, vm);
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is not null)
            {
                dialog.ShowDialog(desktop.MainWindow);
            }
        };

        var topBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        topBar.Children.Add(moneyPanel);
        Grid.SetColumn(addBtn, 1);
        topBar.Children.Add(addBtn);
        root.Children.Add(topBar);

        // 2. Category Filter & Search Bar
        var filterBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
        };

        filterBar.Children.Add(MakeCategoryChip(vm, "ВСЕ", "all"));
        filterBar.Children.Add(MakeCategoryChip(vm, "ОРУЖИЕ", "weapon"));
        filterBar.Children.Add(MakeCategoryChip(vm, "БОЕПРИПАСЫ", "ammo"));
        filterBar.Children.Add(MakeCategoryChip(vm, "СНАРЯЖЕНИЕ", "armor"));
        filterBar.Children.Add(MakeCategoryChip(vm, "РАСХОДНИКИ", "consumable"));
        filterBar.Children.Add(MakeCategoryChip(vm, "АРТЕФАКТЫ", "artifact"));
        filterBar.Children.Add(MakeCategoryChip(vm, "КЛЮЧИ", "quest"));
        filterBar.Children.Add(MakeCategoryChip(vm, "ПРОЧЕЕ", "other"));

        var searchBox = new TextBox
        {
            Width = 200,
            Height = 30,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = "Поиск предметов…",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(SaveLibraryViewModel.InventorySearchText))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

        var filterRow = new DockPanel();
        DockPanel.SetDock(searchBox, Dock.Right);
        filterRow.Children.Add(searchBox);
        filterRow.Children.Add(filterBar);

        Grid.SetRow(filterRow, 1);
        root.Children.Add(filterRow);

        // 3. Workspace: Items List (Left) + Item Inspector (Right)
        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,340"),
            Margin = new Thickness(0, 4, 0, 0),
        };

        var itemsList = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemTemplate = new FuncDataTemplate<InventoryLineViewModel>((item, _) => MakeItemRow(item)),
        };
        itemsList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.FilteredInventory)));
        itemsList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedItem))
        {
            Mode = BindingMode.TwoWay,
        });
        workspace.Children.Add(itemsList);

        var inspector = BuildItemInspector(vm);
        Grid.SetColumn(inspector, 1);
        workspace.Children.Add(inspector);

        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);

        return root;
    }

    private static Control MakeItemRow(InventoryLineViewModel item)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            Margin = new Thickness(6, 4),
        };

        // Title and key
        var nameStack = new StackPanel { Spacing = 2 };
        nameStack.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontWeight = FontWeight.Medium,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 13,
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = item.TypeKey,
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
        });
        row.Children.Add(nameStack);

        // Placement Badge
        if (!string.IsNullOrEmpty(item.PlacementDisplay) && item.Placement != "ruck")
        {
            var badge = StalkerTheme.Badge(item.PlacementDisplay, StalkerTheme.BrushBgElevated, StalkerTheme.BrushAccentAmber, 10);
            badge.Margin = new Thickness(8, 0);
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
        }

        // Durability
        var condBlock = new TextBlock
        {
            Text = item.ConditionDisplay,
            Foreground = new SolidColorBrush(Color.Parse(item.ConditionColor)),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        Grid.SetColumn(condBlock, 2);
        row.Children.Add(condBlock);

        // Count
        var countBlock = new TextBlock
        {
            Text = item.CountDisplay,
            Foreground = StalkerTheme.BrushAccentDim,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        Grid.SetColumn(countBlock, 3);
        row.Children.Add(countBlock);

        return row;
    }

    private static Control BuildItemInspector(SaveLibraryViewModel vm)
    {
        var scroll = new ScrollViewer { Padding = new Thickness(12, 0, 0, 0) };
        var inspectorStack = new StackPanel { Spacing = 14 };

        // Empty state
        var noSelect = new TextBlock
        {
            Text = "Выберите предмет для редактирования характеристик",
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
        };
        noSelect.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem") { Converter = new NullToBoolConverter(invert: true) });

        var content = new StackPanel { Spacing = 12 };
        content.Bind(Visual.IsVisibleProperty, new Binding("SelectedItem") { Converter = new NullToBoolConverter(invert: false) });

        // Item Header Card
        var headerCard = new StackPanel { Spacing = 4 };
        var title = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        title.Bind(TextBlock.TextProperty, new Binding("SelectedItem.Name"));
        headerCard.Children.Add(title);

        var key = new TextBlock { FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        key.Bind(TextBlock.TextProperty, new Binding("SelectedItem.TypeKey"));
        headerCard.Children.Add(key);
        content.Children.Add(StalkerTheme.Card(headerCard, "Выбранный предмет"));

        // Durability Card
        var durStack = new StackPanel { Spacing = 6 };
        var durLabel = new TextBlock { Text = "Состояние / Прочность:", Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 };
        durStack.Children.Add(durLabel);

        var sliderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var durSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 0, 8, 0),
        };
        durSlider.Bind(Slider.ValueProperty, new Binding("SelectedItem.ConditionPercent") { Mode = BindingMode.TwoWay });
        durSlider.Bind(Slider.IsEnabledProperty, new Binding("SelectedItem.CanEditCondition"));
        sliderRow.Children.Add(durSlider);

        var durPercentText = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushSuccess,
            VerticalAlignment = VerticalAlignment.Center,
        };
        durPercentText.Bind(TextBlock.TextProperty, new Binding("SelectedItem.ConditionDisplay"));
        Grid.SetColumn(durPercentText, 1);
        sliderRow.Children.Add(durPercentText);
        durStack.Children.Add(sliderRow);

        var quickCondRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        quickCondRow.Children.Add(MakeQuickCondButton(vm, "100%", "100"));
        quickCondRow.Children.Add(MakeQuickCondButton(vm, "75%", "75"));
        quickCondRow.Children.Add(MakeQuickCondButton(vm, "50%", "50"));
        durStack.Children.Add(quickCondRow);

        content.Children.Add(StalkerTheme.Card(durStack, "Прочность"));

        // Count Card
        var countStack = new StackPanel { Spacing = 6 };
        countStack.Children.Add(new TextBlock { Text = "Количество в стаке:", Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 });
        var countBox = new TextBox
        {
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        countBox.Bind(TextBox.TextProperty, new Binding("SelectedItem.CountInput") { Mode = BindingMode.TwoWay });
        countBox.Bind(TextBox.IsEnabledProperty, new Binding("SelectedItem.CanEditCount"));
        countStack.Children.Add(countBox);
        content.Children.Add(StalkerTheme.Card(countStack, "Количество"));

        // Placement Card
        var placeStack = new StackPanel { Spacing = 8 };
        placeStack.Children.Add(new TextBlock { Text = "Размещение:", Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 });
        var placeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        placeButtons.Children.Add(MakePlacementButton(vm, "Слот", "slot"));
        placeButtons.Children.Add(MakePlacementButton(vm, "Пояс", "belt"));
        placeButtons.Children.Add(MakePlacementButton(vm, "Рюкзак", "ruck"));
        placeStack.Children.Add(placeButtons);
        content.Children.Add(StalkerTheme.Card(placeStack, "Размещение"));

        // Upgrades Card
        var upgList = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<UpgradeItemViewModel>((upg, _) =>
            {
                var chk = new CheckBox
                {
                    Content = upg.DisplayName,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    Margin = new Thickness(0, 2),
                };
                chk.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(UpgradeItemViewModel.IsInstalled)) { Mode = BindingMode.TwoWay });
                chk.Bind(CheckBox.IsEnabledProperty, new Binding(nameof(UpgradeItemViewModel.CanEdit)));
                return chk;
            }),
        };
        upgList.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedItem.UpgradeItems"));
        content.Children.Add(StalkerTheme.Card(upgList, "Апгрейды"));

        // Action Buttons
        var deleteBtn = StalkerTheme.StalkerButton("Удалить предмет", isPrimary: false);
        deleteBtn.Foreground = StalkerTheme.BrushDanger;
        deleteBtn.BorderBrush = StalkerTheme.BrushDanger;
        deleteBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RemoveSelectedItemCommand)));
        content.Children.Add(deleteBtn);

        inspectorStack.Children.Add(noSelect);
        inspectorStack.Children.Add(content);
        scroll.Content = inspectorStack;
        return scroll;
    }

    private static Button MakeCategoryChip(SaveLibraryViewModel vm, string label, string category)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(8, 4),
            Background = vm.SelectedCategory == category ? StalkerTheme.BrushAccentAmber : StalkerTheme.BrushBgElevated,
            Foreground = vm.SelectedCategory == category ? new SolidColorBrush(Color.Parse("#0C0D0A")) : StalkerTheme.BrushTextSecondary,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            CornerRadius = new CornerRadius(3),
        };
        btn.Click += (_, _) => vm.SelectedCategory = category;
        return btn;
    }

    private static Button MakeQuickMoneyButton(SaveLibraryViewModel vm, string label, string amount)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 11,
            Padding = new Thickness(8, 5),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            CornerRadius = new CornerRadius(3),
        };
        btn.Click += (_, _) => vm.AddMoney(amount);
        return btn;
    }

    private static Button MakeQuickCondButton(SaveLibraryViewModel vm, string label, string percent)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 11,
            Padding = new Thickness(8, 3),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            CornerRadius = new CornerRadius(3),
        };
        btn.Click += (_, _) => vm.SetItemCondition(percent);
        return btn;
    }

    private static Button MakePlacementButton(SaveLibraryViewModel vm, string label, string target)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 11,
            Padding = new Thickness(10, 4),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            CornerRadius = new CornerRadius(3),
        };
        btn.Click += (_, _) =>
        {
            if (vm.SelectedItem is { CanEditPlacement: true })
            {
                vm.SelectedItem.Placement = target;
            }
        };
        return btn;
    }

    private sealed class NullToBoolConverter(bool invert) : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            invert ? value is null : value is not null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
