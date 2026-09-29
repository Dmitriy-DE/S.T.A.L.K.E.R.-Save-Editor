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

public static class StashesView
{
    public static Control Build(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(16),
        };

        var hintCard = new StackPanel { Spacing = 4 };
        hintCard.Children.Add(new TextBlock
        {
            Text = L.T("Отмеченные галочками предметы будут перенесены из тайников в инвентарь персонажа при сохранении. Предмет из рюкзака можно положить в тайник, а новый предмет — создать прямо в тайнике."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(StalkerTheme.Card(hintCard, L.T("Управление тайниками Зоны")));

        var stashesList = new ItemsControl
        {
            ItemTemplate = StalkerTheme.Template<StashViewModel>(stash => MakeStashCard(vm, stash)),
        };
        stashesList.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedSave.Stashes"));

        var scroll = new ScrollViewer
        {
            Content = stashesList,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        return root;
    }

    private static Control MakeStashCard(SaveLibraryViewModel vm, StashViewModel stash)
    {
        var stack = new StackPanel { Spacing = 6 };

        var itemsControl = new ItemsControl
        {
            ItemsSource = stash.Items,
            ItemTemplate = StalkerTheme.Template<StashItemViewModel>(item =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(4, 2),
                };

                var itemText = new TextBlock
                {
                    Text = $"{item.DisplayName} {item.CountDisplay}",
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                row.Children.Add(itemText);

                var takeChk = new CheckBox
                {
                    Content = L.T("В рюкзак"),
                    FontSize = 11,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                takeChk.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(StashItemViewModel.IsTaken))
                {
                    Source = item,
                    Mode = BindingMode.TwoWay,
                });
                takeChk.Bind(CheckBox.IsEnabledProperty, new Binding(nameof(StashItemViewModel.CanEdit))
                {
                    Source = item,
                });
                takeChk.Bind(ToolTip.TipProperty, new Binding(nameof(StashItemViewModel.DisabledReason))
                {
                    Source = item,
                });
                takeChk.Click += (_, _) =>
                {
                    if (item.CanEdit) vm.TakeStashItem(item);
                };
                Grid.SetColumn(takeChk, 1);
                row.Children.Add(takeChk);

                return row;
            }),
        };

        stack.Children.Add(itemsControl);

        stack.Children.Add(new ItemsControl
        {
            ItemsSource = stash.Pending,
            ItemTemplate = new FuncDataTemplate<string>((line, _) => new TextBlock
            {
                Text = line,
                FontSize = 12,
                Foreground = StalkerTheme.BrushAccentAmber,
                Margin = new Thickness(4, 1),
            }),
        });

        var putRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var inventoryPick = new ComboBox
        {
            PlaceholderText = L.T("Предмет из рюкзака"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<InventoryLineViewModel>((line, _) => new TextBlock { Text = line?.Name ?? string.Empty }),
        };
        inventoryPick.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedSave.Inventory") { Source = vm });
        putRow.Children.Add(inventoryPick);

        var putButton = StalkerTheme.StalkerButton(L.T("В ТАЙНИК"), isPrimary: false, minWidth: 110);
        putButton.Bind(Button.IsEnabledProperty, new Binding("SelectedSave.CanAddItems") { Source = vm });
        putButton.Bind(ToolTip.TipProperty, new Binding("SelectedSave.AddItemsDisabledReason") { Source = vm });
        putButton.Click += (_, _) =>
        {
            if (inventoryPick.SelectedItem is InventoryLineViewModel line) vm.ToggleStashPut(line, stash);
        };
        Grid.SetColumn(putButton, 1);
        putRow.Children.Add(putButton);

        var createButton = StalkerTheme.StalkerButton(L.T("+ СОЗДАТЬ В ТАЙНИКЕ"), isPrimary: true, minWidth: 170);
        createButton.Bind(Button.IsEnabledProperty, new Binding("SelectedSave.CanAddItems") { Source = vm });
        createButton.Bind(ToolTip.TipProperty, new Binding("SelectedSave.AddItemsDisabledReason") { Source = vm });
        createButton.Click += (_, _) =>
        {
            if (vm.SelectedSave is not { CanAddItems: true }) return;
            var dialog = new AddItemDialog(vm.CreateAddItemDialog(), vm, "stash:" + stash.Handle.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is not null)
            {
                dialog.ShowDialog(desktop.MainWindow);
            }
        };
        Grid.SetColumn(createButton, 2);
        putRow.Children.Add(createButton);
        stack.Children.Add(putRow);

        return StalkerTheme.Card(stack, stash.HeaderDisplay, margin: new Thickness(0, 0, 0, 10));
    }
}
