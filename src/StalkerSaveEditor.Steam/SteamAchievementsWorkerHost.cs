using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace StalkerSaveEditor.Steam;

internal interface ISteamUserStats : IDisposable
{
    void Initialize(int appId);

    IReadOnlyList<SteamAchievement> List();

    SteamAchievement Set(string apiName, bool achieved);
}

/// <summary>One-shot child operations for listing or explicitly changing S.T.A.L.K.E.R. achievements.</summary>
public static partial class SteamAchievementsWorkerHost
{
    private static readonly HashSet<int> SupportedAppIds =
    [1643320, 4500, 20510, 41700, 2427410, 2427420, 2427430];

    public static Task<int> RunAchievementsAsync(int appId) => RunConsoleAsync(
        appId,
        "achievements",
        null,
        null,
        _ => new SteamNativeUserStats());

    public static Task<int> RunAchievementAsync(int appId, string apiName, bool achieved) => RunConsoleAsync(
        appId,
        "achievement",
        apiName,
        achieved,
        _ => new SteamNativeUserStats());

    internal static bool IsSupportedAppId(int appId) => SupportedAppIds.Contains(appId);

    internal static async Task<int> RunAsync(
        int appId,
        string operation,
        string? apiName,
        bool? achieved,
        Stream output,
        Func<int, ISteamUserStats> statsFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(statsFactory);

        try
        {
            if (!IsSupportedAppId(appId))
            {
                throw new ArgumentOutOfRangeException(nameof(appId), "Achievements are limited to official S.T.A.L.K.E.R. releases.");
            }

            if (operation == "achievements")
            {
                if (apiName is not null || achieved is not null)
                {
                    throw new ArgumentException("List operation does not accept achievement arguments.");
                }
            }
            else if (operation == "achievement")
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
                if (apiName.Any(char.IsControl) || achieved is null)
                {
                    throw new ArgumentException("Set operation requires a valid API name and achieved state.");
                }
            }
            else
            {
                throw new InvalidOperationException("Only achievements list and set operations are supported.");
            }

            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var stats = statsFactory(appId);
            stats.Initialize(appId);

            if (operation == "achievements")
            {
                await WriteJsonLineAsync(
                    output,
                    new WorkerAchievementsResponse("Achievements", stats.List()),
                    SteamAchievementsWorkerJsonContext.Default.WorkerAchievementsResponse,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var item = stats.Set(apiName!, achieved!.Value);
                await WriteJsonLineAsync(
                    output,
                    new WorkerAchievementResponse("Achievement", item),
                    SteamAchievementsWorkerJsonContext.Default.WorkerAchievementResponse,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteJsonLineAsync(
                output,
                new WorkerErrorResponse("Error", exception.Message),
                SteamAchievementsWorkerJsonContext.Default.WorkerErrorResponse,
                cancellationToken)
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunConsoleAsync(
        int appId,
        string operation,
        string? apiName,
        bool? achieved,
        Func<int, ISteamUserStats> statsFactory)
    {
        await using var output = Console.OpenStandardOutput();
        return await RunAsync(appId, operation, apiName, achieved, output, statsFactory, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task WriteJsonLineAsync<T>(
        Stream output,
        T response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, jsonTypeInfo);
        await output.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record WorkerAchievementsResponse(string Type, IReadOnlyList<SteamAchievement> Items);

    private sealed record WorkerAchievementResponse(string Type, SteamAchievement Item);

    private sealed record WorkerErrorResponse(string Type, string Message);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(WorkerAchievementsResponse))]
    [JsonSerializable(typeof(WorkerAchievementResponse))]
    [JsonSerializable(typeof(WorkerErrorResponse))]
    private partial class SteamAchievementsWorkerJsonContext : JsonSerializerContext
    {
    }
}
