using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace StalkerSaveEditor.Steam;

internal interface ISteamWorkerProcessRunner
{
    Task<IReadOnlyList<SteamCloudFile>> ListAsync(
        int appId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(
        int appId,
        string fileName,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface ISteamWorkerProcessFactory
{
    ISteamWorkerChildProcess Start(ProcessStartInfo startInfo);
}

internal interface ISteamWorkerChildProcess : IDisposable
{
    Stream StandardInput { get; }

    Stream StandardOutput { get; }

    Stream StandardError { get; }

    void Start();

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    void KillTree();
}

internal sealed class SteamWorkerProcessRunner : ISteamWorkerProcessRunner
{
    private const int MaximumHeaderBytes = 1024 * 1024;
    private readonly ISteamWorkerProcessFactory _factory;

    public SteamWorkerProcessRunner() : this(new SteamWorkerProcessFactory()) { }

    internal SteamWorkerProcessRunner(ISteamWorkerProcessFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<IReadOnlyList<SteamCloudFile>> ListAsync(
        int appId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var response = await RunAsync(
            new WorkerRequest("list", appId, null), timeout, cancellationToken).ConfigureAwait(false);
        if (response.Header.GetProperty("type").GetString() != "files")
        {
            throw new InvalidDataException("Steam worker returned an unexpected list response.");
        }

        return response.Header.GetProperty("files").Deserialize<SteamCloudFile[]>(JsonOptions)
            ?? throw new InvalidDataException("Steam worker returned an invalid file list.");
    }

    public async Task<byte[]> ReadAsync(
        int appId,
        string fileName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var response = await RunAsync(
            new WorkerRequest("read", appId, fileName), timeout, cancellationToken).ConfigureAwait(false);
        if (response.Header.GetProperty("type").GetString() != "data")
        {
            throw new InvalidDataException("Steam worker returned an unexpected read response.");
        }

        return response.Data;
    }

    private async Task<WorkerResponse> RunAsync(
        WorkerRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(request.AppId);
        using var process = _factory.Start(startInfo);
        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var stderrTask = DrainErrorAsync(process.StandardError);
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            await process.StandardInput.WriteAsync(requestBytes, timeoutSource.Token).ConfigureAwait(false);
            await process.StandardInput.WriteAsync("\n"u8.ToArray(), timeoutSource.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
            process.StandardInput.Dispose();

            var headerBytes = await ReadHeaderAsync(process.StandardOutput, timeoutSource.Token)
                .ConfigureAwait(false);
            using var header = JsonDocument.Parse(headerBytes);
            var kind = header.RootElement.GetProperty("type").GetString();
            if (kind == "error")
            {
                var message = header.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new InvalidOperationException(message ?? "Steam worker failed.");
            }

            byte[] data = [];
            if (kind == "data")
            {
                var size = header.RootElement.GetProperty("size").GetInt32();
                if (size < 0 || size > SteamNativeRemoteStorage.MaximumFileBytes)
                {
                    throw new InvalidDataException("Steam worker returned an invalid file size.");
                }

                data = new byte[size];
                await process.StandardOutput.ReadExactlyAsync(data, timeoutSource.Token).ConfigureAwait(false);
            }

            var exitCode = await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"Steam worker exited with code {exitCode}."
                        : $"Steam worker exited with code {exitCode}: {stderr.Trim()}");
            }

            return new WorkerResponse(header.RootElement.Clone(), data);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.KillTree();
            throw new TimeoutException($"Steam worker exceeded the {timeout.TotalSeconds:0.###} second timeout.");
        }
        catch
        {
            process.KillTree();
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(int appId)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the current executable for the Steam worker.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly))
            {
                throw new InvalidOperationException("Cannot determine the application assembly for the Steam worker.");
            }

            startInfo.ArgumentList.Add(entryAssembly);
        }

        startInfo.ArgumentList.Add("--steam-native-worker");
        startInfo.Environment["SteamAppId"] = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["SteamGameId"] = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return startInfo;
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var oneByte = new byte[1];
        while (buffer.Length < MaximumHeaderBytes)
        {
            var count = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("Steam worker closed stdout before returning a response.");
            }

            if (oneByte[0] == (byte)'\n')
            {
                return buffer.ToArray();
            }

            buffer.WriteByte(oneByte[0]);
        }

        throw new InvalidDataException("Steam worker response header exceeded the size limit.");
    }

    private static async Task<string> DrainErrorAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record WorkerRequest(string Operation, int AppId, string? FileName);

    private sealed record WorkerResponse(JsonElement Header, byte[] Data);
}

internal sealed class SteamWorkerProcessFactory : ISteamWorkerProcessFactory
{
    public ISteamWorkerChildProcess Start(ProcessStartInfo startInfo) =>
        new SteamWorkerChildProcess(startInfo);
}

internal sealed class SteamWorkerChildProcess(ProcessStartInfo startInfo) : ISteamWorkerChildProcess
{
    private readonly Process _process = new() { StartInfo = startInfo };

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public Stream StandardError => _process.StandardError.BaseStream;

    public void Start()
    {
        if (!_process.Start())
        {
            throw new InvalidOperationException("Steam worker process could not be started.");
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
        WaitAsync(cancellationToken);

    private async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public void KillTree()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited while the timeout handler was running.
        }
    }

    public void Dispose() => _process.Dispose();
}
