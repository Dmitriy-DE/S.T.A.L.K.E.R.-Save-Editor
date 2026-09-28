using StalkerSaveEditor.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class CapabilitiesView
{
    public static Control Build(CapabilitiesViewModel vm)
    {
        var scroll = new ScrollViewer
        {
            Padding = new Thickness(16),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var stack = new StackPanel { Spacing = 16 };

        // 1. Header Card
        var headerStack = new StackPanel { Spacing = 6 };
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var titleBlock = new TextBlock
        {
            Text = L.T("МАТРИЦА ВОЗМОЖНОСТЕЙ РЕДАКТОРА"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
        };
        titleRow.Children.Add(titleBlock);

        var oracleBadge = StalkerTheme.Badge(
            $"Oracle: {vm.OracleRevision[..Math.Min(8, vm.OracleRevision.Length)]}",
            StalkerTheme.BrushBgElevated,
            StalkerTheme.BrushTextSecondary,
            10);
        Grid.SetColumn(oracleBadge, 1);
        titleRow.Children.Add(oracleBadge);
        headerStack.Children.Add(titleRow);

        var subtitle = new TextBlock
        {
            Text = L.T("Что редактор умеет делать с сейвами каждой игры."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
        };
        headerStack.Children.Add(subtitle);

        stack.Children.Add(StalkerTheme.Card(headerStack, L.T("Справка по возможностям")));

        // 2. Table Card
        var tableStack = new StackPanel { Spacing = 2 };

        // Table Header
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("200,80,80,80,80,80,80,80"),
            Margin = new Thickness(8, 4, 8, 8),
        };

        var opHeader = new TextBlock
        {
            Text = L.T("ОПЕРАЦИЯ"),
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerGrid.Children.Add(opHeader);

        for (var i = 0; i < vm.Columns.Count; i++)
        {
            var col = vm.Columns[i];
            var colHeader = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            colHeader.Children.Add(new TextBlock
            {
                Text = col.ShortName,
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = StalkerTheme.BrushAccentAmber,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            colHeader.Children.Add(new TextBlock
            {
                Text = col.Edition,
                FontSize = 9,
                Foreground = StalkerTheme.BrushTextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            ToolTip.SetTip(colHeader, $"{col.FullName} ({col.Edition})");
            Grid.SetColumn(colHeader, i + 1);
            headerGrid.Children.Add(colHeader);
        }

        tableStack.Children.Add(headerGrid);

        // Separator
        tableStack.Children.Add(new Border
        {
            Height = 1,
            Background = StalkerTheme.BrushBorder,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // Rows
        for (var r = 0; r < vm.Rows.Count; r++)
        {
            var row = vm.Rows[r];
            var isAlt = r % 2 == 1;

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("200,80,80,80,80,80,80,80"),
                Margin = new Thickness(8, 4, 8, 4),
            };

            // Operation name and description
            var opPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            opPanel.Children.Add(new TextBlock
            {
                Text = row.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Foreground = StalkerTheme.BrushTextPrimary,
            });
            opPanel.Children.Add(new TextBlock
            {
                Text = row.Description,
                FontSize = 10,
                Foreground = StalkerTheme.BrushTextMuted,
            });
            rowGrid.Children.Add(opPanel);

            // Format Cells
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                var badge = new Border
                {
                    Background = cell.BadgeBackground,
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = cell.StatusText,
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = cell.BadgeForeground,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                };
                ToolTip.SetTip(badge, $"{row.Name} — {cell.FormatId}:\n{cell.TooltipText}");

                Grid.SetColumn(badge, c + 1);
                rowGrid.Children.Add(badge);
            }

            var rowBorder = new Border
            {
                Background = isAlt ? StalkerTheme.BrushBgElevated : Brushes.Transparent,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(0, 2),
                Child = rowGrid,
            };
            tableStack.Children.Add(rowBorder);
        }

        var tableScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = tableStack,
        };
        stack.Children.Add(StalkerTheme.Card(tableScroll, L.T("Матрица поддерживаемых возможностей")));

        // 3. Legend Card
        var legendStack = new StackPanel { Spacing = 8 };
        var legendRow = new WrapPanel { Orientation = Orientation.Horizontal };

        legendRow.Children.Add(MakeLegendItem(L.T("Запись (Verified)"), StalkerTheme.BrushSuccess, new SolidColorBrush(Color.Parse("#1A2E1A")), L.T("Полная поддержка чтения и записи, верифицировано тестами.")));
        legendRow.Children.Add(MakeLegendItem(L.T("Эксперим. (Experimental)"), StalkerTheme.BrushWarning, new SolidColorBrush(Color.Parse("#332A15")), L.T("Поддержка в формате реализована, ожидается подтверждение в игре.")));
        legendRow.Children.Add(MakeLegendItem(L.T("Чтение (Research)"), new SolidColorBrush(Color.Parse("#64B5F6")), new SolidColorBrush(Color.Parse("#16283B")), L.T("Режим только для чтения.")));
        legendRow.Children.Add(MakeLegendItem(L.T("Нет (Unsupported)"), StalkerTheme.BrushTextMuted, new SolidColorBrush(Color.Parse("#1E201C")), L.T("Механика отсутствует в игре или не поддерживается.")));
        legendRow.Children.Add(MakeLegendItem(L.T("Блок UI"), StalkerTheme.BrushAccentAmber, new SolidColorBrush(Color.Parse("#2B2215")), L.T("Поддержано в ядре, но отключено в UI в целях безопасности.")));

        legendStack.Children.Add(legendRow);

        var note = new TextBlock
        {
            Text = L.T("Запись сейвов S.T.A.L.K.E.R. 2 выключена, пока изменения не проверены в самой игре."),
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            FontStyle = FontStyle.Italic,
            Margin = new Thickness(4, 4, 0, 0),
        };
        legendStack.Children.Add(note);

        stack.Children.Add(StalkerTheme.Card(legendStack, L.T("Обозначения")));

        scroll.Content = stack;
        return scroll;
    }

    private static Control MakeLegendItem(string label, IBrush fg, IBrush bg, string description)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 16, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var badge = new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = fg,
            },
        };
        panel.Children.Add(badge);

        panel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return panel;
    }
}
