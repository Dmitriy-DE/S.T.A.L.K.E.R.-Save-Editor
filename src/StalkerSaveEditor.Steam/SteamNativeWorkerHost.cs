using System.Text.Json;

namespace StalkerSaveEditor.Steam;

internal interface ISteamRemoteStorage : IDisposable
{
    void Initialize(int appId);

    void RunCallbacks();

    IReadOnlyList<SteamCloudFile> ListFiles();

    byte[] ReadFile(string fileName);

    void WriteFile(string fileName, byte[] data);
}

/// <summary>One-shot worker protocol for isolated Steam RemoteStorage operations.</summary>
public static class SteamNativeWorkerHost
{
    private const int MaximumRequestBytes = 1024 * 1024;
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(3);
    private static readonly TimeSpan DefaultCallbackInterval = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync()
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await RunAsync(input, output, _ => new SteamNativeRemoteStorage(), CancellationToken.None)
            .ConfigureAwait(false);
    }

    public static async Task<int> RunGameSessionAsync(int appId)
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await RunGameSessionAsync(
            appId,
            input,
            output,
            _ => new SteamNativeRemoteStorage(),
            DefaultSessionLifetime,
            DefaultCallbackInterval,
            TimeSpan.FromMilliseconds(250),
            CancellationToken.None).ConfigureAwait(false);
    }

    internal static async Task<int> RunGameSessionAsync(
        int appId,
        Stream input,
        Stream output,
        Func<int, ISteamRemoteStorage> storageFactory,
        TimeSpan sessionLifetime,
        TimeSpan callbackInterval,
        TimeSpan finalCallbackInterval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(storageFactory);
        if (appId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be positive.");
        }

        if (sessionLifetime <= TimeSpan.Zero || callbackInterval <= TimeSpan.Zero || finalCallbackInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime), "Session timing values must be positive.");
        }

        try
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var storage = storageFactory(appId);
            storage.Initialize(appId);
            await WriteJsonLineAsync(output, new WorkerSessionResponse("ready"), cancellationToken).ConfigureAwait(false);

            var inputClosed = DrainInputAsync(input, cancellationToken);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!inputClosed.IsCompleted && elapsed.Elapsed < sessionLifetime)
            {
                storage.RunCallbacks();
                var remaining = sessionLifetime - elapsed.Elapsed;
                await Task.Delay(remaining < callbackInterval ? remaining : callbackInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!inputClosed.IsCompleted)
            {
                await WriteJsonLineAsync(output, new WorkerErrorResponse("error", "Steam game session exceeded its lifetime."), cancellationToken)
                    .ConfigureAwait(false);
                return 1;
            }

            await inputClosed.ConfigureAwait(false);
            for (var callback = 0; callback < 4; callback++)
            {
                storage.RunCallbacks();
                if (finalCallbackInterval > TimeSpan.Zero)
                {
                    await Task.Delay(finalCallbackInterval, cancellationToken).ConfigureAwait(false);
                }
            }

            await WriteJsonLineAsync(output, new WorkerSessionResponse("ok"), cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteJsonLineAsync(output, new WorkerErrorResponse("error", exception.Message), cancellationToken)
                .ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task<int> RunAsync(
        Stream input,
        Stream output,
        Func<int, ISteamRemoteStorage> storageFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(storageFactory);

        try
        {
            var requestBytes = await ReadRequestAsync(input, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(requestBytes);
            var root = document.RootElement;
            var operation = root.GetProperty("operation").GetString();
            var appId = root.GetProperty("appId").GetInt32();
            if (appId <= 0)
            {
                throw new ArgumentOutOfRangeException("appId", "Steam app id must be positive.");
            }

            if (operation is not ("list" or "read" or "write"))
            {
                throw new InvalidOperationException("Only Steam RemoteStorage list, read, and save-write are supported.");
            }

            byte[]? writeData = null;
            string? writeName = null;
            if (operation == "write")
            {
                if (!SteamCloudSaveProfiles.TryGet(appId, out var profile))
                {
                    throw new InvalidOperationException("RemoteStorage writes are limited to official X-Ray trilogy releases.");
                }

                var fileName = root.GetProperty("fileName").GetString();
                if (!profile.TryNormalizeSavePath(fileName ?? string.Empty, out writeName))
                {
                    throw new InvalidOperationException("RemoteStorage write path is outside the selected release's save allow-list.");
                }

                var size = root.GetProperty("size").GetInt32();
                if (size is <= 0 or > SteamNativeRemoteStorage.MaximumFileBytes)
                {
                    throw new InvalidDataException("RemoteStorage write size is outside the supported range.");
                }

                writeData = new byte[size];
                await input.ReadExactlyAsync(writeData, cancellationToken).ConfigureAwait(false);
                if (!profile.HasExpectedFormat(writeData))
                {
                    throw new InvalidDataException("RemoteStorage write payload is not a save for the selected release.");
                }
            }

            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var storage = storageFactory(appId);
            storage.Initialize(appId);

            if (operation == "list")
            {
                var files = storage.ListFiles();
                await WriteJsonLineAsync(output, new WorkerFilesResponse("files", files), cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (operation == "read")
            {
                var fileName = root.GetProperty("fileName").GetString();
                ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
                var data = storage.ReadFile(fileName);
                await WriteJsonLineAsync(output, new WorkerDataResponse("data", data.Length), cancellationToken)
                    .ConfigureAwait(false);
                await output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (operation == "write")
            {
                storage.WriteFile(writeName!, writeData!);
                await WriteJsonLineAsync(output, new WorkerStatusResponse("ok"), cancellationToken)
                    .ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteJsonLineAsync(output, new WorkerErrorResponse("error", exception.Message), cancellationToken)
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<byte[]> ReadRequestAsync(Stream input, CancellationToken cancellationToken)
    {
        using var request = new MemoryStream();
        var oneByte = new byte[1];
        while (request.Length < MaximumRequestBytes)
        {
            var count = await input.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("Steam worker received no request.");
            }

            if (oneByte[0] == (byte)'\n')
            {
                return request.ToArray();
            }

            request.WriteByte(oneByte[0]);
        }

        throw new InvalidDataException("Steam worker request exceeded the size limit.");
    }

    private static async Task WriteJsonLineAsync<T>(
        Stream output,
        T response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        await output.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DrainInputAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0)
        {
        }
    }

    private sealed record WorkerFilesResponse(string Type, IReadOnlyList<SteamCloudFile> Files);

    private sealed record WorkerDataResponse(string Type, int Size);

    private sealed record WorkerErrorResponse(string Type, string Message);

    private sealed record WorkerSessionResponse(string Type);

    private sealed record WorkerStatusResponse(string Type);
}
