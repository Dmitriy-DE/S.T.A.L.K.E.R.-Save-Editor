using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class GameFixesView : UserControl
{
    public GameFixesView(GameFixesViewModel viewModel)
    {
        DataContext = viewModel;
        Content = Build(viewModel);
    }

    private static Control Build(GameFixesViewModel viewModel)
    {
        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"),
            Margin = new Thickness(24),
            RowSpacing = 14,
        };
        page.Children.Add(new TextBlock
        {
            Text = L.T("ИСПРАВЛЕНИЯ ИГРЫ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });

        var targetRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        targetRow.Children.Add(new TextBlock
        {
            Text = L.T("ИГРА"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StalkerTheme.BrushTextSecondary,
        });
        var target = new ComboBox
        {
            ItemsSource = viewModel.Targets,
            ItemTemplate = new FuncDataTemplate<GameTargetOption>((option, _) => new TextBlock { Text = option?.Title }),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        target.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(GameFixesViewModel.SelectedTarget))
        {
            Mode = BindingMode.TwoWay,
            Source = viewModel,
        });
        Grid.SetColumn(target, 1);
        targetRow.Children.Add(target);
        Grid.SetRow(targetRow, 1);
        page.Children.Add(targetRow);

        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        var path = new TextBox
        {
            Watermark = L.T("ПАПКА ИГРЫ (РУЧНОЙ ВЫБОР)"),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        path.Bind(TextBox.TextProperty, new Binding(nameof(GameFixesViewModel.GameDirectory))
        {
            Mode = BindingMode.TwoWay,
            Source = viewModel,
        });
        pathRow.Children.Add(path);

        var browse = StalkerTheme.StalkerButton(L.T("Обзор…"), isPrimary: false, minWidth: 90);
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(browse)?.StorageProvider is not { CanPickFolder: true } storage) return;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L.T("ПАПКА ИГРЫ (РУЧНОЙ ВЫБОР)"),
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].Path.LocalPath is { } selectedPath)
                viewModel.GameDirectory = selectedPath;
        };
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        var checkInstallation = StalkerTheme.StalkerButton(L.T("ПРОВЕРИТЬ СОВМЕСТИМОСТЬ"), isPrimary: true, minWidth: 180);
        checkInstallation.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.CheckInstallationCommand)) { Source = viewModel });
        Grid.SetColumn(checkInstallation, 2);
        pathRow.Children.Add(checkInstallation);
        Grid.SetRow(pathRow, 2);
        page.Children.Add(pathRow);

        var statusPanel = new StackPanel { Spacing = 5 };
        var status = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.CatalogueStatus)) { Source = viewModel });
        statusPanel.Children.Add(status);
        var compatibility = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        compatibility.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.CompatibilityStatus)) { Source = viewModel });
        statusPanel.Children.Add(compatibility);
        var preset = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        preset.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.PresetStatus)) { Source = viewModel });
        statusPanel.Children.Add(preset);
        var presetChanges = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        presetChanges.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.PresetChangeStatus)) { Source = viewModel });
        statusPanel.Children.Add(presetChanges);
        var operationStatus = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        operationStatus.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.Status)) { Source = viewModel });
        statusPanel.Children.Add(operationStatus);
        Grid.SetRow(statusPanel, 3);
        page.Children.Add(statusPanel);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2*"), ColumnSpacing = 14 };
        var catalogue = new StackPanel { Spacing = 10 };
        catalogue.Children.Add(new TextBlock
        {
            Text = L.T("КАТЕГОРИИ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        var categoryList = new ItemsControl
        {
            ItemsSource = viewModel.Categories,
            ItemTemplate = new FuncDataTemplate<GameFixCategoryRow>((row, _) => CreateCategoryRow(row), true),
        };
        catalogue.Children.Add(new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = categoryList,
        });
        catalogue.Children.Add(new TextBlock
        {
            Text = L.T("ДОСТУПНЫЕ ИСПРАВЛЕНИЯ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        var fixes = new ListBox
        {
            ItemsSource = viewModel.Fixes,
            ItemTemplate = new FuncDataTemplate<GameFixEntry>((entry, _) => CreateFixRow(entry), true),
            MinHeight = 100,
            MaxHeight = 260,
            Background = StalkerTheme.BrushBgPanel,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
        };
        fixes.Bind(ListBox.SelectedItemProperty, new Binding(nameof(GameFixesViewModel.SelectedFix), BindingMode.TwoWay)
        {
            Source = viewModel,
        });
        catalogue.Children.Add(fixes);
        Grid.SetColumn(catalogue, 0);
        content.Children.Add(catalogue);

        var details = new StackPanel { Spacing = 9 };
        var selectedTitle = new TextBlock
        {
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
        };
        selectedTitle.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.SelectedTitle)) { Source = viewModel });
        details.Children.Add(selectedTitle);

        var id = new TextBlock { FontSize = 11, Foreground = StalkerTheme.BrushTextMuted, TextWrapping = TextWrapping.Wrap };
        id.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.SelectedId)) { Source = viewModel });
        details.Children.Add(id);
        var classification = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        classification.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.SelectedClassification)) { Source = viewModel });
        details.Children.Add(classification);

        AddDetail(details, L.T("ПРОБЛЕМА"), nameof(GameFixesViewModel.SelectedProblem), viewModel);
        AddDetail(details, L.T("ИЗМЕНЕНИЕ"), nameof(GameFixesViewModel.SelectedDescription), viewModel);
        AddDetail(details, L.T("ПОДДЕРЖИВАЕМЫЕ STEAM-СБОРКИ"), nameof(GameFixesViewModel.SelectedBuilds), viewModel);
        AddDetail(details, L.T("ЗАТРАГИВАЕМЫЕ ФАЙЛЫ"), nameof(GameFixesViewModel.SelectedFiles), viewModel);
        AddDetail(details, L.T("ИСТОЧНИК"), nameof(GameFixesViewModel.SelectedSource), viewModel);

        var presetActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var recommendedPreset = StalkerTheme.StalkerButton(L.T("ПРИМЕНИТЬ: РЕКОМЕНДУЕМЫЕ"), isPrimary: true, minWidth: 190);
        recommendedPreset.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.ApplyRecommendedPresetCommand)) { Source = viewModel });
        presetActions.Children.Add(recommendedPreset);
        var essentialPreset = StalkerTheme.StalkerButton(L.T("ПРИМЕНИТЬ: ОБЯЗАТЕЛЬНЫЕ"), isPrimary: false, minWidth: 190);
        essentialPreset.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.ApplyEssentialPresetCommand)) { Source = viewModel });
        presetActions.Children.Add(essentialPreset);
        var allSafePreset = StalkerTheme.StalkerButton(L.T("ПРИМЕНИТЬ: ВСЕ БЕЗОПАСНЫЕ"), isPrimary: false, minWidth: 190);
        allSafePreset.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.ApplyAllSafePresetCommand)) { Source = viewModel });
        presetActions.Children.Add(allSafePreset);
        var footer = new StackPanel { Spacing = 4, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(presetActions);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var install = StalkerTheme.StalkerButton(L.T("УСТАНОВИТЬ ВЫБРАННОЕ"), isPrimary: true, minWidth: 190);
        install.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.InstallCommand)) { Source = viewModel });
        actions.Children.Add(install);
        var update = StalkerTheme.StalkerButton(L.T("ОБНОВИТЬ ВЫБРАННОЕ"), isPrimary: false, minWidth: 165);
        update.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.UpdateCommand)) { Source = viewModel });
        update.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameFixesViewModel.ShowUpdateButton)) { Source = viewModel });
        actions.Children.Add(update);
        var remove = StalkerTheme.StalkerButton(L.T("УДАЛИТЬ И ВОССТАНОВИТЬ"), isPrimary: false, minWidth: 190);
        remove.Bind(Button.CommandProperty, new Binding(nameof(GameFixesViewModel.RemoveCommand)) { Source = viewModel });
        actions.Children.Add(remove);
        footer.Children.Add(actions);
        footer.Children.Add(new TextBlock
        {
            Text = L.T("ИСПРАВЛЕНИЕ ЗАПИСЫВАЕТСЯ ТОЛЬКО ПО НАЖАТИЮ КНОПКИ. ПРИ ИЗМЕНЕНИИ УПРАВЛЯЕМОГО ФАЙЛА УДАЛЕНИЕ ОСТАНОВИТСЯ, НЕ ПЕРЕЗАПИСЫВАЯ ЕГО."),
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        var detailCard = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14),
            // Actions stay pinned under the scrolling details, so they are never below the fold.
            Child = new DockPanel
            {
                Children =
                {
                    Dock(footer, Avalonia.Controls.Dock.Bottom),
                    new ScrollViewer { Content = details, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
                },
            },
        };
        Grid.SetColumn(detailCard, 1);
        content.Children.Add(detailCard);
        Grid.SetRow(content, 4);
        page.Children.Add(content);
        return page;
    }

    private static Control Dock(Control control, Avalonia.Controls.Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static void AddDetail(StackPanel panel, string label, string propertyName, GameFixesViewModel viewModel)
    {
        var section = new StackPanel { Spacing = 3 };
        section.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextMuted,
        });
        var value = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        value.Bind(TextBlock.TextProperty, new Binding(propertyName) { Source = viewModel });
        section.Children.Add(value);
        panel.Children.Add(section);
    }

    private static Control CreateFixRow(GameFixEntry? entry)
    {
        if (entry is null) return new Border();
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{entry.Id} · {entry.MaturityName} · {entry.StateName}",
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8),
            Child = content,
        };
    }

    private static Control CreateCategoryRow(GameFixCategoryRow? row)
    {
        if (row is null) return new Border();
        var color = row.Category == GameFixCategory.Experimental
            ? StalkerTheme.BrushTextMuted
            : StalkerTheme.BrushTextPrimary;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 6) };
        grid.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontWeight = FontWeight.SemiBold,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var count = new TextBlock
        {
            Text = row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }
}
