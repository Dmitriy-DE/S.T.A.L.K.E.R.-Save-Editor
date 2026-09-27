using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class BackupsView
{
    public static Control Build(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(16),
        };

        // Header and Refresh Button
        var refreshBtn = StalkerTheme.StalkerButton("Обновить бэкапы", isPrimary: false, minWidth: 140);
        refreshBtn.Click += (_, _) => vm.RefreshBackups();

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var intro = new TextBlock
        {
            Text = "Резервные копии создаются автоматически перед каждой операцией записи в соответствии с правилами безопасности.",
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        topGrid.Children.Add(intro);
        Grid.SetColumn(refreshBtn, 1);
        topGrid.Children.Add(refreshBtn);
        root.Children.Add(topGrid);

        // Table Header
        var tableHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,180,110,*,260"),
            Margin = new Thickness(8, 0, 8, 8),
        };
        tableHeader.Children.Add(new TextBlock { Text = "ДАТА СОЗДАНИЯ", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted });
        var c1 = new TextBlock { Text = "ИСХОДНЫЙ СЕЙВ", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c1, 1);
        tableHeader.Children.Add(c1);
        var c2 = new TextBlock { Text = "СТАТУС", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c2, 2);
        tableHeader.Children.Add(c2);
        var c3 = new TextBlock { Text = "РЕЗУЛЬТАТ", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c3, 3);
        tableHeader.Children.Add(c3);
        var c4 = new TextBlock { Text = "ДЕЙСТВИЯ", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c4, 4);
        tableHeader.Children.Add(c4);

        Grid.SetRow(tableHeader, 1);
        root.Children.Add(tableHeader);

        // List
        var list = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<BackupRecordViewModel>((item, _) => MakeBackupRow(vm, item)),
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Backups)));

        var scroll = new ScrollViewer
        {
            Content = list,
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        return root;
    }

    private static Control MakeBackupRow(SaveLibraryViewModel vm, BackupRecordViewModel item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,180,110,*,260"),
            Margin = new Thickness(0, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };

        grid.Children.Add(new TextBlock { Text = item.ShortDateDisplay, Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var c1 = new TextBlock { Text = item.SourceName, Foreground = StalkerTheme.BrushTextPrimary, FontWeight = FontWeight.Medium, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(c1, 1);
        grid.Children.Add(c1);

        var c2 = StalkerTheme.Badge(item.StatusDisplay, StalkerTheme.BrushBgElevated, new SolidColorBrush(Color.Parse(item.StatusBadgeColor)), 10);
        Grid.SetColumn(c2, 2);
        grid.Children.Add(c2);

        var c3 = new TextBlock { Text = item.OutputName ?? "—", Foreground = StalkerTheme.BrushTextMuted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(c3, 3);
        grid.Children.Add(c3);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var restoreInPlaceBtn = new Button
        {
            Content = "Восстановить",
            FontSize = 11,
            Padding = new Thickness(8, 4),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushAccentAmber,
            BorderBrush = StalkerTheme.BrushBorder,
            IsEnabled = item.CanRestore,
            CornerRadius = new CornerRadius(3),
        };
        restoreInPlaceBtn.Click += (_, _) => vm.RestoreBackup(item, inPlace: true);
        actions.Children.Add(restoreInPlaceBtn);

        var restoreAsBtn = new Button
        {
            Content = "В копию",
            FontSize = 11,
            Padding = new Thickness(8, 4),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            IsEnabled = item.CanRestore,
            CornerRadius = new CornerRadius(3),
        };
        restoreAsBtn.Click += (_, _) => vm.RestoreBackup(item, inPlace: false);
        actions.Children.Add(restoreAsBtn);

        Grid.SetColumn(actions, 4);
        grid.Children.Add(actions);

        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4),
            Child = grid,
        };
    }
}
