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

public sealed class AddItemDialog : Window
{
    public AddItemDialog(AddItemViewModel addVm, SaveLibraryViewModel parentVm, string destination = "inventory")
    {
        Title = L.T("Добавить предмет в инвентарь");
        Width = 520;
        Height = 560;
        Background = StalkerTheme.BrushBgBase;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent(addVm, parentVm, destination, Close);
    }

    /// <summary>Shared catalog editor, also hosted inline by Inventory so adding is not a blocking flow.</summary>
    public static Control BuildContent(
        AddItemViewModel addVm,
        SaveLibraryViewModel parentVm,
        string destination,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(addVm);
        ArgumentNullException.ThrowIfNull(parentVm);
        ArgumentNullException.ThrowIfNull(close);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(16),
            RowSpacing = 8,
        };

        var searchBox = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = L.T("Поиск по названию или ключу секции…"),
            MinHeight = 36,
        };
        Avalonia.Automation.AutomationProperties.SetName(searchBox, L.T("Поиск по каталогу предметов"));
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(AddItemViewModel.SearchText))
        {
            Source = addVm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        root.Children.Add(searchBox);

        var itemsList = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ItemsSource = addVm.FilteredItems,
            ItemTemplate = StalkerTheme.RecyclingTemplate<CatalogItemEntry>(() =>
            {
                var row = new StackPanel { Spacing = 2, Margin = new Thickness(8, 6) };
                var name = new TextBlock
                {
                    FontWeight = FontWeight.Medium,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                };
                name.Bind(TextBlock.TextProperty, new Binding(nameof(CatalogItemEntry.DisplayName)));
                row.Children.Add(name);

                var key = new TextBlock { FontSize = 10, Foreground = StalkerTheme.BrushTextMuted };
                key.Bind(TextBlock.TextProperty, new Binding(nameof(CatalogItemEntry.Key)));
                row.Children.Add(key);
                return row;
            }),
        };
        itemsList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(AddItemViewModel.SelectedItem))
        {
            Source = addVm,
            Mode = BindingMode.TwoWay,
        });

        var noResults = new TextBlock
        {
            Text = L.T("Предметы не найдены."),
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16),
        };
        var catalogHost = new Grid { Children = { itemsList, noResults } };
        void UpdateCatalogState()
        {
            var hasItems = addVm.FilteredItems.Count > 0;
            itemsList.IsVisible = hasItems;
            noResults.IsVisible = !hasItems;
        }
        addVm.FilteredItems.CollectionChanged += (_, _) => UpdateCatalogState();
        UpdateCatalogState();
        Grid.SetRow(catalogHost, 1);
        root.Children.Add(catalogHost);

        var qtyRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,120"),
            Margin = new Thickness(0, 4, 0, 4),
            ColumnSpacing = 8,
        };
        qtyRow.Children.Add(new TextBlock
        {
            Text = L.T("Количество:"),
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });
        var qtyBox = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var addButton = StalkerTheme.StalkerButton(L.T("Добавить"), isPrimary: true, minWidth: 110);
        addButton.Bind(Button.IsEnabledProperty, new Binding(nameof(AddItemViewModel.SelectedItem))
        {
            Source = addVm,
            Converter = new NullToBoolConverter(),
        });
        addButton.Click += (_, _) =>
        {
            if (addVm.SelectedItem is not { } selected) return;
            parentVm.StageItemAddition(selected.Key, addVm.Quantity, destination);
            close();
        };
        buttonRow.Children.Add(addButton);

        var cancelButton = StalkerTheme.StalkerButton(L.T("Отмена"), isPrimary: false, minWidth: 100);
        cancelButton.Click += (_, _) => close();
        buttonRow.Children.Add(cancelButton);
        Grid.SetRow(buttonRow, 3);
        root.Children.Add(buttonRow);
        return root;
    }

    private sealed class NullToBoolConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not null;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
