namespace StalkerSaveEditor.Core.Hotkeys;

/// <summary>
/// Tells whether a foreground window belongs to an X-Ray game (by process or window-class name). Global
/// hotkeys are held only while it does, so Ctrl+S, Ctrl+R… keep working in every other program.
/// </summary>
public static class GameWindowMatcher
{
    private static readonly string[] GameNames = ["xr_3da", "xrengine", "steam_app_4500", "steam_app_20510", "steam_app_41700", "steam_app_2427410", "steam_app_2427420", "steam_app_2427430"];

    public static bool IsGame(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var value = name.Trim().ToLowerInvariant();
        if (value.EndsWith(".exe", StringComparison.Ordinal)) value = value[..^4];
        return GameNames.Any(game => value == game || value.EndsWith("\\" + game, StringComparison.Ordinal) || value.EndsWith("/" + game, StringComparison.Ordinal));
    }
}
