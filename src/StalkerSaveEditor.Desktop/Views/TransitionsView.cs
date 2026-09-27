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

        var noteStack = new StackPanel { Spacing = 6 };
        noteStack.Children.Add(new TextBlock
        {
            Text = "Переходы между локациями (Level Changers) требуют подтверждённого Core API для безопасного чтения и записи.",
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        });
        noteStack.Children.Add(new TextBlock
        {
            Text = "Согласно правилу AGENTS.md («Unknown or ambiguous fields stay read-only»), недокументированные или неподтверждённые структуры остаются закрытыми для показа и редактирования до добавления официальной поддержки в Core (запрос API оформлен в Issue #55).",
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(StalkerTheme.Card(noteStack, "Переходы между локациями"));

        var emptyPanel = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 48, 0, 0),
        };

        emptyPanel.Children.Add(new TextBlock
        {
            Text = "ДАННЫЕ О ПЕРЕХОДАХ НЕДОСТУПНЫ",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        emptyPanel.Children.Add(new TextBlock
        {
            Text = "В текущем сохранении подтверждённые точки перехода не извлечены.",
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        Grid.SetRow(emptyPanel, 1);
        root.Children.Add(emptyPanel);

        return root;
    }
}
