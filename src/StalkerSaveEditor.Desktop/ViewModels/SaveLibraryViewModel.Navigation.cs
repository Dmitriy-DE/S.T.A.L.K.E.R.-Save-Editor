using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed partial class SaveLibraryViewModel : ObservableViewModel, IDisposable
{
    public bool IsOverviewTab => SelectedTab == AppTabs.Overview;
    public bool IsInventoryTab => SelectedTab == AppTabs.Inventory;
    public bool IsFactionsTab => SelectedTab == AppTabs.Factions;
    public bool IsStashesTab => SelectedTab == AppTabs.Stashes;
    public bool IsTransitionsTab => SelectedTab == AppTabs.Transitions;
    public bool IsBackupsTab => SelectedTab == AppTabs.Backups;
    public bool IsCompareTab => SelectedTab == AppTabs.Compare;
    public bool IsSettingsTab => SelectedTab == AppTabs.Settings;
    public bool IsCapabilitiesTab => SelectedTab == AppTabs.Capabilities;
    public bool IsCompanionTab => SelectedTab == AppTabs.Companion;
    public bool IsCloudTab => SelectedTab == AppTabs.Cloud;
    public bool IsAchievementsTab => SelectedTab == AppTabs.Achievements;
    public bool IsGameDoctorTab => SelectedTab == AppTabs.GameDoctor;
    public bool IsSaveDoctorTab => SelectedTab == AppTabs.SaveDoctor;
    public bool IsGameFixesTab => SelectedTab == AppTabs.GameFixes;
    public bool IsUpdatesTab => SelectedTab == AppTabs.Updates;
    public bool IsTimelineTab => SelectedTab == AppTabs.Timeline;
    public bool IsEncyclopediaTab => SelectedTab == AppTabs.Encyclopedia;
    public bool IsToolkitEnvironmentTab => SelectedTab == AppTabs.ToolkitEnvironment;
    public bool IsGamesOverviewTab => SelectedTab == AppTabs.Games;

    public bool IsSaveWorkspace => SelectedTab is AppTabs.Overview or AppTabs.Inventory or AppTabs.Factions or AppTabs.Stashes or AppTabs.Transitions or AppTabs.Backups or AppTabs.Compare or AppTabs.Timeline or AppTabs.SaveDoctor;

    public string CurrentGroupTitle => SelectedTab switch
    {
        AppTabs.Games or AppTabs.GameFixes or AppTabs.GameDoctor or AppTabs.ToolkitEnvironment or AppTabs.Companion or AppTabs.Achievements => L.T("ИГРЫ"),
        AppTabs.Encyclopedia or AppTabs.Capabilities or AppTabs.Updates or AppTabs.Settings or AppTabs.Cloud => L.T("ИНСТРУМЕНТЫ"),
        _ => L.T("СОХРАНЕНИЯ"),
    };

    public string CurrentPageTitle => SelectedTab switch
    {
        AppTabs.Inventory => L.T("ИНВЕНТАРЬ"),
        AppTabs.Factions => L.T("ФРАКЦИИ"),
        AppTabs.Stashes => L.T("ТАЙНИКИ"),
        AppTabs.Transitions => L.T("ПЕРЕХОДЫ"),
        AppTabs.Backups => L.T("БЭКАПЫ"),
        AppTabs.Compare => L.T("СРАВНЕНИЕ"),
        AppTabs.Timeline => L.T("ИСТОРИЯ СОХРАНЕНИЙ"),
        AppTabs.SaveDoctor => L.T("ДОКТОР СОХРАНЕНИЯ"),
        AppTabs.GameFixes => L.T("ИСПРАВЛЕНИЯ ИГРЫ"),
        AppTabs.Games => L.T("ИГРЫ И ИНСТРУМЕНТЫ"),
        AppTabs.GameDoctor => L.T("ДОКТОР ИГРЫ"),
        AppTabs.ToolkitEnvironment => L.T("СРЕДА ИГРЫ"),
        AppTabs.Companion => L.T("КОМПАНЬОН"),
        AppTabs.Achievements => L.T("ДОСТИЖЕНИЯ"),
        AppTabs.Cloud => L.T("ОБЛАКО"),
        AppTabs.Encyclopedia => L.T("ЭНЦИКЛОПЕДИЯ"),
        AppTabs.Capabilities => L.T("ВОЗМОЖНОСТИ"),
        AppTabs.Updates => L.T("ОБНОВЛЕНИЯ"),
        AppTabs.Settings => L.T("НАСТРОЙКИ"),
        _ => L.T("ОБЗОР"),
    };

    public bool ShowOverviewScreen => HasSelection && IsOverviewTab;
    public bool ShowInventoryScreen => HasSelection && IsInventoryTab;
    public bool ShowFactionsScreen => HasSelection && IsFactionsTab;
    public bool ShowStashesScreen => HasSelection && IsStashesTab;
    public bool ShowTransitionsScreen => HasSelection && IsTransitionsTab;
    public bool ShowBackupsScreen => HasSelection && IsBackupsTab;
    public bool ShowCompareScreen => IsCompareTab;
    public bool ShowSettingsScreen => IsSettingsTab;
    public bool ShowCapabilitiesScreen => IsCapabilitiesTab;
    public bool ShowCompanionScreen => IsCompanionTab;
    public bool ShowCloudScreen => IsCloudTab;
    public bool ShouldShowEmptyState => HasNoSelection && !IsSettingsTab && !IsCapabilitiesTab && !IsCompanionTab && !IsCloudTab && !IsAchievementsTab && !IsGameDoctorTab && !IsSaveDoctorTab && !IsGameFixesTab && !IsUpdatesTab && !IsTimelineTab && !IsEncyclopediaTab && !IsToolkitEnvironmentTab && !IsGamesOverviewTab && !IsCompareTab && !IsFirstRunWizardVisible;
    public bool ShowAchievementsScreen => IsAchievementsTab;
    public bool ShowGameDoctorScreen => IsGameDoctorTab;
    public bool ShowSaveDoctorScreen => IsSaveDoctorTab;
    public bool ShowGameFixesScreen => IsGameFixesTab;
    public bool ShowUpdatesScreen => IsUpdatesTab;
    public bool ShowTimelineScreen => IsTimelineTab;
    public bool ShowEncyclopediaScreen => IsEncyclopediaTab;
    public bool ShowToolkitEnvironmentScreen => IsToolkitEnvironmentTab;
    public bool ShowGamesOverviewScreen => IsGamesOverviewTab;
}
