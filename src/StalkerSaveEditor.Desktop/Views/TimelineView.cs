using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class TimelineView
{
    public static Control Build(SaveTimelineViewModel viewModel)
    {
        var rows = new ItemsControl
        {
            ItemsSource = viewModel.Entries,
            ItemTemplate = StalkerTheme.Template<SaveTimelineEntry>(entry =>
            {
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(0, 4),
                };
                var details = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock
                {
                    Text = entry.Title,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                details.Children.Add(new TextBlock
                {
                    Text = entry.Details,
                    Foreground = StalkerTheme.BrushTextMuted,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                });
                grid.Children.Add(details);

                var compare = StalkerTheme.StalkerButton(L.T("Сравнить с предыдущим"), isPrimary: false, minWidth: 190);
                compare.IsEnabled = viewModel.CanCompare(entry);
                ToolTip.SetTip(compare, compare.IsEnabled
                    ? L.T("Откройте экран сравнения")
                    : L.T("Нет предыдущего сохранения с известным временем для этой игры."));
                compare.Click += (_, _) => viewModel.CompareAdjacent(entry);
                Grid.SetColumn(compare, 1);
                grid.Children.Add(compare);
                return StalkerTheme.Card(grid);
            }),
        };

        var empty = new TextBlock
        {
            Text = L.T("Сохранения появятся после выбора папок в настройках."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(SaveTimelineViewModel.HasEntries))
            {
                Source = viewModel,
                Converter = new Avalonia.Data.Converters.FuncValueConverter<bool, bool>(hasEntries => !hasEntries),
            },
        };

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("ИСТОРИЯ СОХРАНЕНИЙ"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Временная последовательность строится только по времени изменения реальных файлов."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(empty);
        stack.Children.Add(rows);
        return new ScrollViewer { Padding = new Thickness(20), Content = stack };
    }
}
