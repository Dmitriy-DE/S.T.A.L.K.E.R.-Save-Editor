using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class OverviewView
{
    private static Control BuildCompare()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Bind(StyledElement.DataContextProperty, new Binding(nameof(SaveLibraryViewModel.Compare)));
        var picker = new ComboBox { MinWidth = 320 };
        picker.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CompareViewModel.Candidates)));
        picker.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(CompareViewModel.Selected)) { Mode = BindingMode.TwoWay });
        picker.Bind(Visual.IsVisibleProperty, new Binding(nameof(CompareViewModel.HasCandidates)));
        panel.Children.Add(picker);

        var status = new TextBlock { FontSize = 12, Foreground = StalkerTheme.BrushTextSecondary, TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(CompareViewModel.Status)));
        panel.Children.Add(status);

        var rows = new ItemsControl
        {
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CompareRow>((row, _) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120,20,120"), Margin = new Thickness(0, 1) };
                if (row is null) return grid;
                grid.Children.Add(new TextBlock { Text = row.Label, Foreground = StalkerTheme.BrushTextPrimary, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                var before = new TextBlock { Text = row.Before, Foreground = StalkerTheme.BrushTextMuted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(before, 1);
                grid.Children.Add(before);
                var arrow = new TextBlock { Text = "→", Foreground = StalkerTheme.BrushTextMuted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(arrow, 2);
                grid.Children.Add(arrow);
                var after = new TextBlock { Text = row.After, Foreground = StalkerTheme.BrushAccentAmber, FontSize = 12 };
                Grid.SetColumn(after, 3);
                grid.Children.Add(after);
                return grid;
            }),
        };
        rows.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CompareViewModel.Rows)));
        panel.Children.Add(rows);
        return StalkerTheme.Card(panel, "Сравнить с другим сейвом или бэкапом");
    }

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
        stack.Children.Add(BuildCompare());

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
