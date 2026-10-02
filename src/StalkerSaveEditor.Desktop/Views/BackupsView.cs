using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class BackupsView
{
    private static readonly FuncValueConverter<string?, IBrush> StatusColorConverter =
        new(color => new SolidColorBrush(Color.Parse(string.IsNullOrWhiteSpace(color) ? "#D6A62D" : color)));

    public static Control Build(SaveLibraryViewModel viewModel)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(22),
            RowSpacing = 18,
            DataContext = viewModel,
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16,
        };
        var heading = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = L.T("ИСТОРИЯ И РЕЗЕРВНЫЕ КОПИИ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        heading.Children.Add(new TextBlock
        {
            Text = L.T("Резервные копии создаются перед записью. Здесь можно проверить их состояние и восстановить исходную копию."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        var refresh = StalkerTheme.StalkerButton(L.T("Обновить бэкапы"), isPrimary: false, minWidth: 150);
        refresh.Name = "backup-refresh";
        refresh.Click += (_, _) => viewModel.RefreshBackups();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.3*,0.9*"),
            ColumnSpacing = 16,
        };
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var historyCard = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14),
        };
        var history = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        var historyHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        historyHeader.Children.Add(new TextBlock
        {
            Text = L.T("РЕЗЕРВНЫЕ КОПИИ"),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var count = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        count.Bind(TextBlock.TextProperty, new Binding("Backups.Count") { StringFormat = L.T("Записей: {0}") });
        Grid.SetColumn(count, 1);
        historyHeader.Children.Add(count);
        history.Children.Add(historyHeader);

        var listHost = new Grid();
        var list = new ListBox
        {
            Name = "backup-history-list",
            Background = StalkerTheme.BrushBgPanel,
            BorderThickness = new Thickness(0),
            ItemTemplate = StalkerTheme.RecyclingTemplate<BackupRecordViewModel>(() => MakeBackupRow()),
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Backups)));
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedBackup)) { Mode = BindingMode.TwoWay });
        list.Bind(Visual.IsVisibleProperty, new Binding("Backups.Count")
        {
            Converter = new FuncValueConverter<int, bool>(countValue => countValue > 0),
        });
        listHost.Children.Add(list);

        var empty = new StackPanel
        {
            Name = "backup-empty-state",
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18),
        };
        empty.Children.Add(new TextBlock
        {
            Text = L.T("РЕЗЕРВНЫХ КОПИЙ ПОКА НЕТ"),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        empty.Children.Add(new TextBlock
        {
            Text = L.T("Копия появится после первой операции записи в сохранение."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        empty.Bind(Visual.IsVisibleProperty, new Binding("Backups.Count")
        {
            Converter = new FuncValueConverter<int, bool>(countValue => countValue == 0),
        });
        listHost.Children.Add(empty);
        Grid.SetRow(listHost, 1);
        history.Children.Add(listHost);
        historyCard.Child = history;
        content.Children.Add(historyCard);

        var detailsCard = new Border
        {
            Name = "backup-details",
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16),
        };
        detailsCard.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.HasSelectedBackup)));
        var details = new StackPanel { Spacing = 12 };

        var detailsHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        detailsHeading.Children.Add(new TextBlock
        {
            Text = L.T("СВЕДЕНИЯ О КОПИИ"),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var status = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.Bind(TextBlock.TextProperty, new Binding("SelectedBackup.StatusDisplay"));
        status.Bind(TextBlock.ForegroundProperty, new Binding("SelectedBackup.StatusBadgeColor") { Converter = StatusColorConverter });
        Grid.SetColumn(status, 1);
        detailsHeading.Children.Add(status);
        details.Children.Add(detailsHeading);

        var sourceName = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        sourceName.Bind(TextBlock.TextProperty, new Binding("SelectedBackup.SourceName"));
        details.Children.Add(sourceName);
        details.Children.Add(MakeDetailRow(L.T("СОЗДАНА"), "SelectedBackup.ShortDateDisplay"));
        details.Children.Add(MakeDetailRow(L.T("ИСХОДНЫЙ ФАЙЛ"), "SelectedBackup.SourcePath"));
        details.Children.Add(MakeDetailRow(L.T("ФАЙЛ КОПИИ"), "SelectedBackup.BackupPath"));
        details.Children.Add(MakeDetailRow(L.T("ФАЙЛ ПОСЛЕ ОПЕРАЦИИ"), "SelectedBackup.OutputPath", targetNullValue: "—"));
        details.Children.Add(MakeDetailRow(L.T("SHA-256 ИСХОДНОГО ФАЙЛА"), "SelectedBackup.SourceSha256"));
        details.Children.Add(MakeDetailRow(L.T("SHA-256 КОПИИ"), "SelectedBackup.ActualSha256", targetNullValue: "—"));

        var error = new TextBlock
        {
            Name = "backup-error",
            Foreground = StalkerTheme.BrushDanger,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        error.Bind(TextBlock.TextProperty, new Binding("SelectedBackup.Error"));
        error.Bind(Visual.IsVisibleProperty, new Binding("SelectedBackup.HasError"));
        details.Children.Add(error);

        var restoreReason = new TextBlock
        {
            Name = "backup-restore-disabled-reason",
            Foreground = StalkerTheme.BrushWarning,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        restoreReason.Bind(TextBlock.TextProperty, new Binding("SelectedBackup.RestoreInPlaceDisabledReason"));
        restoreReason.Bind(Visual.IsVisibleProperty, new Binding("SelectedBackup.CanRestoreInPlace")
        {
            Converter = new FuncValueConverter<bool, bool>(canRestore => !canRestore),
        });
        details.Children.Add(restoreReason);

        var actions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var restoreInPlace = StalkerTheme.StalkerButton(L.T("Восстановить на место"), isPrimary: true, minWidth: 220);
        restoreInPlace.Name = "backup-restore-in-place";
        restoreInPlace.Bind(Button.IsEnabledProperty, new Binding("SelectedBackup.CanRestoreInPlace"));
        restoreInPlace.Bind(ToolTip.TipProperty, new Binding("SelectedBackup.RestoreInPlaceDisabledReason"));
        restoreInPlace.Click += (_, _) =>
        {
            if (viewModel.SelectedBackup is { } backup)
            {
                viewModel.RestoreBackup(backup, inPlace: true);
            }
        };
        actions.Children.Add(restoreInPlace);

        var restoreCopy = StalkerTheme.StalkerButton(L.T("Восстановить в копию"), isPrimary: false, minWidth: 220);
        restoreCopy.Name = "backup-restore-copy";
        restoreCopy.Bind(Button.IsEnabledProperty, new Binding("SelectedBackup.CanRestoreCopy"));
        restoreCopy.Bind(ToolTip.TipProperty, new Binding("SelectedBackup.RestoreCopyDisabledReason"));
        restoreCopy.Click += (_, _) =>
        {
            if (viewModel.SelectedBackup is { } backup)
            {
                viewModel.RestoreBackup(backup, inPlace: false);
            }
        };
        actions.Children.Add(restoreCopy);
        details.Children.Add(actions);
        detailsCard.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = details,
        };
        Grid.SetColumn(detailsCard, 1);
        content.Children.Add(detailsCard);

        var noSelection = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(20),
            Child = new TextBlock
            {
                Text = L.T("Выберите резервную копию, чтобы посмотреть сведения и доступные действия."),
                Foreground = StalkerTheme.BrushTextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        noSelection.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.HasSelectedBackup))
        {
            Converter = new FuncValueConverter<bool, bool>(hasSelection => !hasSelection),
        });
        Grid.SetColumn(noSelection, 1);
        content.Children.Add(noSelection);

        return root;
    }

    private static Control MakeBackupRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2),
            ColumnSpacing = 12,
        };
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextPrimary,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(BackupRecordViewModel.SourceName)));
        text.Children.Add(name);

        var date = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
        };
        date.Bind(TextBlock.TextProperty, new Binding(nameof(BackupRecordViewModel.ShortDateDisplay)));
        text.Children.Add(date);
        grid.Children.Add(text);

        var status = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(BackupRecordViewModel.StatusDisplay)));
        status.Bind(TextBlock.ForegroundProperty, new Binding(nameof(BackupRecordViewModel.StatusBadgeColor)) { Converter = StatusColorConverter });
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);

        return new Border
        {
            Background = StalkerTheme.BrushBgElevated,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(11, 9),
            Child = grid,
        };
    }

    private static Control MakeDetailRow(string label, string bindingPath, string? targetNullValue = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("136,*"),
            ColumnSpacing = 8,
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        var value = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        value.Bind(TextBlock.TextProperty, new Binding(bindingPath) { TargetNullValue = targetNullValue });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }
}
