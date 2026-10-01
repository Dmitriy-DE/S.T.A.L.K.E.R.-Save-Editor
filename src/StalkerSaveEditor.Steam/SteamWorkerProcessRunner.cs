using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    Task WriteAsync(
        int appId,
        string fileName,
        ReadOnlyMemory<byte> data,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<JsonElement> RunNativeOperationAsync(
        int appId,
        string operation,
        string? apiName,
        bool? achieved,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface ISteamGameSessionRunner
{
    Task<ISteamGameSession> StartSessionAsync(
        int appId,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface ISteamGameSession : IAsyncDisposable;

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

internal sealed partial class SteamWorkerProcessRunner : ISteamWorkerProcessRunner, ISteamGameSessionRunner
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

        return response.Header.GetProperty("files").Deserialize(SteamWorkerJsonContext.Default.SteamCloudFiles)
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

    public async Task<ISteamGameSession> StartSessionAsync(
        int appId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (appId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be positive.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Steam session startup timeout must be positive.");
        }

        var startInfo = CreateStartInfo(appId, gameSession: true);
        var process = _factory.Start(startInfo);
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var stderrTask = DrainErrorAsync(process.StandardError);
        try
        {
            var headerBytes = await ReadHeaderAsync(process.StandardOutput, timeoutSource.Token).ConfigureAwait(false);
            using var header = JsonDocument.Parse(headerBytes);
            var type = header.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (!string.Equals(type, "ready", StringComparison.Ordinal))
            {
                var message = header.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new InvalidOperationException(message ?? "Steam game session did not report ready.");
            }

            return new SteamWorkerGameSession(process, stderrTask, TimeSpan.FromSeconds(30));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await KillAndWaitAsync(process).ConfigureAwait(false);
            process.Dispose();
            throw new TimeoutException($"Steam game session startup exceeded the {timeout.TotalSeconds:0.###} second timeout.");
        }
        catch
        {
            await KillAndWaitAsync(process).ConfigureAwait(false);
            process.Dispose();
            throw;
        }
    }

    public async Task WriteAsync(
        int appId,
        string fileName,
        ReadOnlyMemory<byte> data,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (appId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be positive.");
        }

        if (data.IsEmpty || data.Length > SteamNativeRemoteStorage.MaximumFileBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Steam Cloud writes must contain 1..64 MiB.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Worker timeout must be positive.");
        }

        var response = await RunAsync(
            new WorkerRequest("write", appId, fileName, data.Length),
            timeout,
            cancellationToken,
            data).ConfigureAwait(false);
        if (response.Header.GetProperty("type").GetString() != "ok")
        {
            throw new InvalidDataException("Steam worker returned an unexpected write response.");
        }
    }

    public async Task<JsonElement> RunNativeOperationAsync(
        int appId,
        string operation,
        string? apiName,
        bool? achieved,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ValidateNativeOperation(appId, operation, apiName, achieved, timeout);
        var startInfo = CreateStartInfo(
            appId,
            nativeOperation: operation,
            apiName: apiName,
            achieved: achieved);
        using var process = _factory.Start(startInfo);
        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        Task<string>? stderrTask = null;
        try
        {
            stderrTask = DrainErrorAsync(process.StandardError);
            process.StandardInput.Dispose();
            var responseBytes = await ReadHeaderAsync(process.StandardOutput, timeoutSource.Token)
                .ConfigureAwait(false);
            using var response = JsonDocument.Parse(responseBytes);
            var exitCode = await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (exitCode != 0)
            {
                var message = response.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(message)
                        ? string.IsNullOrWhiteSpace(stderr)
                            ? $"Steam worker exited with code {exitCode}."
                            : $"Steam worker exited with code {exitCode}: {stderr.Trim()}"
                        : message);
            }

            return response.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync(process, stderrTask).ConfigureAwait(false);
            throw new TimeoutException($"Steam worker exceeded the {timeout.TotalSeconds:0.###} second timeout.");
        }
        catch
        {
            await StopAsync(process, stderrTask).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<WorkerResponse> RunAsync(
        WorkerRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        ReadOnlyMemory<byte> payload = default)
    {
        var startInfo = CreateStartInfo(request.AppId);
        using var process = _factory.Start(startInfo);
        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        Task<string>? stderrTask = null;
        try
        {
            stderrTask = DrainErrorAsync(process.StandardError);
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, SteamWorkerJsonContext.Default.WorkerRequest);
            await process.StandardInput.WriteAsync(requestBytes, timeoutSource.Token).ConfigureAwait(false);
            await process.StandardInput.WriteAsync("\n"u8.ToArray(), timeoutSource.Token).ConfigureAwait(false);
            if (!payload.IsEmpty)
            {
                await process.StandardInput.WriteAsync(payload, timeoutSource.Token).ConfigureAwait(false);
            }

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
            await StopAsync(process, stderrTask).ConfigureAwait(false);
            throw new TimeoutException($"Steam worker exceeded the {timeout.TotalSeconds:0.###} second timeout.");
        }
        catch
        {
            await StopAsync(process, stderrTask).ConfigureAwait(false);
            throw;
        }
    }

    private static void ValidateNativeOperation(
        int appId,
        string operation,
        string? apiName,
        bool? achieved,
        TimeSpan timeout)
    {
        if (appId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be positive.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Worker timeout must be positive.");
        }

        if (operation == "achievements" && apiName is null && achieved is null)
        {
            return;
        }

        if (operation == "achievement" && !string.IsNullOrWhiteSpace(apiName) && !apiName.Any(char.IsControl) && achieved is not null)
        {
            return;
        }

        throw new ArgumentException("Only a valid achievements list or explicit achievement change operation is supported.");
    }

    private static ProcessStartInfo CreateStartInfo(
        int appId,
        bool gameSession = false,
        string? nativeOperation = null,
        string? apiName = null,
        bool? achieved = null)
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
            var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrWhiteSpace(entryAssemblyName))
            {
                throw new InvalidOperationException("Cannot determine the application assembly for the Steam worker.");
            }

            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.dll"));
        }

        if (gameSession)
        {
            startInfo.ArgumentList.Add("--steam-native-op");
            startInfo.ArgumentList.Add("session");
            startInfo.ArgumentList.Add("--app-id");
            startInfo.ArgumentList.Add(appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (nativeOperation is null)
        {
            startInfo.ArgumentList.Add("--steam-native-worker");
        }
        else
        {
            startInfo.ArgumentList.Add("--steam-native-op");
            startInfo.ArgumentList.Add(nativeOperation);
            startInfo.ArgumentList.Add("--app-id");
            startInfo.ArgumentList.Add(appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (apiName is not null)
            {
                startInfo.ArgumentList.Add("--name");
                startInfo.ArgumentList.Add(apiName);
                startInfo.ArgumentList.Add("--achieved");
                startInfo.ArgumentList.Add(achieved!.Value ? "1" : "0");
            }
        }

        startInfo.Environment["SteamAppId"] = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["SteamGameId"] = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return startInfo;
    }

    internal static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
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

    internal static async Task KillAndWaitAsync(ISteamWorkerChildProcess process)
    {
        try
        {
            process.KillTree();
            _ = await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The child may have exited between the timeout and kill requests.
        }
    }

    /// <summary>One way to stop a worker: the caller returns only after the child is gone and its stderr reader ended.</summary>
    private static async Task StopAsync(ISteamWorkerChildProcess process, Task<string>? stderrTask)
    {
        await KillAndWaitAsync(process).ConfigureAwait(false);
        if (stderrTask is null) return;
        try
        {
            // A grandchild may still hold the pipe; the kill above is what matters, so this wait is short.
            _ = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or ObjectDisposedException)
        {
        }
    }

    internal const int MaximumRetainedErrorChars = 64 * 1024;

    /// <summary>
    /// stderr has to be read to the end or the child blocks on a full pipe, but only its tail is kept: a worker that
    /// floods stderr must not grow the editor's memory.
    /// </summary>
    internal static async Task<string> DrainErrorAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var tail = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 2 * MaximumRetainedErrorChars) tail.Remove(0, tail.Length - MaximumRetainedErrorChars);
        }

        if (tail.Length > MaximumRetainedErrorChars) tail.Remove(0, tail.Length - MaximumRetainedErrorChars);
        return tail.ToString();
    }

    private sealed record WorkerRequest(string Operation, int AppId, string? FileName, int? Size = null);

    private sealed record WorkerResponse(JsonElement Header, byte[] Data);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(WorkerRequest))]
    [JsonSerializable(typeof(SteamCloudFile[]), TypeInfoPropertyName = "SteamCloudFiles")]
    private partial class SteamWorkerJsonContext : JsonSerializerContext
    {
    }
}

internal sealed class SteamWorkerGameSession(
    ISteamWorkerChildProcess process,
    Task<string> stderrTask,
    TimeSpan closeTimeout) : ISteamGameSession
{
    private int _closed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        using var timeoutSource = new CancellationTokenSource(closeTimeout);
        try
        {
            await process.StandardInput.DisposeAsync().ConfigureAwait(false);
            var finalHeader = await SteamWorkerProcessRunner.ReadHeaderAsync(
                process.StandardOutput,
                timeoutSource.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(finalHeader);
            if (document.RootElement.GetProperty("type").GetString() != "ok")
            {
                var message = document.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new InvalidOperationException(message ?? "Steam game session returned an unexpected close response.");
            }

            var exitCode = await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"Steam game session exited with code {exitCode}."
                        : $"Steam game session exited with code {exitCode}: {stderr.Trim()}");
            }
        }
        catch (OperationCanceledException)
        {
            await SteamWorkerProcessRunner.KillAndWaitAsync(process).ConfigureAwait(false);
            throw new TimeoutException($"Steam game session did not stop within {closeTimeout.TotalSeconds:0.###} seconds.");
        }
        catch
        {
            await SteamWorkerProcessRunner.KillAndWaitAsync(process).ConfigureAwait(false);
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }
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

    // Buffered once and reused: the response header is framed byte by byte, and the payload that follows it must come
    // from the same buffer. Without it every header byte is a separate read of the pipe.
    public Stream StandardOutput => _standardOutput ??= new BufferedStream(_process.StandardOutput.BaseStream, 64 * 1024);

    private Stream? _standardOutput;

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
