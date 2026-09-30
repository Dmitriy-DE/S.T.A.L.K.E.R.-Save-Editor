using StalkerSaveEditor.Desktop.Services;
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
            Text = L.T("МОД-КОМПАНЬОН"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            LetterSpacing = 1.0,
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = L.T("Меню в игре: Esc → F1 или КПК компаньона. Установка через приложение ниже."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
        });

        var gameSelectorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 10, 0, 0),
        };
        gameSelectorRow.Children.Add(new TextBlock
        {
            Text = L.T("Целевая игра:"),
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

        var statusHeaderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var statusTitle = new TextBlock
        {
            Text = L.T("СТАТУС И СВЯЗЬ"),
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
                    Converter = new FuncValueConverter<string, IBrush>(c =>
                        new SolidColorBrush(Color.Parse(c ?? "#FFFFFF"))),
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
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = StalkerTheme.BrushTextMuted,
                FontSize = 12,
                Margin = new Thickness(0, 4),
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            detailsGrid.Children.Add(lbl);

            var val = new TextBlock
            {
                Foreground = StalkerTheme.BrushTextPrimary,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 4),
                TextWrapping = TextWrapping.Wrap,
                [!TextBlock.TextProperty] = new Binding(bindingPath),
            };
            Grid.SetRow(val, row);
            Grid.SetColumn(val, 1);
            detailsGrid.Children.Add(val);
        }

        AddDetail(0, L.T("Версия мода:"), "VersionText");
        AddDetail(1, L.T("Связь / Задержка:"), "PingText");
        AddDetail(2, L.T("Путь установки:"), "GamePath");

        statusStack.Children.Add(detailsGrid);

        // Install issues block (hidden when empty)
        var issuesBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#E05252")),
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding("InstallIssues"),
            [!IsVisibleProperty] = new Binding("InstallIssues")
            {
                Converter = new FuncValueConverter<string, bool>(s => !string.IsNullOrEmpty(s)),
            },
        };
        statusStack.Children.Add(issuesBlock);

        // Action Buttons Row
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var btnInstall = StalkerTheme.StalkerButton(L.T("УСТАНОВИТЬ / ОБНОВИТЬ"), isPrimary: true, minWidth: 150);
        btnInstall.Bind(Button.CommandProperty, new Binding("InstallCommand"));
        btnRow.Children.Add(btnInstall);

        var btnUninstall = StalkerTheme.StalkerButton(L.T("УДАЛИТЬ"), isPrimary: false, minWidth: 100);
        btnUninstall.Bind(Button.CommandProperty, new Binding("UninstallCommand"));
        btnRow.Children.Add(btnUninstall);

        var btnPing = StalkerTheme.StalkerButton(L.T("ПРОВЕРИТЬ СВЯЗЬ"), isPrimary: false, minWidth: 160);
        btnPing.Bind(Button.CommandProperty, new Binding("PingCommand"));
        btnRow.Children.Add(btnPing);

        var btnRefresh = StalkerTheme.StalkerButton(L.T("ОБНОВИТЬ СТАТУС"), isPrimary: false, minWidth: 140);
        btnRefresh.Bind(Button.CommandProperty, new Binding("RefreshCommand"));
        btnRow.Children.Add(btnRefresh);

        statusStack.Children.Add(btnRow);
        mainStack.Children.Add(StalkerTheme.Card(statusStack));

        var inspectorStack = new StackPanel { Spacing = 10 };
        inspectorStack.Children.Add(new TextBlock
        {
            Text = L.T("ЖИВОЙ ИНСПЕКТОР"),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 0.8,
        });
        inspectorStack.Children.Add(new TextBlock
        {
            Text = L.T("Показываются только ответы протокола Companion: info и list_inventory."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var inspectorButton = StalkerTheme.StalkerButton(L.T("ПОЛУЧИТЬ ДАННЫЕ"), isPrimary: true, minWidth: 170);
        inspectorButton.Bind(Button.CommandProperty, new Binding("InspectCommand"));
        inspectorStack.Children.Add(inspectorButton);
        var inspectorDisabled = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding("InspectorDisabledReason"),
            [!IsVisibleProperty] = new Binding("InspectorDisabledReason")
            {
                Converter = new FuncValueConverter<string, bool>(reason => !string.IsNullOrWhiteSpace(reason)),
            },
        };
        inspectorStack.Children.Add(inspectorDisabled);
        inspectorStack.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            [!TextBlock.TextProperty] = new Binding("InspectorStatus"),
        });
        inspectorStack.Children.Add(new TextBlock
        {
            Text = L.T("Информация игрока"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontWeight = FontWeight.SemiBold,
            [!IsVisibleProperty] = new Binding("InspectorInfo") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty },
        });
        inspectorStack.Children.Add(new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 100,
            Background = StalkerTheme.BrushBgInput,
            [!TextBox.TextProperty] = new Binding("InspectorInfo"),
            // Hidden until the game answered: empty boxes looked like broken output.
            [!IsVisibleProperty] = new Binding("InspectorInfo") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty },
        });
        inspectorStack.Children.Add(new TextBlock
        {
            Text = L.T("Инвентарь игрока"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontWeight = FontWeight.SemiBold,
            [!IsVisibleProperty] = new Binding("InspectorInventory") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty },
        });
        inspectorStack.Children.Add(new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            Background = StalkerTheme.BrushBgInput,
            [!TextBox.TextProperty] = new Binding("InspectorInventory"),
            // Hidden until the game answered: empty boxes looked like broken output.
            [!IsVisibleProperty] = new Binding("InspectorInventory") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty },
        });
        mainStack.Children.Add(StalkerTheme.Card(inspectorStack));

        // 2a'. S2 debug commands (experimental; the game's own GSC commands through the UE4SS mod)
        var s2Stack = new StackPanel { Spacing = 8 };
        s2Stack.Children.Add(new TextBlock
        {
            Text = L.T("S.T.A.L.K.E.R. 2 — команды игры (экспериментально)"),
            Foreground = StalkerTheme.BrushAccentAmber,
            FontWeight = FontWeight.Bold,
        });
        s2Stack.Children.Add(new TextBlock
        {
            Text = L.T("Нужны S2 на ПК, UE4SS и установленный мод. Команды выполняет сама игра (XSetGodMode, XSetNoClipGSC, XSetTimeSpeed)."),
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        var s2Buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, spec) in new[]
        {
            (L.T("Бессмертие: вкл"), "god on"), (L.T("Бессмертие: выкл"), "god off"),
            (L.T("Полёт: вкл"), "noclip on"), (L.T("Полёт: выкл"), "noclip off"),
            (L.T("Время ×5"), "timespeed 5"), (L.T("Время: норма"), "timespeed 0"),
        })
        {
            var button = StalkerTheme.StalkerButton(label, isPrimary: false, minWidth: 140);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Bind(Button.CommandProperty, new Binding(nameof(CompanionViewModel.Stalker2CommandCommand)));
            button.CommandParameter = spec;
            s2Buttons.Children.Add(button);
        }

        s2Stack.Children.Add(s2Buttons);
        s2Stack.Children.Add(new TextBlock
        {
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding(nameof(CompanionViewModel.Stalker2CommandStatus)),
        });
        var s2Card = StalkerTheme.Card(s2Stack);
        s2Card.Bind(Visual.IsVisibleProperty, new Binding(nameof(CompanionViewModel.SupportsStalker2Commands)));
        mainStack.Children.Add(s2Card);

        // 2b. All games
        var gamesStack = new StackPanel { Spacing = 8 };
        gamesStack.Children.Add(new TextBlock
        {
            Text = L.T("ВСЕ ИГРЫ"),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 0.8,
        });
        var gamesList = new ItemsControl
        {
            ItemTemplate = StalkerTheme.Template<CompanionGameRow>(row =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 2) };
                if (row is null) return grid;
                var check = new CheckBox { IsEnabled = row.GameFound, VerticalAlignment = VerticalAlignment.Center };
                check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(CompanionGameRow.IsChecked)) { Mode = BindingMode.TwoWay });
                grid.Children.Add(check);
                var title = new TextBlock
                {
                    Text = row.Title,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MinWidth = 220,
                    MaxWidth = 320,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                Grid.SetColumn(title, 1);
                grid.Children.Add(title);
                var detail = new TextBlock
                {
                    Text = row.Status + (row.GameFound ? " · " + row.Path : string.Empty),
                    Foreground = row.GameFound ? StalkerTheme.BrushTextSecondary : StalkerTheme.BrushTextMuted,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                ToolTip.SetTip(detail, row.Path);
                Grid.SetColumn(detail, 2);
                grid.Children.Add(detail);
                return grid;
            }),
        };
        gamesList.Bind(ItemsControl.ItemsSourceProperty, new Binding("Games"));
        gamesStack.Children.Add(gamesList);
        var btnInstallChecked = StalkerTheme.StalkerButton(L.T("УСТАНОВИТЬ / ОБНОВИТЬ ВО ВСЕ ОТМЕЧЕННЫЕ"), isPrimary: true, minWidth: 300);
        btnInstallChecked.Bind(Button.CommandProperty, new Binding("InstallCheckedCommand"));
        btnInstallChecked.HorizontalAlignment = HorizontalAlignment.Left;
        gamesStack.Children.Add(btnInstallChecked);
        mainStack.Children.Add(StalkerTheme.Card(gamesStack));

        // 3. Manual Game Directory Card
        var gameDirStack = new StackPanel { Spacing = 10 };
        gameDirStack.Children.Add(new TextBlock
        {
            Text = L.T("ПАПКА ИГРЫ (РУЧНОЙ ВЫБОР)"),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 0.8,
        });
        gameDirStack.Children.Add(new TextBlock
        {
            Text = L.T("Оставьте пустым для автоматического поиска через Steam. Укажите путь вручную, если папка нестандартная."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });

        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var dirBox = new TextBox
        {
            Watermark = L.T("/путь/к/папке/игры"),
            MinWidth = 400,
            MaxWidth = 600,
            Background = StalkerTheme.BrushBgInput,
            BorderBrush = StalkerTheme.BrushBorder,
            [!TextBox.TextProperty] = new Binding("ManualGameDir", BindingMode.TwoWay),
        };
        dirRow.Children.Add(dirBox);

        var btnApplyDir = StalkerTheme.StalkerButton(L.T("ПРИМЕНИТЬ"), isPrimary: false, minWidth: 100);
        btnApplyDir.Bind(Button.CommandProperty, new Binding("SetManualDirCommand"));
        dirRow.Children.Add(btnApplyDir);

        gameDirStack.Children.Add(dirRow);
        mainStack.Children.Add(StalkerTheme.Card(gameDirStack));

        // 4. Hotkeys Card
        var hotkeysStack = new StackPanel { Spacing = 14 };
        hotkeysStack.Children.Add(new TextBlock
        {
            Text = L.T("ГОРЯЧИЕ КЛАВИШИ"),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 0.8,
        });

        hotkeysStack.Children.Add(new TextBlock
        {
            Text = L.T("Приложение перехватывает сочетание и отправляет команду моду через файл-протокол. ") +
                   L.T("Игра должна быть запущена с установленным модом."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });

        // Wayland warning (shown only when unsupported)
        var waylandWarning = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#3E2020")),
            BorderBrush = new SolidColorBrush(Color.Parse("#E05252")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            [!IsVisibleProperty] = new Binding("HotkeysUnsupportedReason")
            {
                Converter = new FuncValueConverter<string?, bool>(r => r is not null),
            },
            Child = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.Parse("#E05252")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                [!TextBlock.TextProperty] = new Binding("HotkeysUnsupportedReason"),
            },
        };
        hotkeysStack.Children.Add(waylandWarning);

        // Toggle row
        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };

        var hotkeyToggle = new ToggleButton
        {
            Content = L.T("Горячие клавиши"),
            MinWidth = 160,
            [!ToggleButton.IsCheckedProperty] = new Binding("HotkeysEnabled"),
            [!IsEnabledProperty] = new Binding("HotkeysSupported"),
        };
        hotkeyToggle.Click += (_, _) =>
        {
            if (hotkeyToggle.DataContext is CompanionViewModel vm)
            {
                vm.ToggleHotkeysCommand.Execute(null);
            }
        };
        toggleRow.Children.Add(hotkeyToggle);

        toggleRow.Children.Add(new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding("HotkeySummary"),
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        });
        hotkeysStack.Children.Add(toggleRow);

        // Hotkey binding list
        var hotkeysList = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Hotkeys"),
            ItemTemplate = StalkerTheme.Template<CompanionHotkeyItemViewModel>(item =>
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("160,120,*"),
                    Margin = new Thickness(0, 4),
                };

                var actionTb = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding("Description") { Source = item },
                    Foreground = StalkerTheme.BrushTextPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(actionTb, 0);
                rowGrid.Children.Add(actionTb);

                var keyBox = new TextBox
                {
                    [!TextBox.TextProperty] = new Binding("Key") { Source = item, Mode = BindingMode.TwoWay },
                    Background = StalkerTheme.BrushBgInput,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    FontSize = 12,
                    Width = 110,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                Grid.SetColumn(keyBox, 1);
                rowGrid.Children.Add(keyBox);

                var actionIdTb = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding("Action") { Source = item },
                    Foreground = StalkerTheme.BrushTextMuted,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                };
                Grid.SetColumn(actionIdTb, 2);
                rowGrid.Children.Add(actionIdTb);

                return rowGrid;
            }),
        };

        hotkeysStack.Children.Add(hotkeysList);

        var saveKeys = StalkerTheme.StalkerButton(L.T("СОХРАНИТЬ КЛАВИШИ"), isPrimary: true, minWidth: 170);
        saveKeys.Bind(Button.CommandProperty, new Binding("SaveHotkeysCommand"));
        var resetKeys = StalkerTheme.StalkerButton(L.T("ПО УМОЛЧАНИЮ"), isPrimary: false, minWidth: 140);
        resetKeys.Bind(Button.CommandProperty, new Binding("ResetHotkeysCommand"));
        hotkeysStack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveKeys, resetKeys } });
        mainStack.Children.Add(StalkerTheme.Card(hotkeysStack));

        // 5. Status Message Banner
        var statusMsg = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushAccentAmber,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding("StatusMessage"),
            [!IsVisibleProperty] = new Binding("StatusMessage")
            {
                Converter = new FuncValueConverter<string, bool>(s => !string.IsNullOrEmpty(s)),
            },
        };
        mainStack.Children.Add(statusMsg);

        root.Content = mainStack;
        return root;
    }
}
