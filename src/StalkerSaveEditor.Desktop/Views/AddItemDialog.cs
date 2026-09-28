using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class AddItemDialog : Window
{
    public AddItemDialog(AddItemViewModel addVm, SaveLibraryViewModel parentVm)
    {
        Title = "Добавить предмет в инвентарь";
        Width = 520;
        Height = 560;
        Background = StalkerTheme.BrushBgBase;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(16),
        };

        // Search Box
        var searchBox = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = "Поиск по названию или ключу секции…",
            Margin = new Thickness(0, 0, 0, 10),
        };
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(AddItemViewModel.SearchText))
        {
            Source = addVm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        root.Children.Add(searchBox);

        // Items List
        var itemsList = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ItemsSource = addVm.FilteredItems,
            ItemTemplate = StalkerTheme.Template<CatalogItemEntry>(item =>
            {
                var row = new StackPanel { Spacing = 2, Margin = new Thickness(6, 4) };
                row.Children.Add(new TextBlock
                {
                    Text = item.DisplayName,
                    FontWeight = FontWeight.Medium,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 13,
                });
                row.Children.Add(new TextBlock
                {
                    Text = item.Key,
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextMuted,
                });
                return row;
            }),
        };
        itemsList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(AddItemViewModel.SelectedItem))
        {
            Source = addVm,
            Mode = BindingMode.TwoWay,
        });
        Grid.SetRow(itemsList, 1);
        root.Children.Add(itemsList);

        // Quantity Row
        var qtyRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,80,*"),
            Margin = new Thickness(0, 10, 0, 12),
        };
        qtyRow.Children.Add(new TextBlock
        {
            Text = "Количество:",
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });
        var qtyBox = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Text = "1",
        };
        qtyBox.Bind(TextBox.TextProperty, new Binding(nameof(AddItemViewModel.Quantity))
        {
            Source = addVm,
            Mode = BindingMode.TwoWay,
        });
        Grid.SetColumn(qtyBox, 1);
        qtyRow.Children.Add(qtyBox);
        Grid.SetRow(qtyRow, 2);
        root.Children.Add(qtyRow);

        // Buttons
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };

        var addBtn = StalkerTheme.StalkerButton("Добавить", isPrimary: true, minWidth: 110);
        addBtn.Click += (_, _) =>
        {
            if (addVm.SelectedItem is not null)
            {
                parentVm.StageItemAddition(addVm.SelectedItem.Key, addVm.Quantity);
                Close();
            }
        };
        btnRow.Children.Add(addBtn);

        var cancelBtn = StalkerTheme.StalkerButton("Отмена", isPrimary: false, minWidth: 100);
        cancelBtn.Click += (_, _) => Close();
        btnRow.Children.Add(cancelBtn);

        Grid.SetRow(btnRow, 3);
        root.Children.Add(btnRow);

        Content = root;
    }
}
