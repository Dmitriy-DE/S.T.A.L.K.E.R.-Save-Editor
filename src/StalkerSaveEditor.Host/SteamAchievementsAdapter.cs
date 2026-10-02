using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Steam;
using SteamAchievement = StalkerSaveEditor.Desktop.Services.SteamAchievement;

namespace StalkerSaveEditor.Host;

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

    public async Task<IReadOnlyList<SteamAchievement>> ListAsync(int appId, CancellationToken cancellationToken = default) =>
        (await _client.ListAsync(appId, cancellationToken).ConfigureAwait(false)).Select(ToModel).ToArray();

    public async Task<SteamAchievement> SetAsync(int appId, string apiName, bool achieved, CancellationToken cancellationToken = default) =>
        ToModel(await _client.SetAsync(appId, apiName, achieved, cancellationToken).ConfigureAwait(false));

    private static SteamAchievement ToModel(Steam.SteamAchievement value) =>
        new(value.ApiName, value.Name, value.Description, value.Achieved, value.UnlockTime, value.Hidden);
}
