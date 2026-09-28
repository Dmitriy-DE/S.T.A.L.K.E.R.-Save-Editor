using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;

namespace StalkerSaveEditor.Desktop;

public sealed class MainWindow : Window
{
    private readonly SaveLibraryViewModel _viewModel;

    public MainWindow(SaveLibraryViewModel? viewModel = null)
    {
        _viewModel = viewModel ?? new SaveLibraryViewModel();
        DataContext = _viewModel;

        Title = "S.T.A.L.K.E.R. Save Editor";
        Width = 1260;
        Height = 820;
        MinWidth = 940;
        MinHeight = 600;
        Background = StalkerTheme.BrushBgBase;

        Content = BuildContent(_viewModel);
    }

    private static Control BuildContent(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = StalkerTheme.BrushBgBase,
        };

        // 1. Top Header Bar
        var topBar = BuildTopBar(vm);
        root.Children.Add(topBar);

        // 2. Middle Area: Left Saves Pane (300) + Right Workspace
        var middle = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("310,*"),
        };

        var savesPane = BuildSavesPane();
        middle.Children.Add(savesPane);

        var workspacePane = BuildWorkspacePane(vm);
        Grid.SetColumn(workspacePane, 1);
        middle.Children.Add(workspacePane);

        Grid.SetRow(middle, 1);
        root.Children.Add(middle);

        // 3. Bottom Status Bar
        var statusBar = BuildStatusBar();
        Grid.SetRow(statusBar, 2);
        root.Children.Add(statusBar);

        return root;
    }

    private static Control BuildTopBar(SaveLibraryViewModel vm)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(18, 12, 18, 12),
        };

        // App title & badge
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = "S.T.A.L.K.E.R. SAVE EDITOR",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var releaseBadge = StalkerTheme.Badge("X-Ray / S2", StalkerTheme.BrushBgElevated, StalkerTheme.BrushTextSecondary, 10);
        releaseBadge.Bind(TextBlock.TextProperty, new Binding("SelectedSave.ReleaseName"));
        titleStack.Children.Add(releaseBadge);
        grid.Children.Add(titleStack);

        // Right side: Undo, Redo, Discard, Save buttons
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var undoBtn = StalkerTheme.StalkerButton("↶ Отменить", isPrimary: false, minWidth: 90);
        undoBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.UndoCommand)));
        actions.Children.Add(undoBtn);

        var redoBtn = StalkerTheme.StalkerButton("↷ Повторить", isPrimary: false, minWidth: 90);
        redoBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RedoCommand)));
        actions.Children.Add(redoBtn);

        var discardBtn = StalkerTheme.StalkerButton("Сбросить", isPrimary: false, minWidth: 90);
        discardBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.DiscardDraftCommand)));
        actions.Children.Add(discardBtn);

        var refreshBtn = StalkerTheme.StalkerButton("Обновить", isPrimary: false, minWidth: 90);
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RefreshCommand)));
        actions.Children.Add(refreshBtn);

        var saveBtn = StalkerTheme.StalkerButton("СОХРАНИТЬ", isPrimary: true, minWidth: 120);
        saveBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.SaveCommand)));
        saveBtn.Bind(ToolTip.TipProperty, new Binding(nameof(SaveLibraryViewModel.SaveDisabledReason)));
        actions.Children.Add(saveBtn);

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var border = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
        return border;
    }

    private static Control BuildSavesPane()
    {
        var dock = new DockPanel();

        var header = new TextBlock
        {
            Text = "БИБЛИОТЕКА СОХРАНЕНИЙ",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.1,
            Margin = new Thickness(14, 12, 14, 8),
        };
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var saveList = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            ItemTemplate = new FuncDataTemplate<SaveFileSummary>((item, _) =>
            {
                var stack = new StackPanel { Spacing = 3, Margin = new Thickness(6, 4) };
                stack.Children.Add(new TextBlock
                {
                    Text = item.DisplayName,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 13,
                });

                var subRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                subRow.Children.Add(new TextBlock
                {
                    Text = item.ReleaseName,
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextSecondary,
                });
                var sizeText = new TextBlock
                {
                    Text = item.FileSizeDisplay,
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextMuted,
                };
                Grid.SetColumn(sizeText, 1);
                subRow.Children.Add(sizeText);
                stack.Children.Add(subRow);

                return stack;
            }),
        };
        saveList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Saves)));
        saveList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedSave))
        {
            Mode = BindingMode.TwoWay,
        });
        dock.Children.Add(saveList);

        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock,
        };
    }

    private static Control BuildWorkspacePane(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        // Nav Tabs Rail
        var navBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(14, 8, 14, 8),
        };

        navBar.Children.Add(MakeNavTab(vm, "ОБЗОР", "overview", nameof(SaveLibraryViewModel.IsOverviewTab)));
        navBar.Children.Add(MakeNavTab(vm, "ИНВЕНТАРЬ", "inventory", nameof(SaveLibraryViewModel.IsInventoryTab)));
        navBar.Children.Add(MakeNavTab(vm, "ФРАКЦИИ", "factions", nameof(SaveLibraryViewModel.IsFactionsTab)));
        navBar.Children.Add(MakeNavTab(vm, "ТАЙНИКИ", "stashes", nameof(SaveLibraryViewModel.IsStashesTab)));
        navBar.Children.Add(MakeNavTab(vm, "ПЕРЕХОДЫ", "transitions", nameof(SaveLibraryViewModel.IsTransitionsTab)));
        navBar.Children.Add(MakeNavTab(vm, "БЭКАПЫ", "backups", nameof(SaveLibraryViewModel.IsBackupsTab)));
        navBar.Children.Add(MakeNavTab(vm, "ВОЗМОЖНОСТИ", "capabilities", nameof(SaveLibraryViewModel.IsCapabilitiesTab)));
        navBar.Children.Add(MakeNavTab(vm, "КОМПАНЬОН", "companion", nameof(SaveLibraryViewModel.IsCompanionTab)));
        navBar.Children.Add(MakeNavTab(vm, "ОБЛАКО", "cloud", nameof(SaveLibraryViewModel.IsCloudTab)));
        navBar.Children.Add(MakeNavTab(vm, "НАСТРОЙКИ", "settings", nameof(SaveLibraryViewModel.IsSettingsTab)));

        var navBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = navBar,
        };
        root.Children.Add(navBorder);

        // Content Area
        var contentGrid = new Grid();

        // Empty state
        var emptyState = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "ВЫБЕРИТЕ СОХРАНЕНИЕ",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Выберите сейв из списка слева для просмотра и редактирования.",
                    Foreground = StalkerTheme.BrushTextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        emptyState.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShouldShowEmptyState)));
        contentGrid.Children.Add(emptyState);

        // Screens Container
        var screens = new Grid();

        // 1. Overview
        var overview = OverviewView.Build();
        overview.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowOverviewScreen)));
        screens.Children.Add(overview);

        // 2. Inventory
        var inventory = InventoryView.Build(vm);
        inventory.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowInventoryScreen)));
        screens.Children.Add(inventory);

        // 3. Factions
        var factions = FactionsView.Build(vm);
        factions.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowFactionsScreen)));
        screens.Children.Add(factions);

        // 4. Stashes
        var stashes = StashesView.Build(vm);
        stashes.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowStashesScreen)));
        screens.Children.Add(stashes);

        // 5. Transitions
        var transitions = TransitionsView.Build();
        transitions.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowTransitionsScreen)));
        screens.Children.Add(transitions);

        // 6. Backups
        var backups = BackupsView.Build(vm);
        backups.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowBackupsScreen)));
        screens.Children.Add(backups);

        // 7. Settings
        var settings = SettingsView.Build(vm.Settings);
        settings.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowSettingsScreen)));
        screens.Children.Add(settings);

        // 8. Capabilities
        var capabilities = CapabilitiesView.Build(vm.Capabilities);
        capabilities.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCapabilitiesScreen)));
        screens.Children.Add(capabilities);

        // 9. Companion
        var companion = new CompanionView { DataContext = vm.Companion };
        companion.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCompanionScreen)));
        screens.Children.Add(companion);

        // 10. Cloud
        var cloud = new CloudView { DataContext = vm.Cloud };
        cloud.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCloudScreen)));
        screens.Children.Add(cloud);

        contentGrid.Children.Add(screens);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        return root;
    }

    private static Button MakeNavTab(SaveLibraryViewModel vm, string label, string tabName, string isTabProperty)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(14, 7),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
        };
        btn.Bind(Button.BackgroundProperty, new Binding(isTabProperty)
        {
            Converter = new BoolToBrushConverter(StalkerTheme.BrushAccentAmber, StalkerTheme.BrushBgElevated),
        });
        btn.Bind(Button.ForegroundProperty, new Binding(isTabProperty)
        {
            Converter = new BoolToBrushConverter(new SolidColorBrush(Color.Parse("#0C0D0A")), StalkerTheme.BrushTextSecondary),
        });
        btn.Bind(Button.BorderBrushProperty, new Binding(isTabProperty)
        {
            Converter = new BoolToBrushConverter(StalkerTheme.BrushAccentAmber, StalkerTheme.BrushBorderSubtle),
        });
        btn.Click += (_, _) => vm.SelectedTab = tabName;
        return btn;
    }

    private static Control BuildStatusBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(14, 6, 14, 6),
        };

        var status = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.StatusMessage)));
        grid.Children.Add(status);

        var draftBadge = StalkerTheme.Badge("Сохранено", StalkerTheme.BrushBgElevated, StalkerTheme.BrushAccentDim, 10);
        draftBadge.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.DraftStatusText)));
        Grid.SetColumn(draftBadge, 1);
        grid.Children.Add(draftBadge);

        var sha = new TextBlock
        {
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        sha.Bind(TextBlock.TextProperty, new Binding("SelectedSave.SourceSha256")
        {
            StringFormat = "SHA: {0}",
        });
        Grid.SetColumn(sha, 2);
        grid.Children.Add(sha);

        var border = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
        return border;
    }

    private sealed class BoolToBrushConverter(IBrush trueBrush, IBrush falseBrush) : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? trueBrush : falseBrush;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
