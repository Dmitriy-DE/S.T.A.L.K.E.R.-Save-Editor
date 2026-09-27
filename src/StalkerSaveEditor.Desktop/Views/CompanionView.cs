using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class CompanionView : UserControl
{
    public CompanionView()
    {
        Content = BuildContent();
    }

    private static Control BuildContent()
    {
        var root = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 20),
        };

        var mainStack = new StackPanel { Spacing = 20, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };

        // 1. Header & Game Selector
        var headerPanel = new StackPanel { Spacing = 6 };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "ИНТЕЛЛЕКТУАЛЬНЫЙ КОМПАНЬОН (COMPANION MOD)",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            LetterSpacing = 1.0,
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Интеграция с движком X-Ray в реальном времени: телепортация по меткам, быстрое сохранение, хоткеи без сворачивания игры.",
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
        });

        var gameSelectorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 10, 0, 0) };
        gameSelectorRow.Children.Add(new TextBlock
        {
            Text = "Целевая игра:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontWeight = FontWeight.SemiBold,
        });

        var gameCombo = new ComboBox
        {
            MinWidth = 280,
            [!ItemsControl.ItemsSourceProperty] = new Binding("AvailableGames"),
            DisplayMemberBinding = new Binding("Value"),
            SelectedValueBinding = new Binding("Key"),
            [!SelectingItemsControl.SelectedValueProperty] = new Binding("SelectedGame", BindingMode.TwoWay),
        };
        gameSelectorRow.Children.Add(gameCombo);

        headerPanel.Children.Add(gameSelectorRow);
        mainStack.Children.Add(headerPanel);

        // 2. Status Card
        var statusStack = new StackPanel { Spacing = 14 };

        var statusHeaderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        var statusTitle = new TextBlock
        {
            Text = "СТАТУС И СВЯЗЬ",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 0.8,
        };
        Grid.SetColumn(statusTitle, 0);
        statusHeaderRow.Children.Add(statusTitle);

        var badgeBorder = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 3),
            Background = StalkerTheme.BrushBgElevated,
            Child = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                [!TextBlock.TextProperty] = new Binding("StatusBadgeText"),
                [!TextBlock.ForegroundProperty] = new Binding("StatusBadgeColor")
                {
                    Converter = new FuncValueConverter<string, IBrush>(c => new SolidColorBrush(Color.Parse(c ?? "#FFFFFF"))),
                },
            },
        };
        Grid.SetColumn(badgeBorder, 1);
        statusHeaderRow.Children.Add(badgeBorder);
        statusStack.Children.Add(statusHeaderRow);

        var detailsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        };

        void AddDetail(int row, string label, string bindingPath)
        {
            var lbl = new TextBlock { Text = label, Foreground = StalkerTheme.BrushTextMuted, FontSize = 12, Margin = new Thickness(0, 4) };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            detailsGrid.Children.Add(lbl);

            var val = new TextBlock { Foreground = StalkerTheme.BrushTextPrimary, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4), [!TextBlock.TextProperty] = new Binding(bindingPath) };
            Grid.SetRow(val, row);
            Grid.SetColumn(val, 1);
            detailsGrid.Children.Add(val);
        }

        AddDetail(0, "Версия мода:", "VersionText");
        AddDetail(1, "Пинг / Задержка:", "PingText");
        AddDetail(2, "Путь установки:", "GamePath");

        statusStack.Children.Add(detailsGrid);

        // Action Buttons Row
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };

        var btnInstall = StalkerTheme.StalkerButton("УСТАНОВИТЬ МОД", isPrimary: true, minWidth: 150);
        btnInstall.Bind(Button.CommandProperty, new Binding("InstallCommand"));
        btnRow.Children.Add(btnInstall);

        var btnUninstall = StalkerTheme.StalkerButton("УДАЛИТЬ", isPrimary: false, minWidth: 100);
        btnUninstall.Bind(Button.CommandProperty, new Binding("UninstallCommand"));
        btnRow.Children.Add(btnUninstall);

        var btnPing = StalkerTheme.StalkerButton("ПРОВЕРИТЬ СВЯЗЬ (PING)", isPrimary: false, minWidth: 180);
        btnPing.Bind(Button.CommandProperty, new Binding("PingCommand"));
        btnRow.Children.Add(btnPing);

        var btnRefresh = StalkerTheme.StalkerButton("ОБНОВИТЬ СТАТУС", isPrimary: false, minWidth: 140);
        btnRefresh.Bind(Button.CommandProperty, new Binding("RefreshCommand"));
        btnRow.Children.Add(btnRefresh);

        statusStack.Children.Add(btnRow);
        var statusCard = StalkerTheme.Card(statusStack);
        mainStack.Children.Add(statusCard);

        // 3. Hotkeys Card
        var hotkeysStack = new StackPanel { Spacing = 14 };

        hotkeysStack.Children.Add(new TextBlock
        {
            Text = "ПРИВЯЗКА ГОРЯЧИХ КЛАВИШ (HOTKEYS)",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 0.8,
        });

        hotkeysStack.Children.Add(new TextBlock
        {
            Text = "Клавиши перехватываются внутри игры через модуль companion_hotkeys.lua (опрос 250 мс без просадки FPS).",
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
        });

        var hotkeysList = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Hotkeys"),
            ItemTemplate = new FuncDataTemplate<CompanionHotkeyItemViewModel>((item, _) =>
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("140,90,*"),
                    Margin = new Thickness(0, 4),
                };

                var actionTb = new TextBlock
                {
                    Text = item.Action,
                    FontWeight = FontWeight.Bold,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(actionTb, 0);
                rowGrid.Children.Add(actionTb);

                var keyBox = new TextBox
                {
                    Text = item.Key,
                    MaxWidth = 85,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    Background = StalkerTheme.BrushBgInput,
                    BorderBrush = StalkerTheme.BrushBorder,
                };
                keyBox.LostFocus += (_, _) =>
                {
                    if (item.ChangeKeyCommand.CanExecute(keyBox.Text))
                        item.ChangeKeyCommand.Execute(keyBox.Text);
                };
                Grid.SetColumn(keyBox, 1);
                rowGrid.Children.Add(keyBox);

                var descTb = new TextBlock
                {
                    Text = item.Description,
                    Foreground = StalkerTheme.BrushTextMuted,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                };
                Grid.SetColumn(descTb, 2);
                rowGrid.Children.Add(descTb);

                return rowGrid;
            }),
        };

        hotkeysStack.Children.Add(hotkeysList);
        var hotkeysCard = StalkerTheme.Card(hotkeysStack);
        mainStack.Children.Add(hotkeysCard);

        // 4. Status Message Banner
        var statusMsg = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushAccentAmber,
            FontWeight = FontWeight.SemiBold,
            [!TextBlock.TextProperty] = new Binding("StatusMessage"),
        };
        mainStack.Children.Add(statusMsg);

        root.Content = mainStack;
        return root;
    }
}
