using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class OverviewView
{
    public static Control Build()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(16) };
        var stack = new StackPanel { Spacing = 16 };

        // Game Header Card
        var releaseTitle = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        };
        releaseTitle.Bind(TextBlock.TextProperty, new Binding("SelectedSave.ReleaseName"));

        var saveName = new TextBlock
        {
            FontSize = 14,
            Foreground = StalkerTheme.BrushTextSecondary,
        };
        saveName.Bind(TextBlock.TextProperty, new Binding("SelectedSave.DisplayName"));

        var savePath = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        };
        savePath.Bind(TextBlock.TextProperty, new Binding("SelectedSave.FilePath"));

        var headerContent = new StackPanel
        {
            Spacing = 6,
            Children = { releaseTitle, saveName, savePath }
        };
        stack.Children.Add(StalkerTheme.Card(headerContent, "Информация о сохранении"));

        // Stats Grid
        var statsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };

        statsGrid.Children.Add(MakeMetricCell("ДЕНЬГИ", "SelectedSave.MoneyDisplay", 0, 0, StalkerTheme.BrushAccentAmber));
        statsGrid.Children.Add(MakeMetricCell("ПРЕДМЕТОВ", "SelectedSave.Inventory.Count", 1, 0, StalkerTheme.BrushTextPrimary));
        statsGrid.Children.Add(MakeMetricCell("ТАЙНИКОВ", "SelectedSave.Stashes.Count", 2, 0, StalkerTheme.BrushTextPrimary));
        statsGrid.Children.Add(MakeMetricCell("ИГРОВОЕ ВРЕМЯ", "SelectedSave.GameTimeDisplay", 3, 0, StalkerTheme.BrushTextSecondary));

        statsGrid.Children.Add(MakeMetricCell("ПЕРСОНАЖ", "SelectedSave.ActorName", 0, 1, StalkerTheme.BrushTextPrimary));
        statsGrid.Children.Add(MakeMetricCell("ЗДОРОВЬЕ", "SelectedSave.HealthDisplay", 1, 1, StalkerTheme.BrushSuccess));
        statsGrid.Children.Add(MakeMetricCell("РАНГ", "SelectedSave.RankDisplay", 2, 1, StalkerTheme.BrushAccentDim));
        statsGrid.Children.Add(MakeMetricCell("ЛОКАЦИЯ", "SelectedSave.LevelName", 3, 1, StalkerTheme.BrushTextSecondary));

        stack.Children.Add(StalkerTheme.Card(statsGrid, "Параметры сталкера"));

        // File Details & Checksums
        var fileDetails = new StackPanel { Spacing = 6 };

        fileDetails.Children.Add(MakeRow("Размер файла:", "SelectedSave.FileSizeDisplay"));
        fileDetails.Children.Add(MakeRow("Изменён:", "SelectedSave.LastModifiedDisplay"));
        fileDetails.Children.Add(MakeRow("SHA-256:", "SelectedSave.SourceSha256"));

        stack.Children.Add(StalkerTheme.Card(fileDetails, "Целостность и метаданные"));

        scroll.Content = stack;
        return scroll;
    }

    private static Control MakeMetricCell(string label, string bindingPath, int col, int row, IBrush valueBrush)
    {
        var panel = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(8),
        };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextMuted,
        });

        var valueBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = valueBrush,
        };
        valueBlock.Bind(TextBlock.TextProperty, new Binding(bindingPath));
        panel.Children.Add(valueBlock);

        var border = new Border
        {
            Background = StalkerTheme.BrushBgElevated,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8),
            Child = panel,
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        return border;
    }

    private static Control MakeRow(string label, string bindingPath)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            Margin = new Thickness(0, 2),
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
        });

        var val = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        val.Bind(TextBlock.TextProperty, new Binding(bindingPath));
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);

        return grid;
    }
}
