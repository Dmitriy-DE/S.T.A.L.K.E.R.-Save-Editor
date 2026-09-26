using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop;

public sealed class MainWindow : Window
{
    public MainWindow(SaveLibraryViewModel? viewModel = null)
    {
        Title = "S.T.A.L.K.E.R. Save Editor";
        Width = 1180;
        Height = 760;
        MinWidth = 820;
        MinHeight = 540;
        DataContext = viewModel ?? new SaveLibraryViewModel();
        Content = BuildContent();
    }

    private static Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("320,*"),
        };

        var headingText = new StackPanel { Orientation = Orientation.Horizontal };
        headingText.Children.Add(new TextBlock
        {
            Text = "Библиотека сейвов",
            FontSize = 25,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        headingText.Children.Add(new TextBlock
        {
            Text = "Только чтение",
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        });
        var refresh = new Button
        {
            Content = "Обновить",
            MinWidth = 112,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        refresh.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RefreshCommand)));
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(24, 20, 24, 18),
        };
        heading.Children.Add(headingText);
        Grid.SetColumn(refresh, 1);
        heading.Children.Add(refresh);
        Grid.SetColumnSpan(heading, 2);
        root.Children.Add(heading);

        var savesHeading = new TextBlock
        {
            Text = "Найденные сейвы",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(16, 0, 16, 10),
        };
        var saveList = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SaveFileSummary>((item, _) => new Border
            {
                Padding = new Thickness(10, 8),
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = item.DisplayName, FontWeight = FontWeight.Medium },
                        new TextBlock { Text = item.ReleaseName, FontSize = 12, Opacity = 0.7 },
                    },
                },
            }),
            Margin = new Thickness(8, 0, 8, 8),
        };
        saveList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Saves)));
        saveList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedSave))
        {
            Mode = BindingMode.TwoWay,
        });
        var libraryPane = new DockPanel();
        DockPanel.SetDock(savesHeading, Dock.Top);
        libraryPane.Children.Add(savesHeading);
        libraryPane.Children.Add(saveList);
        var libraryBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 1, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#30343B")),
            Child = libraryPane,
        };
        Grid.SetRow(libraryBorder, 1);
        Grid.SetColumn(libraryBorder, 0);
        root.Children.Add(libraryBorder);

        var emptyState = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Выберите сейв",
                    FontSize = 20,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Данные будут открыты только для чтения.",
                    Opacity = 0.72,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        emptyState.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.HasNoSelection)));

        var detailName = BoundText(nameof(SaveLibraryViewModel.SelectedSaveName), 23, FontWeight.SemiBold);
        var detailRelease = BoundText(nameof(SaveLibraryViewModel.SelectedReleaseName), 14, FontWeight.Normal);
        detailRelease.Opacity = 0.72;
        var moneyLabel = BoundText(nameof(SaveLibraryViewModel.SelectedMoneyDisplay), 18, FontWeight.Medium);
        var inventoryHeading = new TextBlock
        {
            Text = "Инвентарь",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 24, 0, 8),
        };
        var inventory = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<InventoryLineViewModel>((item, _) => MakeInventoryLine(item)),
        };
        inventory.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.SelectedInventory)));
        var detailContent = new StackPanel
        {
            Spacing = 8,
            Children = { detailName, detailRelease, moneyLabel, inventoryHeading, inventory },
        };
        detailContent.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.HasSelection)));

        var details = new Grid
        {
            Margin = new Thickness(28),
            Children = { emptyState, detailContent },
        };
        Grid.SetRow(details, 1);
        Grid.SetColumn(details, 1);
        root.Children.Add(details);

        return root;
    }

    private static Control MakeInventoryLine(InventoryLineViewModel item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 5),
        };
        grid.Children.Add(new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap });
        var count = new TextBlock
        {
            Text = item.CountDisplay,
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0.76,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#30343B")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
        return border;
    }

    private static TextBlock BoundText(string property, double fontSize, FontWeight weight) => new()
    {
        [!TextBlock.TextProperty] = new Binding(property),
        FontSize = fontSize,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
    };
}
