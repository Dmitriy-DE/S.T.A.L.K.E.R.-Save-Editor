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

        // A screen is built when it is first shown: twenty screens built at start, each following the selected save
        // while hidden, made opening a large save freeze the window.
        void Screen(string shows, Func<Control> build)
        {
            Control? screen = null;
            var property = typeof(SaveLibraryViewModel).GetProperty(shows)!;
            void Ensure()
            {
                if (screen is not null || property.GetValue(vm) is not true) return;
                screen = build();
                screen.Bind(Visual.IsVisibleProperty, new Binding(shows) { Source = vm });
                screens.Children.Add(screen);
            }

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == shows) Ensure();
            };
            Ensure();
        }

        Screen(nameof(SaveLibraryViewModel.ShowOverviewScreen), OverviewView.Build);
        Screen(nameof(SaveLibraryViewModel.ShowInventoryScreen), () => InventoryView.Build(vm));
        Screen(nameof(SaveLibraryViewModel.ShowFactionsScreen), () => FactionsView.Build(vm));
        Screen(nameof(SaveLibraryViewModel.ShowStashesScreen), () => StashesView.Build(vm));
        Screen(nameof(SaveLibraryViewModel.ShowTransitionsScreen), TransitionsView.Build);
        Screen(nameof(SaveLibraryViewModel.ShowBackupsScreen), () => BackupsView.Build(vm));
        Screen(nameof(SaveLibraryViewModel.ShowCompareScreen), () => CompareView.Build(vm.Compare));
        Screen(nameof(SaveLibraryViewModel.ShowTimelineScreen), () => TimelineView.Build(vm.Timeline));
        var screenshotSettingsCategory = SaveLibraryViewModel.InteractiveApp
            ? null
            : Environment.GetEnvironmentVariable("STALKER_EDITOR_SCREENSHOT_SETTINGS_CATEGORY");
        Screen(nameof(SaveLibraryViewModel.ShowSettingsScreen), () => SettingsView.Build(
            vm.Settings,
            vm.Diagnostics,
            tab => vm.SelectedTab = tab,
            screenshotSettingsCategory));
        Screen(nameof(SaveLibraryViewModel.ShowCapabilitiesScreen), () => CapabilitiesView.Build(vm.Capabilities));
        // These screens have a view model of their own as DataContext; visibility belongs to the window's.
        Screen(nameof(SaveLibraryViewModel.ShowCompanionScreen), () => new CompanionView { DataContext = vm.Companion });
        Screen(nameof(SaveLibraryViewModel.ShowCloudScreen), () => new CloudView { DataContext = vm.Cloud });
        Screen(nameof(SaveLibraryViewModel.ShowAchievementsScreen), () => new AchievementsView { DataContext = vm.Achievements });
        Screen(nameof(SaveLibraryViewModel.ShowUpdatesScreen), () => new UpdatesView { DataContext = vm.Updates });
        if (!HostPlatform.IsBrowser)
        {
            // These work on local installations and files; the web edition has none.
            Screen(nameof(SaveLibraryViewModel.ShowEncyclopediaScreen), () => EncyclopediaView.Build(vm.Encyclopedia));
            Screen(nameof(SaveLibraryViewModel.ShowGamesOverviewScreen), () => GamesOverviewView.Build(vm));
            Screen(nameof(SaveLibraryViewModel.ShowGameFixesScreen), () => new GameFixesView(vm.GameFixes));
            Screen(nameof(SaveLibraryViewModel.ShowSaveDoctorScreen), () => new SaveDoctorView(vm.SaveDoctor));
            Screen(nameof(SaveLibraryViewModel.ShowGameDoctorScreen), () => new GameDoctorView(vm.GameDoctor));
            Screen(nameof(SaveLibraryViewModel.ShowToolkitEnvironmentScreen), () => new ToolkitEnvironmentView(vm.ToolkitEnvironment));
        }

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
