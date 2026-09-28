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
            Text = L.T("Отмеченные галочками предметы будут перенесены из тайников в инвентарь персонажа при сохранении."),
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
        return StalkerTheme.Card(stack, stash.HeaderDisplay, margin: new Thickness(0, 0, 0, 10));
    }
}
