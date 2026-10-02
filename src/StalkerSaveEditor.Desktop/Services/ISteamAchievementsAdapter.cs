namespace StalkerSaveEditor.Desktop.Services;

public interface ISteamAchievementsAdapter
{
    bool IsAvailable(int appId);
    string? GetAvailabilityMessage(int appId);
    Task<IReadOnlyList<SteamAchievement>> ListAsync(int appId, CancellationToken cancellationToken = default);
    Task<SteamAchievement> SetAsync(int appId, string apiName, bool achieved, CancellationToken cancellationToken = default);
}

/// <summary>One achievement as the screen shows it (the Steam project has its own type; the UI does not depend on it).</summary>
public sealed record SteamAchievement(
    string ApiName,
    string Name,
    string Description,
    bool Achieved,
    uint UnlockTime,
    bool Hidden);

/// <summary>What a host without Steam (the web edition) offers: nothing, with a reason.</summary>
public sealed class UnavailableSteamAchievementsAdapter : ISteamAchievementsAdapter
{
    public bool IsAvailable(int appId) => false;

    public string? GetAvailabilityMessage(int appId) => L.T("Steam недоступен: {0}", "host");

    public Task<IReadOnlyList<SteamAchievement>> ListAsync(int appId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SteamAchievement>>([]);

    public Task<SteamAchievement> SetAsync(int appId, string apiName, bool achieved, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Steam is not available in this edition.");
}
