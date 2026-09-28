using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Desktop.Services;

public interface ISteamAchievementsAdapter
{
    bool IsAvailable(int appId);
    string? GetAvailabilityMessage(int appId);
    Task<IReadOnlyList<SteamAchievement>> ListAsync(int appId, CancellationToken cancellationToken = default);
    Task<SteamAchievement> SetAsync(int appId, string apiName, bool achieved, CancellationToken cancellationToken = default);
}

public sealed class SteamAchievementsAdapter : ISteamAchievementsAdapter
{
    private readonly SteamAchievementsClient _client;

    public SteamAchievementsAdapter() : this(new SteamAchievementsClient()) { }

    internal SteamAchievementsAdapter(SteamAchievementsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public bool IsAvailable(int appId)
    {
        try
        {
            return _client.CheckAvailability(appId).Available;
        }
        catch
        {
            return false;
        }
    }

    public string? GetAvailabilityMessage(int appId)
    {
        try
        {
            return _client.CheckAvailability(appId).Reason;
        }
        catch (Exception ex)
        {
            return L.T("Steam недоступен: {0}", ex.Message);
        }
    }

    public Task<IReadOnlyList<SteamAchievement>> ListAsync(int appId, CancellationToken cancellationToken = default) =>
        _client.ListAsync(appId, cancellationToken);

    public Task<SteamAchievement> SetAsync(int appId, string apiName, bool achieved, CancellationToken cancellationToken = default) =>
        _client.SetAsync(appId, apiName, achieved, cancellationToken);
}
