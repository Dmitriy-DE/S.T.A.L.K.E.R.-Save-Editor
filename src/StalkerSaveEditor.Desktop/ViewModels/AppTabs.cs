namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>
/// The screens of the application by id. The ids are what the navigation, the view model and the tests exchange;
/// they are written once here, so a mistyped id is a compile error instead of an empty screen.
/// </summary>
public static class AppTabs
{
    public const string Overview = "overview";
    public const string Inventory = "inventory";
    public const string Factions = "factions";
    public const string Stashes = "stashes";
    public const string Transitions = "transitions";
    public const string Backups = "backups";
    public const string Compare = "compare";
    public const string Settings = "settings";
    public const string Capabilities = "capabilities";
    public const string Companion = "companion";
    public const string Cloud = "cloud";
    public const string Achievements = "achievements";
    public const string GameDoctor = "game-doctor";
    public const string SaveDoctor = "save-doctor";
    public const string GameFixes = "game-fixes";
    public const string Updates = "updates";
    public const string Timeline = "timeline";
    public const string Encyclopedia = "encyclopedia";
    public const string ToolkitEnvironment = "toolkit-environment";
    public const string Games = "games";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Overview, Inventory, Factions, Stashes, Transitions, Backups, Compare, Settings, Capabilities, Companion, Cloud, Achievements, GameDoctor, SaveDoctor, GameFixes, Updates, Timeline, Encyclopedia, ToolkitEnvironment, Games,
    };

    /// <summary>The id itself when it names a screen, otherwise the overview (a stale id from settings or a caller's typo).</summary>
    public static string Normalize(string? id) => id is not null && All.Contains(id) ? id : Overview;
}
