using System.Text.Json;
using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Steam;

public sealed record SteamAchievement(
    string ApiName,
    string Name,
    string Description,
    bool Achieved,
    uint UnlockTime,
    bool Hidden);

public sealed record SteamAchievementsAvailability(bool Available, string Reason);

public sealed class SteamAchievementsException(string message, Exception? innerException = null)
    : IOException(message, innerException);

/// <summary>Lists and explicitly changes achievements through a timed Steam child process.</summary>
public sealed class SteamAchievementsClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<string?> _findLibrary;
    private readonly ISteamWorkerProcessRunner _worker;
    private readonly TimeSpan _timeout;

    public SteamAchievementsClient()
        : this(SteamLibraryLocator.FindLibraryPath, new SteamWorkerProcessRunner(), DefaultTimeout)
    {
    }

    internal SteamAchievementsClient(
        Func<string?> findLibrary,
        ISteamWorkerProcessRunner worker,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(findLibrary);
        ArgumentNullException.ThrowIfNull(worker);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _findLibrary = findLibrary;
        _worker = worker;
        _timeout = timeout;
    }

    public SteamAchievementsAvailability CheckAvailability(int appId)
    {
        if (!SteamAchievementsWorkerHost.IsSupportedAppId(appId))
        {
            return new SteamAchievementsAvailability(false, "Achievements are supported only for official S.T.A.L.K.E.R. releases.");
        }

        try
        {
            return _findLibrary() is not null
                ? new SteamAchievementsAvailability(true, "Steam libsteam_api is available; the Steam client session is checked when requested.")
                : new SteamAchievementsAvailability(false, "Steam libsteam_api was not found.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new SteamAchievementsAvailability(false, $"Could not locate Steam libsteam_api: {exception.Message}");
        }
    }

    public async Task<IReadOnlyList<SteamAchievement>> ListAsync(
        int appId,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(appId);
        var response = await RunAsync(appId, "achievements", null, null, cancellationToken).ConfigureAwait(false);
        EnsureResponseType(response, "Achievements");
        var items = response.GetProperty("items").Deserialize(SteamAchievementJsonContext.Default.SteamAchievementArray);
        return items ?? throw new SteamAchievementsException("Steam worker returned an invalid achievement list.");
    }

    /// <summary>Changes one achievement only when explicitly called by the user.</summary>
    public async Task<SteamAchievement> SetAsync(
        int appId,
        string apiName,
        bool achieved,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        if (apiName.Any(char.IsControl))
        {
            throw new ArgumentException("Achievement API names must not contain control characters.", nameof(apiName));
        }

        EnsureAvailable(appId);
        var response = await RunAsync(appId, "achievement", apiName, achieved, cancellationToken).ConfigureAwait(false);
        EnsureResponseType(response, "Achievement");
        return response.GetProperty("item").Deserialize(SteamAchievementJsonContext.Default.SteamAchievement)
            ?? throw new SteamAchievementsException("Steam worker returned an invalid achievement result.");
    }

    private void EnsureAvailable(int appId)
    {
        var availability = CheckAvailability(appId);
        if (!availability.Available)
        {
            throw new SteamAchievementsException(availability.Reason);
        }
    }

    private async Task<JsonElement> RunAsync(
        int appId,
        string operation,
        string? apiName,
        bool? achieved,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _worker.RunNativeOperationAsync(
                appId,
                operation,
                apiName,
                achieved,
                _timeout,
                cancellationToken).ConfigureAwait(false);
            if (response.TryGetProperty("type", out var type) && type.GetString() is "Error" or "Unavailable")
            {
                var message = response.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new SteamAchievementsException(message ?? "Steam could not return achievement data.");
            }

            return response;
        }
        catch (SteamAchievementsException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            throw new SteamAchievementsException($"Steam achievement operation failed: {exception.Message}", exception);
        }
    }

    private static void EnsureResponseType(JsonElement response, string expected)
    {
        if (!response.TryGetProperty("type", out var type) || type.GetString() != expected)
        {
            throw new SteamAchievementsException("Steam worker returned an unexpected achievement response.");
        }
    }

}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(SteamAchievement[]))]
[JsonSerializable(typeof(SteamAchievement))]
internal partial class SteamAchievementJsonContext : JsonSerializerContext
{
}
