using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class FactionsView
{
    public static Control Build(SaveLibraryViewModel vm)
    {
        var scroll = new ScrollViewer { Padding = new Thickness(16) };
        var stack = new StackPanel { Spacing = 16 };

        // Player Faction Card
        var playerFactionStack = new StackPanel { Spacing = 8 };
        var curFactionText = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
        };
        curFactionText.Bind(TextBlock.TextProperty, new Binding("SelectedSave.PlayerFaction")
        {
            StringFormat = "Текущая группировка игрока: {0}",
            TargetNullValue = "Одиночки",
        });
        playerFactionStack.Children.Add(curFactionText);

        var playerFactionHint = new TextBlock
        {
            Text = "Группировка определяет начальные отношения и доступ к базам фракций в X-Ray.",
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
        };
        playerFactionStack.Children.Add(playerFactionHint);

        stack.Children.Add(StalkerTheme.Card(playerFactionStack, "Группировка игрока"));

        // Faction Relations Table Card
        var relationsList = new ItemsControl
        {
            ItemTemplate = StalkerTheme.Template<FactionRelationViewModel>(rel => MakeRelationRow(vm, rel)),
        };
        relationsList.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedSave.FactionRelations"));

        var tableHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,100,100,*"),
            Margin = new Thickness(8, 0, 8, 8),
        };
        tableHeader.Children.Add(new TextBlock { Text = "ГРУППИРОВКА", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted });
        var col1 = new TextBlock { Text = "ОЧКИ", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(col1, 1);
        tableHeader.Children.Add(col1);
        var col2 = new TextBlock { Text = "СТАТУС", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(col2, 2);
        tableHeader.Children.Add(col2);
        var col3 = new TextBlock { Text = "БЫСТРЫЕ ДЕЙСТВИЯ", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = StalkerTheme.BrushTextMuted };
        Grid.SetColumn(col3, 3);
        tableHeader.Children.Add(col3);

        var tableContainer = new StackPanel
        {
            Spacing = 4,
            Children = { tableHeader, relationsList }
        };

        stack.Children.Add(StalkerTheme.Card(tableContainer, "Отношения с группировками"));

        scroll.Content = stack;
        return scroll;
    }

    private static Control MakeRelationRow(SaveLibraryViewModel vm, FactionRelationViewModel rel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,100,100,*"),
            Margin = new Thickness(0, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = new TextBlock
        {
            Text = rel.DisplayName,
            FontWeight = FontWeight.Medium,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(name);

        var score = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextSecondary,
        };
        score.Bind(TextBlock.TextProperty, new Binding(nameof(FactionRelationViewModel.GoodwillDisplay)) { Source = rel });
        Grid.SetColumn(score, 1);
        grid.Children.Add(score);

        var status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Bold,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(FactionRelationViewModel.Attitude)) { Source = rel });
        status.Bind(TextBlock.ForegroundProperty, new Binding(nameof(FactionRelationViewModel.AttitudeBadgeColor))
        {
            Source = rel,
            Converter = new HexToBrushConverter(),
        });
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);

        // Buttons
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(MakeActionBtn("+100", () => vm.AdjustFactionRelation(rel, 100), rel));
        actions.Children.Add(MakeActionBtn("-100", () => vm.AdjustFactionRelation(rel, -100), rel));
        actions.Children.Add(MakeActionBtn("Друг (+1500)", () => rel.Goodwill = 1500, rel));
        actions.Children.Add(MakeActionBtn("Нейтрал (0)", () => rel.Goodwill = 0, rel));
        actions.Children.Add(MakeActionBtn("Враг (-1500)", () => rel.Goodwill = -1500, rel));

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var border = new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4),
            Child = grid,
        };
        return border;
    }

    private static Button MakeActionBtn(string text, Action action, FactionRelationViewModel rel)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 10,
            Padding = new Thickness(6, 3),
            Background = StalkerTheme.BrushBgElevated,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            CornerRadius = new CornerRadius(3),
        };
        btn.Bind(Button.IsEnabledProperty, new Binding(nameof(FactionRelationViewModel.CanEdit)) { Source = rel });
        btn.Bind(ToolTip.TipProperty, new Binding(nameof(FactionRelationViewModel.DisabledReason)) { Source = rel });
        btn.Click += (_, _) =>
        {
            if (rel.CanEdit) action();
        };
        return btn;
    }

    private sealed class HexToBrushConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            return StalkerTheme.BrushTextPrimary;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
