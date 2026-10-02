using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class EncyclopediaView
{
    public static Control Build(EncyclopediaViewModel viewModel)
    {
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(new TextBlock
        {
            Text = L.T("ЭНЦИКЛОПЕДИЯ ПРЕДМЕТОВ"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });

        var selectGame = new ComboBox
        {
            MinWidth = 280,
            ItemsSource = viewModel.AvailableGames,
            DisplayMemberBinding = new Binding("Value"),
            SelectedValueBinding = new Binding("Key"),
        };
        selectGame.Bind(SelectingItemsControl.SelectedValueProperty, new Binding(nameof(EncyclopediaViewModel.SelectedGameReleaseId))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
        });
        header.Children.Add(selectGame);
        header.Children.Add(new TextBlock
        {
            Text = L.T("У каждой записи показаны имя, значок, вес, цена и секция из файлов установленной игры."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });

        // A game has hundreds of items: only the rows on screen exist, and a row asks for its icon when it is built.
        var items = new ListBox
        {
            ItemsSource = viewModel.Items,
            ItemTemplate = StalkerTheme.Template<EncyclopediaItemViewModel>(item => BuildRow(viewModel, item)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        items.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(0)) },
        });
        var empty = new TextBlock
        {
            Text = L.T("Нет загруженного каталога установленной игры."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        };
        empty.Bind(Visual.IsVisibleProperty, new Binding(nameof(EncyclopediaViewModel.HasItems))
        {
            Source = viewModel,
            Converter = new Avalonia.Data.Converters.FuncValueConverter<bool, bool>(hasItems => !hasItems),
        });

        var status = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(EncyclopediaViewModel.StatusMessage)) { Source = viewModel });

        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 12, Margin = new Thickness(20) };
        page.Children.Add(header);
        Grid.SetRow(status, 1);
        page.Children.Add(status);
        Grid.SetRow(empty, 2);
        page.Children.Add(empty);
        Grid.SetRow(items, 3);
        page.Children.Add(items);
        return page;
    }

    private static Control BuildRow(EncyclopediaViewModel viewModel, EncyclopediaItemViewModel item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("52,*,110,110,Auto,Auto"),
            Margin = new Thickness(0, 3),
        };

        var icon = new Image { Width = 46, Height = 46, Stretch = Avalonia.Media.Stretch.Uniform };
        ItemIconService.Show(icon, item.ReleaseId, item.Key);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var name = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        name.Children.Add(new TextBlock
        {
            Text = L.T("Вес: {0} · Цена: {1} · Секция: {2}", item.WeightDisplay, item.CostDisplay, item.Section),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var category = new TextBlock
        {
            Text = item.Category ?? "—",
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(category, 2);
        grid.Children.Add(category);

        var add = StalkerTheme.StalkerButton(L.T("В сохранение"), isPrimary: true, minWidth: 112);
        add.Bind(Button.IsEnabledProperty, new Binding(nameof(EncyclopediaItemViewModel.CanAddToSave)) { Source = item });
        add.Bind(ToolTip.TipProperty, new Binding(nameof(EncyclopediaItemViewModel.AddDisabledReason)) { Source = item });
        add.Click += (_, _) => viewModel.AddToSave(item);
        Grid.SetColumn(add, 4);
        grid.Children.Add(add);

        var spawn = StalkerTheme.StalkerButton(L.T("В игру"), isPrimary: false, minWidth: 86);
        spawn.Bind(Button.IsEnabledProperty, new Binding(nameof(EncyclopediaItemViewModel.CanSpawnViaCompanion)) { Source = item });
        spawn.Bind(ToolTip.TipProperty, new Binding(nameof(EncyclopediaItemViewModel.SpawnDisabledReason)) { Source = item });
        spawn.Click += async (_, _) => await viewModel.SpawnViaCompanionAsync(item);
        Grid.SetColumn(spawn, 5);
        grid.Children.Add(spawn);

        return StalkerTheme.Card(grid, padding: new Thickness(10));
    }
}
