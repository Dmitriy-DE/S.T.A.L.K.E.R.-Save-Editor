using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;

namespace StalkerSaveEditor.Desktop;

public sealed partial class MainWindow : Window
{
    private static Control BuildWorkspacePane(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
        };

        var breadcrumb = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 9),
        };
        var breadcrumbParts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var group = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextMuted,
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        group.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.CurrentGroupTitle)) { Source = vm });
        breadcrumbParts.Children.Add(group);
        breadcrumbParts.Children.Add(new TextBlock
        {
            Text = "/",
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var page = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            FontFamily = StalkerTheme.HeadingFont,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        page.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.CurrentPageTitle)) { Source = vm });
        breadcrumbParts.Children.Add(page);
        breadcrumb.Child = breadcrumbParts;
        root.Children.Add(breadcrumb);

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
                    Text = L.T("ВЫБЕРИТЕ СОХРАНЕНИЕ"),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = L.T("Выберите сейв из списка слева для просмотра и редактирования."),
                    Foreground = StalkerTheme.BrushTextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        emptyState.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShouldShowEmptyState)));
        contentGrid.Children.Add(emptyState);

        // First Run Wizard
        var wizard = BuildFirstRunWizard(vm);
        wizard.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.IsFirstRunWizardVisible)));
        contentGrid.Children.Add(wizard);

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

        var compare = CompareView.Build(vm.Compare);
        compare.Bind(Visual.IsVisibleProperty,
            new Binding(nameof(SaveLibraryViewModel.ShowCompareScreen)) { Source = vm });
        screens.Children.Add(compare);

        var timeline = TimelineView.Build(vm.Timeline);
        timeline.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowTimelineScreen)) { Source = vm });
        screens.Children.Add(timeline);
        if (!HostPlatform.IsBrowser)
        {
            var encyclopedia = EncyclopediaView.Build(vm.Encyclopedia);
            encyclopedia.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowEncyclopediaScreen)) { Source = vm });
            screens.Children.Add(encyclopedia);
        }

        // 7. Settings
        var screenshotSettingsCategory = SaveLibraryViewModel.InteractiveApp
            ? null
            : Environment.GetEnvironmentVariable("STALKER_EDITOR_SCREENSHOT_SETTINGS_CATEGORY");
        var settings = SettingsView.Build(
            vm.Settings,
            vm.Diagnostics,
            tab => vm.SelectedTab = tab,
            screenshotSettingsCategory);
        settings.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowSettingsScreen)));
        screens.Children.Add(settings);

        // 8. Capabilities
        var capabilities = CapabilitiesView.Build(vm.Capabilities);
        capabilities.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCapabilitiesScreen)));
        screens.Children.Add(capabilities);

        // 9. Companion
        var companion = new CompanionView { DataContext = vm.Companion };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        companion.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCompanionScreen)) { Source = vm });
        screens.Children.Add(companion);

        // 10. Cloud
        var cloud = new CloudView { DataContext = vm.Cloud };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        cloud.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCloudScreen)) { Source = vm });
        screens.Children.Add(cloud);
        // 10. Achievements
        var achievements = new AchievementsView { DataContext = vm.Achievements };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        achievements.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowAchievementsScreen)) { Source = vm });
        screens.Children.Add(achievements);
        // Game Doctor operates on an explicitly selected local installation.
        if (!HostPlatform.IsBrowser)
        {
            var games = GamesOverviewView.Build(vm);
            games.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowGamesOverviewScreen)) { Source = vm });
            screens.Children.Add(games);

            var gameFixes = new GameFixesView(vm.GameFixes);
            gameFixes.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowGameFixesScreen)) { Source = vm });
            screens.Children.Add(gameFixes);

            var saveDoctor = new SaveDoctorView(vm.SaveDoctor);
            saveDoctor.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowSaveDoctorScreen)) { Source = vm });
            screens.Children.Add(saveDoctor);

            var gameDoctor = new GameDoctorView(vm.GameDoctor);
            gameDoctor.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowGameDoctorScreen)) { Source = vm });
            screens.Children.Add(gameDoctor);

            var toolkitEnvironment = new ToolkitEnvironmentView(vm.ToolkitEnvironment);
            toolkitEnvironment.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowToolkitEnvironmentScreen)) { Source = vm });
            screens.Children.Add(toolkitEnvironment);
        }
        // 10. Updates
        var updates = new UpdatesView { DataContext = vm.Updates };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        updates.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowUpdatesScreen)) { Source = vm });
        screens.Children.Add(updates);

        contentGrid.Children.Add(screens);

        Grid.SetRow(contentGrid, 2);
        root.Children.Add(contentGrid);

        // Edge state "changed outside the editor": the game or another tool rewrote the open save.
        var changedText = new TextBlock
        {
            Text = L.T("Файл сейва изменился после открытия (игра или другая программа). Несохранённые правки относятся к старой версии."),
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var reload = StalkerTheme.StalkerButton(L.T("Открыть заново"), isPrimary: true, minWidth: 150);
        reload.Click += (_, _) => vm.ReloadChangedSave();
        DockPanel.SetDock(reload, Dock.Right);
        var changedBanner = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushWarning,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(16, 8),
            Child = new DockPanel { LastChildFill = true, Children = { reload, changedText } },
        };
        changedBanner.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.SelectedSaveChangedOnDisk)) { Source = vm });
        Grid.SetRow(changedBanner, 1);
        root.Children.Add(changedBanner);

        return root;
    }
}
