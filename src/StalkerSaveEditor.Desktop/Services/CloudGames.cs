namespace StalkerSaveEditor.Desktop.Services;

/// <summary>Which save release a Steam application's cloud files belong to.</summary>
public static class CloudGames
{
    public static string ReleaseId(int appId) => appId switch
    {
        4500 => "stalker-soc",
        20510 => "stalker-cs",
        41700 => "stalker-cop",
        1643320 => "stalker2",
        _ => "unknown"
    };
}
