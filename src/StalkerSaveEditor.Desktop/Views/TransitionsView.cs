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

public static class TransitionsView
{
    public static Control Build()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(16),
        };

        // 1. Info Card
        var noteStack = new StackPanel { Spacing = 6 };
        noteStack.Children.Add(new TextBlock
        {
            Text = L.T("Объекты переходов между локациями (Level Changers) из реестра X-Ray."),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        });
        noteStack.Children.Add(new TextBlock
        {
            Text = L.T("Данные прочитаны из сохранения. Поля переходов не описаны, поэтому они только для чтения."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(StalkerTheme.Card(noteStack, L.T("Переходы между локациями (Read-Only)")));

        // Relocation (TP, experimental): only destinations of the level changers in this save.
        var relocation = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        relocation.Children.Add(new TextBlock
        {
            Text = L.T("ПЕРЕНОС ПЕРСОНАЖА (ЭКСПЕРИМЕНТАЛЬНО)"),
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        relocation.Children.Add(new TextBlock
        {
            Text = L.T("Только точки, куда игра сама ставит персонажа после перехода. Сохранение записывается сразу, с бэкапом. В игре это ещё не проверено."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var here = new TextBlock { Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12 };
        here.Bind(TextBlock.TextProperty, new Binding("SelectedSave.ActorLocationDisplay") { StringFormat = L.T("Сейчас: {0}") });
        relocation.Children.Add(here);
        var pickRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var anchors = new ComboBox
        {
            PlaceholderText = L.T("Куда перенести"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<RelocationAnchorViewModel>((anchor, _) => new TextBlock { Text = anchor?.Display ?? string.Empty }),
        };
        anchors.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedSave.RelocationAnchors"));
        anchors.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedRelocationAnchor)) { Mode = BindingMode.TwoWay });
        pickRow.Children.Add(anchors);
        var move = StalkerTheme.StalkerButton(L.T("ПЕРЕНЕСТИ"), isPrimary: true, minWidth: 140);
        move.Click += (_, _) => (move.DataContext as SaveLibraryViewModel)?.RelocateActor();
        Grid.SetColumn(move, 1);
        pickRow.Children.Add(move);
        relocation.Children.Add(pickRow);
        relocation.Bind(Visual.IsVisibleProperty, new Binding("SelectedSave.CanRelocate"));
        noteStack.Children.Add(relocation);

        // 2. Empty State
        var emptyPanel = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 48, 0, 0),
        };
        emptyPanel.Children.Add(new TextBlock
        {
            Text = L.T("ТОЧКИ ПЕРЕХОДОВ ОТСУТСТВУЮТ"),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        emptyPanel.Children.Add(new TextBlock
        {
            Text = L.T("В объектах реестра данного сохранения нет записей типа level_changer."),
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        emptyPanel.Bind(Visual.IsVisibleProperty, new Binding("SelectedSave.HasNoTransitions"));
        Grid.SetRow(emptyPanel, 1);
        root.Children.Add(emptyPanel);

        // 3. Transitions List Container
        var listContainer = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(0, 12, 0, 0),
        };
        listContainer.Bind(Visual.IsVisibleProperty, new Binding("SelectedSave.HasTransitions"));

        // Header Row
        var headerBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 6),
        };
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*,120,100,80,110"),
        };
        headerGrid.Children.Add(MakeColHeader(L.T("ID В РЕЕСТРЕ"), 0));
        headerGrid.Children.Add(MakeColHeader(L.T("ИДЕНТИФИКАТОР / ТОЧКА"), 1));
        headerGrid.Children.Add(MakeColHeader(L.T("ТИП ОБЪЕКТА"), 2));
        headerGrid.Children.Add(MakeColHeader(L.T("РОДИТЕЛЬ"), 3));
        headerGrid.Children.Add(MakeColHeader(L.T("ВЕРСИЯ"), 4));
        headerGrid.Children.Add(MakeColHeader(L.T("СТАТУС"), 5));
        headerBorder.Child = headerGrid;
        listContainer.Children.Add(headerBorder);

        // Items List
        var itemsControl = new ItemsControl
        {
            ItemTemplate = StalkerTheme.Template<TransitionViewModel>(item => MakeTransitionRow(item)),
        };
        itemsControl.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedSave.Transitions"));

        var scroll = new ScrollViewer
        {
            Content = itemsControl,
        };
        Grid.SetRow(scroll, 1);
        listContainer.Children.Add(scroll);

        Grid.SetRow(listContainer, 2);
        root.Children.Add(listContainer);

        return root;
    }

    private static TextBlock MakeColHeader(string title, int col)
    {
        var text = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, col);
        return text;
    }

    private static Control MakeTransitionRow(TransitionViewModel item)
    {
        var border = new Border
        {
            Background = StalkerTheme.BrushBgElevated,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12, 9),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*,120,100,80,110"),
        };

        // 0. Handle
        var handleText = new TextBlock
        {
            Text = item.HandleDisplay,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(handleText);

        // 1. DisplayName
        var nameText = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        // 2. Type
        var typeText = new TextBlock
        {
            Text = item.TypeDisplay,
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(typeText, 2);
        grid.Children.Add(typeText);

        // 3. Parent
        var parentText = new TextBlock
        {
            Text = item.ParentDisplay,
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(parentText, 3);
        grid.Children.Add(parentText);

        // 4. Version
        var verText = new TextBlock
        {
            Text = item.VersionDisplay,
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(verText, 4);
        grid.Children.Add(verText);

        // 5. Read-only badge
        var roBadge = StalkerTheme.Badge(L.T("Только чтение"), StalkerTheme.BrushBgInput, StalkerTheme.BrushTextSecondary, 10);
        Grid.SetColumn(roBadge, 5);
        grid.Children.Add(roBadge);

        border.Child = grid;
        return border;
    }
}
