using System.Text.Json;

namespace StalkerSaveEditor.Steam;

internal interface ISteamRemoteStorage : IDisposable
{
    void Initialize(int appId);

    IReadOnlyList<SteamCloudFile> ListFiles();

    byte[] ReadFile(string fileName);
}

/// <summary>One-shot worker protocol. Only list and read operations are accepted.</summary>
public static class SteamNativeWorkerHost
{
    private const int MaximumRequestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync()
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await RunAsync(input, output, _ => new SteamNativeRemoteStorage(), CancellationToken.None)
            .ConfigureAwait(false);
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

            if (operation is not ("list" or "read"))
            {
                throw new InvalidOperationException("Only Steam RemoteStorage list and read are supported.");
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
            else
            {
                var fileName = root.GetProperty("fileName").GetString();
                ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
                var data = storage.ReadFile(fileName);
                await WriteJsonLineAsync(output, new WorkerDataResponse("data", data.Length), cancellationToken)
                    .ConfigureAwait(false);
                await output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private sealed record WorkerFilesResponse(string Type, IReadOnlyList<SteamCloudFile> Files);

    private sealed record WorkerDataResponse(string Type, int Size);

    private sealed record WorkerErrorResponse(string Type, string Message);
}
