using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class CloudView : UserControl
{
    public CloudView()
    {
        Content = BuildContent();
    }

    private static Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(20),
        };

        // 1. Header & Steam Status Banner
        var headerPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "СОХРАНЕНИЯ В STEAM CLOUD",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
        });

        var steamStatusBorder = new Border
        {
            Background = StalkerTheme.BrushBgInput,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
        };
        var steamStatusText = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
        };
        steamStatusText.Bind(TextBlock.TextProperty, new Binding(nameof(CloudViewModel.SteamStatusMessage)));
        steamStatusBorder.Child = steamStatusText;
        headerPanel.Children.Add(steamStatusBorder);

        root.Children.Add(headerPanel);

        // 2. Filter Bar & Actions
        var filterBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var appSelector = new ComboBox
        {
            Width = 240,
            ItemsSource = new[]
            {
                new ComboBoxItem { Content = "Все игры", Tag = 0 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R.: Тень Чернобыля (4500)", Tag = 4500 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R.: Чистое Небо (20510)", Tag = 20510 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R.: Зов Припяти (41700)", Tag = 41700 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R. 2: Heart of Chornobyl (1643320)", Tag = 1643320 },
            },
            SelectedIndex = 0,
        };
        appSelector.SelectionChanged += (s, e) =>
        {
            if (appSelector.SelectedItem is ComboBoxItem item && item.Tag is int appId)
            {
                if (appSelector.DataContext is CloudViewModel vm)
                {
                    vm.SelectedAppId = appId;
                }
            }
        };
        filterBar.Children.Add(appSelector);

        var refreshBtn = StalkerTheme.StalkerButton("ОБНОВИТЬ СПИСОК");
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(CloudViewModel.RefreshCommand)));
        Grid.SetColumn(refreshBtn, 2);
        filterBar.Children.Add(refreshBtn);

        Grid.SetRow(filterBar, 1);
        root.Children.Add(filterBar);

        // 3. Main Workspace: Cloud Saves List (left) + Action / Details Panel (right)
        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,340"),
        };

        // Left: List
        var listBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 12, 0),
        };

        var listBox = new ListBox
        {
            Background = Brushes.Transparent,
            ItemTemplate = StalkerTheme.Template<CloudSaveItemViewModel>(item =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    Margin = new Thickness(8, 6),
                };

                var nameStack = new StackPanel { Spacing = 2 };
                nameStack.Children.Add(new TextBlock
                {
                    Text = item.FileName,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 13,
                });
                nameStack.Children.Add(new TextBlock
                {
                    Text = $"{item.GameName} • {item.SizeText} • {item.TimestampText}",
                    FontSize = 11,
                    Foreground = StalkerTheme.BrushTextMuted,
                });
                row.Children.Add(nameStack);

                var badgeBorder = new Border
                {
                    Background = Brush.Parse(item.ComparisonBadgeColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0),
                };
                badgeBorder.Child = new TextBlock
                {
                    Text = item.ComparisonBadgeText,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                };
                Grid.SetColumn(badgeBorder, 1);
                row.Children.Add(badgeBorder);

                return row;
            }),
        };
        listBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CloudViewModel.CloudSaves)));
        listBox.Bind(ListBox.SelectedItemProperty, new Binding(nameof(CloudViewModel.SelectedCloudSave), BindingMode.TwoWay));
        listBorder.Child = listBox;
        workspace.Children.Add(listBorder);

        // Right: Detail Panel
        var detailPanel = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16),
        };

        var detailStack = new StackPanel { Spacing = 12 };

        detailStack.Children.Add(new TextBlock
        {
            Text = "ДЕЙСТВИЯ С СОХРАНЕНИЕМ",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.0,
        });

        var selectedName = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
        };
        selectedName.Bind(TextBlock.TextProperty, new Binding("SelectedCloudSave.FileName"));
        detailStack.Children.Add(selectedName);

        var selectedRemote = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        selectedRemote.Bind(TextBlock.TextProperty, new Binding("SelectedCloudSave.RemotePath"));
        detailStack.Children.Add(selectedRemote);

        // Action Buttons
        var downloadBtn = StalkerTheme.StalkerButton("СКАЧАТЬ В ЛОКАЛЬНЫЕ", isPrimary: false);
        downloadBtn.Bind(Button.CommandProperty, new Binding(nameof(CloudViewModel.DownloadSelectedCommand)));
        detailStack.Children.Add(downloadBtn);

        var writeBtn = StalkerTheme.StalkerButton("ЗАПИСАТЬ В ОБЛАКО...", isPrimary: true);
        writeBtn.Bind(Button.CommandProperty, new Binding(nameof(CloudViewModel.RequestWriteCommand)));
        detailStack.Children.Add(writeBtn);

        // Safety explanation card
        var safetyBorder = new Border
        {
            Background = StalkerTheme.BrushBgInput,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
        };
        safetyBorder.Child = new TextBlock
        {
            Text = "Безопасность Steam Cloud:\n• Запись в облако требует явного подтверждения.\n• Перед записью автоматически создаётся страховочный бэкап.\n• Если статус Uncertain — автоматический повтор запрещён.",
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        };
        detailStack.Children.Add(safetyBorder);

        // Confirmation Modal Card (visible when ShowWriteConfirmDialog == true)
        var confirmCard = new Border
        {
            Background = Brush.Parse("#381A14"),
            BorderBrush = Brush.Parse("#8E3A2E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 0),
        };
        confirmCard.Bind(Visual.IsVisibleProperty, new Binding(nameof(CloudViewModel.ShowWriteConfirmDialog)));

        var confirmStack = new StackPanel { Spacing = 8 };
        confirmStack.Children.Add(new TextBlock
        {
            Text = "ПОДТВЕРЖДЕНИЕ ПЕРЕЗАПИСИ",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#FF6B55"),
        });
        confirmStack.Children.Add(new TextBlock
        {
            Text = "Внимание: локальный файл будет отправлен в Steam Cloud и перезапишет облачное сохранение. Резервная копия будет сохранена в бэкапы.",
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
        });

        var confirmCheck = new CheckBox
        {
            Content = "Я подтверждаю перезапись",
            Foreground = StalkerTheme.BrushTextPrimary,
        };
        confirmCheck.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(CloudViewModel.WriteConfirmationChecked), BindingMode.TwoWay));
        confirmStack.Children.Add(confirmCheck);

        var confirmBtnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        var execWriteBtn = StalkerTheme.StalkerButton("ЗАПИСАТЬ", isPrimary: true);
        execWriteBtn.Bind(Button.CommandProperty, new Binding(nameof(CloudViewModel.ConfirmWriteCommand)));
        confirmBtnRow.Children.Add(execWriteBtn);

        var cancelWriteBtn = StalkerTheme.StalkerButton("ОТМЕНА", isPrimary: false);
        cancelWriteBtn.Bind(Button.CommandProperty, new Binding(nameof(CloudViewModel.CancelWriteCommand)));
        Grid.SetColumn(cancelWriteBtn, 2);
        confirmBtnRow.Children.Add(cancelWriteBtn);

        confirmStack.Children.Add(confirmBtnRow);
        confirmCard.Child = confirmStack;
        detailStack.Children.Add(confirmCard);

        detailPanel.Child = detailStack;
        Grid.SetColumn(detailPanel, 1);
        workspace.Children.Add(detailPanel);

        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);

        // 4. Status Bar
        var statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushAccentAmber,
            Margin = new Thickness(0, 10, 0, 0),
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(CloudViewModel.StatusMessage)));
        Grid.SetRow(statusText, 3);
        root.Children.Add(statusText);

        return root;
    }
}
