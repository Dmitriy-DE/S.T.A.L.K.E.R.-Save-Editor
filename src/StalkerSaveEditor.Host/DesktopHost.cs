using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Host;

/// <summary>Gives the UI its desktop-only services. Called once at start-up by the desktop application.</summary>
public static class DesktopHost
{
    public static void Register()
    {
        HostPlatform.CreateCloudService = static () => new CloudServiceAdapter();
        HostPlatform.CreateAchievementsService = static () => new SteamAchievementsAdapter();
        HostPlatform.CreateUpdateService = static () => new UpdateServiceAdapter();
    }
}
