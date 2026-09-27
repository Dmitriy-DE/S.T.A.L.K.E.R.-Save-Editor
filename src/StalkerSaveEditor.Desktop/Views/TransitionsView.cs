using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class TransitionsView
{
    public static Control Build()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(16),
        };

        var noteStack = new StackPanel { Spacing = 4 };
        noteStack.Children.Add(new TextBlock
        {
            Text = "Переходы между локациями (Level Changers) читаются из геометрии и реестра игры. Согласно AGENTS.md, мутации точек перехода не производятся без подтверждения безопасности движка.",
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(StalkerTheme.Card(noteStack, "Карта переходов Зоны"));

        // Table
        var tableHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,160,160,*,70"),
            Margin = new Thickness(8, 0, 8, 8),
        };
        tableHeader.Children.Add(new TextBlock { Text = "ОТКУДА", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted });
        var c1 = new TextBlock { Text = "КУДА", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c1, 1);
        tableHeader.Children.Add(c1);
        var c2 = new TextBlock { Text = "ТОЧКА ВЫХОДА", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c2, 2);
        tableHeader.Children.Add(c2);
        var c3 = new TextBlock { Text = "КООРДИНАТЫ (X, Y, Z)", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c3, 3);
        tableHeader.Children.Add(c3);
        var c4 = new TextBlock { Text = "БЕЗ ЗВУКА", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(c4, 4);
        tableHeader.Children.Add(c4);

        var list = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<TransitionViewModel>((item, _) => MakeRow(item)),
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedSave.Transitions"));

        var scroll = new ScrollViewer
        {
            Content = new StackPanel { Children = { tableHeader, list } },
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        return root;
    }

    private static Control MakeRow(TransitionViewModel item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,160,160,*,70"),
            Margin = new Thickness(0, 4),
        };

        grid.Children.Add(new TextBlock { Text = item.SourceLevel, Foreground = StalkerTheme.BrushTextPrimary, FontSize = 12 });

        var c1 = new TextBlock { Text = item.DestLevel, Foreground = StalkerTheme.BrushAccentAmber, FontWeight = FontWeight.SemiBold, FontSize = 12 };
        Grid.SetColumn(c1, 1);
        grid.Children.Add(c1);

        var c2 = new TextBlock { Text = item.DestPoint, Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 };
        Grid.SetColumn(c2, 2);
        grid.Children.Add(c2);

        var c3 = new TextBlock { Text = item.CoordinatesDisplay, Foreground = StalkerTheme.BrushTextMuted, FontSize = 12 };
        Grid.SetColumn(c3, 3);
        grid.Children.Add(c3);

        var c4 = new TextBlock { Text = item.SilentDisplay, Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 };
        Grid.SetColumn(c4, 4);
        grid.Children.Add(c4);

        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4),
            Child = grid,
        };
    }
}
