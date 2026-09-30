using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Updater;

public enum UpdateState
{
    Current,
    Available,
    Unavailable,
    Invalid,
}

public enum UpdateInstallState
{
    Succeeded,
    Cancelled,
    Failed,
    OpenedExternally,
}

public sealed record UpdateArtifact(
    string Target,
    string Architecture,
    string Kind,
    string File,
    long Size,
    string Sha256,
    string Url)
{
    public async Task VerifyAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.File.Exists(path))
        {
            throw new UpdateManifestException("Update artifact is missing.");
        }

        var info = new FileInfo(path);
        if (info.Length != Size)
        {
            throw new UpdateManifestException($"Update artifact size mismatch: expected {Size}, got {info.Length}.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(digest).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(Sha256)))
        {
            throw new UpdateManifestException("Update artifact SHA-256 mismatch.");
        }
    }
}

public sealed record UpdateManifest(
    int Schema,
    string Channel,
    string Version,
    string SourceCommit,
    string PublishedAt,
    IReadOnlyDictionary<string, UpdateArtifact> Artifacts)
{
    public UpdateArtifact Select(string target, string architecture, string kind)
    {
        var key = (target, kind) switch
        {
            ("windows", "portable") => "windows-x86_64",
            ("windows", "installer") => "windows-installer-x86_64",
            ("linux", "portable") => "linux-x86_64",
            ("linux", "package") => "linux-deb-amd64",
            ("macos", "disk-image") => architecture == "arm64" ? "macos-arm64" : "macos-x86_64",
            _ => throw new UpdateManifestException($"No update artifact exists for {target}/{kind}.")
        };

        if (!Artifacts.TryGetValue(key, out var artifact) || artifact.Architecture != architecture || artifact.Kind != kind)
        {
            throw new UpdateManifestException($"No update artifact exists for {target}/{architecture}/{kind}.");
        }

        return artifact;
    }
}

public sealed record UpdateCheckResult(
    UpdateState State,
    UpdateManifest? Manifest = null,
    UpdateArtifact? Artifact = null,
    string? Error = null);

public sealed record UpdateInstallation(
    string Target,
    string Architecture,
    string Kind,
    string Root,
    string Executable);

public sealed record UpdateProgress(
    string Stage,
    string Message,
    long? CompletedBytes = null,
    long? TotalBytes = null);

public sealed record UpdateInstallResult(UpdateInstallState State, int? ExitCode, string Message);

public sealed class UpdateManifestException(string message, Exception? innerException = null)
    : IOException(message, innerException);

internal interface IUpdateProcessRunner
{
    Task<int> RunAsync(
        ProcessStartInfo startInfo,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Checks, downloads, verifies, and explicitly hands off an update artifact.</summary>
public sealed class UpdateService : IDisposable
{
    public const string DefaultDownloadHost = "save-editor-downloads.save-editor.workers.dev";
    public const string DefaultManifestUrl = "https://save-editor-downloads.save-editor.workers.dev/latest.json";
    public const string UpdateUserAgent = "SaveEditor-updater/1";

    private const int MaximumManifestBytes = 2 * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly Regex StaleDownloadPattern = new("^SaveEditor-update-[0-9]+-.+", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    private readonly Uri _manifestUri;
    private readonly string _target;
    private readonly string _architecture;
    private readonly string _kind;
    private readonly HashSet<string> _allowedHosts;
    private readonly HashSet<string> _allowedSchemes;
    private readonly TimeSpan _timeout;
    private readonly Func<string, string?> _findExecutable;
    private readonly IUpdateProcessRunner _processRunner;
    private bool _disposed;
    private readonly string _signingPublicKeyPem;

    public UpdateService(
        string currentVersion,
        string? manifestUrl = null,
        string? target = null,
        string? architecture = null,
        string? kind = null)
        : this(
            currentVersion,
            manifestUrl ?? DefaultManifestUrl,
            target ?? GetDefaultTarget(),
            architecture ?? GetDefaultArchitecture(target ?? GetDefaultTarget()),
            kind ?? GetDefaultKind(target ?? GetDefaultTarget()),
            new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false },
            [DefaultDownloadHost],
            ["https"],
            Path.GetTempPath(),
            FindExecutable,
            new UpdateProcessRunner(),
            TimeSpan.FromSeconds(15))
    {
    }

    public UpdateService(string currentVersion, UpdateInstallation installation, string? manifestUrl = null)
        : this(
            currentVersion,
            manifestUrl,
            installation?.Target ?? throw new ArgumentNullException(nameof(installation)),
            installation.Architecture,
            GetArtifactKind(installation))
    {
    }

    internal UpdateService(
        string currentVersion,
        string manifestUrl,
        string target,
        string architecture,
        string kind,
        HttpMessageHandler handler,
        IEnumerable<string> allowedHosts,
        IEnumerable<string> allowedSchemes,
        string staleDownloadDirectory,
        Func<string, string?> findExecutable,
        IUpdateProcessRunner processRunner,
        TimeSpan? timeout = null,
        string? signingPublicKeyPem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(allowedHosts);
        ArgumentNullException.ThrowIfNull(allowedSchemes);
        ArgumentNullException.ThrowIfNull(findExecutable);
        ArgumentNullException.ThrowIfNull(processRunner);

        _currentVersion = currentVersion;
        _manifestUri = ParseUri(manifestUrl, "manifest URL");
        _target = target;
        _architecture = architecture;
        _kind = kind;
        _allowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);
        _allowedSchemes = new HashSet<string>(allowedSchemes, StringComparer.OrdinalIgnoreCase);
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Update request timeout must be positive.");
        }

        ValidateUri(_manifestUri);
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _findExecutable = findExecutable;
        _processRunner = processRunner;
        _signingPublicKeyPem = signingPublicKeyPem ?? UpdateSignature.PublicKeyPem;
        _ = CleanupStaleDownloads(staleDownloadDirectory);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            using var response = await SendTrustedAsync(_manifestUri, cancellationToken).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(response.Content, MaximumManifestBytes, cancellationToken)
                .ConfigureAwait(false);
            await VerifyManifestSignatureAsync(bytes, cancellationToken).ConfigureAwait(false);
            var manifest = ParseManifest(new UTF8Encoding(false, true).GetString(bytes));
            var artifact = manifest.Select(_target, _architecture, _kind);
            var state = CompareVersions(_currentVersion, manifest.Version) >= 0
                ? UpdateState.Current
                : UpdateState.Available;
            return new UpdateCheckResult(state, manifest, artifact);
        }
        catch (UpdateManifestException exception)
        {
            return new UpdateCheckResult(UpdateState.Invalid, Error: exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateState.Unavailable, Error: "Update request timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or JsonException or DecoderFallbackException)
        {
            return new UpdateCheckResult(UpdateState.Unavailable, Error: $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public async Task<string> DownloadAsync(
        UpdateArtifact artifact,
        string destination,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ValidateArtifact(artifact);
        var targetPath = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(targetPath)
            ?? throw new UpdateManifestException("Update download destination has no parent directory.");
        Directory.CreateDirectory(parent);
        if (Directory.Exists(targetPath))
        {
            throw new UpdateManifestException("Update download destination is a directory.");
        }

        if (File.Exists(targetPath))
        {
            try
            {
                await artifact.VerifyAsync(targetPath, cancellationToken).ConfigureAwait(false);
                progress?.Report(new UpdateProgress("download", "Reused the verified update download.", artifact.Size, artifact.Size));
                return targetPath;
            }
            catch (UpdateManifestException)
            {
                File.Delete(targetPath);
            }
        }

        var temporaryPath = Path.Combine(parent, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.part");
        try
        {
            using var response = await SendTrustedAsync(ParseUri(artifact.Url, "artifact URL"), cancellationToken)
                .ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } contentLength && contentLength > artifact.Size)
            {
                throw new UpdateManifestException($"Update download is larger than the manifest size {artifact.Size}.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > artifact.Size)
                    {
                        throw new UpdateManifestException($"Update download is larger than the manifest size {artifact.Size}.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new UpdateProgress("download", "Downloading update.", total, artifact.Size));
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await artifact.VerifyAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, targetPath, overwrite: true);
            progress?.Report(new UpdateProgress("download", "Update download verified.", artifact.Size, artifact.Size));
            return targetPath;
        }
        catch (UpdateManifestException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
        {
            TryDelete(temporaryPath);
            throw new UpdateManifestException($"Update download failed: {exception.Message}", exception);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    /// <summary>Installs only a previously downloaded and reverified artifact after an explicit caller action.</summary>
    public async Task<UpdateInstallResult> InstallAsync(
        UpdateArtifact artifact,
        string verifiedArchive,
        UpdateInstallation installation,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(installation);
        try
        {
            ValidateArtifact(artifact);
            ValidateInstallationPair(artifact, installation);
            await artifact.VerifyAsync(verifiedArchive, cancellationToken).ConfigureAwait(false);
            var (startInfo, externalHandoff) = BuildInstallerStartInfo(artifact, verifiedArchive, installation);
            progress?.Report(new UpdateProgress("install", externalHandoff ? "Opening the verified installer." : "Starting the verified update installer."));
            var exitCode = await _processRunner.RunAsync(startInfo, progress, cancellationToken).ConfigureAwait(false);
            if (exitCode is 126 or 127)
            {
                return new UpdateInstallResult(UpdateInstallState.Cancelled, exitCode, "The installer authorization was cancelled.");
            }

            if (exitCode != 0)
            {
                return new UpdateInstallResult(UpdateInstallState.Failed, exitCode, $"Installer exited with code {exitCode}.");
            }

            return externalHandoff
                ? new UpdateInstallResult(UpdateInstallState.OpenedExternally, exitCode, "The verified installer was opened.")
                : new UpdateInstallResult(UpdateInstallState.Succeeded, exitCode, "Update installation completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UpdateInstallResult(UpdateInstallState.Cancelled, null, "Update installation was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return new UpdateInstallResult(UpdateInstallState.Failed, null, exception.Message);
        }
    }

    /// <summary>Removes only pre-versioned per-process update cache files.</summary>
    public static int CleanupStaleDownloads(string? directory = null)
    {
        var folder = directory ?? Path.GetTempPath();
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var removed = 0;
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return 0;
        }

        foreach (var entry in entries)
        {
            if (!StaleDownloadPattern.IsMatch(Path.GetFileName(entry)) || !File.Exists(entry))
            {
                continue;
            }

            try
            {
                File.Delete(entry);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Stale cache cleanup is best-effort; active update work is never deleted.
            }
        }

        return removed;
    }

    public static int CompareVersions(string current, string latest)
    {
        var left = ParseVersion(current);
        var right = ParseVersion(latest);
        var coreComparison = CompareCore(left.Core, right.Core);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (left.Prerelease.Length == 0 || right.Prerelease.Length == 0)
        {
            return left.Prerelease.Length == right.Prerelease.Length ? 0 : left.Prerelease.Length == 0 ? 1 : -1;
        }

        var count = Math.Min(left.Prerelease.Length, right.Prerelease.Length);
        for (var index = 0; index < count; index++)
        {
            var comparison = CompareIdentifier(left.Prerelease[index], right.Prerelease[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Prerelease.Length.CompareTo(right.Prerelease.Length);
    }

    private async Task<HttpResponseMessage> SendTrustedAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            ValidateUri(currentUri);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.Accept.ParseAdd("application/json, application/octet-stream");
            request.Headers.UserAgent.ParseAdd(UpdateUserAgent);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Update server request timed out.");
            }

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                {
                    throw new UpdateManifestException("Update server returned a redirect without a location.");
                }

                if (redirect == MaximumRedirects)
                {
                    throw new UpdateManifestException("Update server exceeded the redirect limit.");
                }

                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                ValidateUri(currentUri);
                continue;
            }

            ValidateUri(response.RequestMessage?.RequestUri ?? currentUri);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"Update server returned HTTP {(int)statusCode}.", null, statusCode);
            }

            return response;
        }

        throw new UpdateManifestException("Update server exceeded the redirect limit.");
    }

    private void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !_allowedSchemes.Contains(uri.Scheme)
            || !_allowedHosts.Contains(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new UpdateManifestException("Update URL is outside the trusted download policy.");
        }
    }

    /// <summary>latest.json.sig next to the manifest must verify against the embedded publisher key (fail closed).</summary>
    private async Task VerifyManifestSignatureAsync(byte[] manifest, CancellationToken cancellationToken)
    {
        var signatureUri = new Uri(_manifestUri.AbsoluteUri + ".sig");
        byte[] signature;
        try
        {
            using var response = await SendTrustedAsync(signatureUri, cancellationToken).ConfigureAwait(false);
            signature = await ReadBoundedAsync(response.Content, UpdateSignature.MaximumSignatureBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new UpdateManifestException("Update manifest signature is missing: " + exception.Message);
        }

        if (!UpdateSignature.Verify(manifest, signature, _signingPublicKeyPem))
        {
            throw new UpdateManifestException("Update manifest signature is invalid; the update was not trusted.");
        }
    }

    private UpdateManifest ParseManifest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ReadInt(root, "schema") != 1)
            {
                throw new UpdateManifestException("Unsupported update manifest schema.");
            }

            var channel = ReadString(root, "channel");
            if (channel != "stable")
            {
                throw new UpdateManifestException("Unsupported update release channel.");
            }

            var version = ReadString(root, "version");
            _ = ParseVersion(version);
            var commit = ReadString(root, "source_commit");
            if (!Regex.IsMatch(commit, "^[0-9a-f]{40,64}$", RegexOptions.CultureInvariant))
            {
                throw new UpdateManifestException("source_commit must be a lowercase Git SHA.");
            }

            var publishedAt = ReadString(root, "published_at");
            if (!root.TryGetProperty("artifacts", out var artifactRoot) || artifactRoot.ValueKind != JsonValueKind.Object)
            {
                throw new UpdateManifestException("Update manifest artifacts are missing.");
            }

            var artifacts = new Dictionary<string, UpdateArtifact>(StringComparer.Ordinal);
            foreach (var required in new[] { "windows-x86_64", "linux-x86_64", "linux-deb-amd64" })
            {
                if (!artifactRoot.TryGetProperty(required, out var artifactJson))
                {
                    throw new UpdateManifestException("Update manifest artifacts are incomplete.");
                }

                var artifact = ParseArtifact(artifactJson);
                if (artifact.Target != required)
                {
                    throw new UpdateManifestException($"Artifact target does not match manifest key {required}.");
                }

                artifacts.Add(required, artifact);
            }

            var optionalKeys = new HashSet<string>(["windows-installer-x86_64", "macos-arm64", "macos-x86_64"], StringComparer.Ordinal);
            if (root.TryGetProperty("optional_artifacts", out var optionalRoot))
            {
                if (optionalRoot.ValueKind != JsonValueKind.Object)
                {
                    throw new UpdateManifestException("optional_artifacts must be an object.");
                }

                foreach (var optional in optionalRoot.EnumerateObject())
                {
                    if (!optionalKeys.Contains(optional.Name))
                    {
                        throw new UpdateManifestException("optional_artifacts contains an unsupported target.");
                    }

                    var artifact = ParseArtifact(optional.Value);
                    if (artifact.Target != optional.Name)
                    {
                        throw new UpdateManifestException($"Artifact target does not match manifest key {optional.Name}.");
                    }

                    artifacts.Add(optional.Name, artifact);
                }
            }

            return new UpdateManifest(1, channel, version, commit, publishedAt, artifacts);
        }
        catch (UpdateManifestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new UpdateManifestException("Update manifest is invalid.", exception);
        }
    }

    private UpdateArtifact ParseArtifact(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateManifestException("Artifact must be an object.");
        }

        var target = ReadString(value, "target");
        var architecture = ReadString(value, "architecture");
        var kind = ReadString(value, "kind");
        var file = ReadString(value, "file");
        if (!value.TryGetProperty("size", out var sizeValue)
            || sizeValue.ValueKind != JsonValueKind.Number
            || !sizeValue.TryGetInt64(out var size)
            || size < 0)
        {
            throw new UpdateManifestException("artifact.size must be a non-negative integer.");
        }

        var sha256 = ReadString(value, "sha256");
        if (!Regex.IsMatch(sha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
        {
            throw new UpdateManifestException("artifact.sha256 must be lowercase SHA-256.");
        }

        if (file is "." or ".." || file.IndexOfAny(['/', '\\', ':']) >= 0 || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new UpdateManifestException("artifact.file must be a plain filename.");
        }

        if (kind is not ("portable" or "package" or "installer" or "disk-image"))
        {
            throw new UpdateManifestException("artifact.kind is unsupported.");
        }

        var url = ReadString(value, "url");
        var uri = ParseUri(url, "artifact URL");
        ValidateUri(uri);
        if (!string.Equals(uri.AbsolutePath, $"/{file}", StringComparison.Ordinal))
        {
            throw new UpdateManifestException("Artifact URL does not match its filename.");
        }

        return new UpdateArtifact(target, architecture, kind, file, size, sha256, uri.AbsoluteUri);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && length > maximumBytes)
        {
            throw new UpdateManifestException("Update manifest is too large.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new UpdateManifestException("Update manifest is too large.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private void ValidateArtifact(UpdateArtifact artifact)
    {
        if (artifact.Size < 0
            || !Regex.IsMatch(artifact.Sha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant)
            || string.IsNullOrWhiteSpace(artifact.File)
            || artifact.File is "." or ".."
            || artifact.File.Contains('/')
            || artifact.File.Contains('\\'))
        {
            throw new UpdateManifestException("Update artifact metadata is invalid.");
        }

        var uri = ParseUri(artifact.Url, "artifact URL");
        ValidateUri(uri);
        if (!string.Equals(uri.AbsolutePath, $"/{artifact.File}", StringComparison.Ordinal))
        {
            throw new UpdateManifestException("Artifact URL does not match its filename.");
        }
    }

    private static void ValidateInstallationPair(UpdateArtifact artifact, UpdateInstallation installation)
    {
        var expectedTarget = (installation.Target, installation.Kind) switch
        {
            ("windows", "portable") => "windows-x86_64",
            ("windows", "installer") => "windows-installer-x86_64",
            ("linux", "portable") => "linux-x86_64",
            ("linux", "package") => "linux-deb-amd64",
            ("macos", "portable") => "macos-arm64",
            _ => throw new UpdateManifestException("Update installation type is unsupported.")
        };
        var expectedKind = installation.Target == "macos" ? "disk-image" : installation.Kind;
        if (artifact.Target != expectedTarget
            || artifact.Architecture != installation.Architecture
            || artifact.Kind != expectedKind)
        {
            throw new UpdateManifestException("Update artifact does not match the current installation.");
        }
    }

    private (ProcessStartInfo StartInfo, bool ExternalHandoff) BuildInstallerStartInfo(
        UpdateArtifact artifact,
        string archive,
        UpdateInstallation installation)
    {
        var fullArchive = Path.GetFullPath(archive);
        ProcessStartInfo startInfo;
        if (installation.Target == "linux" && installation.Kind == "package")
        {
            if (!artifact.File.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateManifestException("Linux package handoff requires a verified .deb artifact.");
            }

            var pkexec = _findExecutable("pkexec");
            var aptGet = _findExecutable("apt-get");
            if (pkexec is null || aptGet is null)
            {
                var openPackage = _findExecutable("xdg-open");
                if (openPackage is null)
                {
                    throw new UpdateManifestException("Linux package manager handoff is unavailable (pkexec/xdg-open missing).");
                }

                startInfo = new ProcessStartInfo(openPackage);
                startInfo.ArgumentList.Add(fullArchive);
                return (ConfigureProgress(startInfo), true);
            }

            startInfo = new ProcessStartInfo(pkexec);
            startInfo.ArgumentList.Add(aptGet);
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(fullArchive);
            return (ConfigureProgress(startInfo), false);
        }

        if (installation.Target == "windows" && installation.Kind == "installer" && artifact.File.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return (ConfigureProgress(new ProcessStartInfo(fullArchive)), false);
        }

        if (installation.Target == "macos" && installation.Kind == "disk-image" && artifact.File.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
        {
            var open = _findExecutable("open")
                ?? throw new UpdateManifestException("macOS installer handoff is unavailable.");
            startInfo = new ProcessStartInfo(open);
            startInfo.ArgumentList.Add(fullArchive);
            return (ConfigureProgress(startInfo), true);
        }

        throw new UpdateManifestException("Automatic replacement of portable .zip or .tar.gz installations is not supported by this installer handoff.");
    }

    private static ProcessStartInfo ConfigureProgress(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        return startInfo;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(value.GetString()))
        {
            throw new UpdateManifestException($"{name} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static int ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : throw new UpdateManifestException($"{name} must be an integer.");

    private static Uri ParseUri(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new UpdateManifestException($"{field} is invalid.");
        }

        return uri;
    }

    private static string GetDefaultTarget() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "macos"
            : OperatingSystem.IsLinux()
                ? "linux"
                : throw new UpdateManifestException("Update platform is unsupported.");

    private static string GetDefaultArchitecture(string target) =>
        target == "macos" && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";

    private static string GetDefaultKind(string target) => target == "macos" ? "disk-image" : "portable";

    private static string GetArtifactKind(UpdateInstallation installation) => (installation.Target, installation.Kind) switch
    {
        ("macos", _) => "disk-image",
        ("linux", "package") => "package",
        ("windows", "installer") => "installer",
        _ => "portable",
    };

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static VersionParts ParseVersion(string value)
    {
        var match = VersionPattern.Match(value);
        if (!match.Success)
        {
            throw new UpdateManifestException($"Invalid version: {value}.");
        }

        var core = new[]
        {
            BigInteger.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
        };
        var prerelease = match.Groups[4].Success
            ? match.Groups[4].Value.Split('.').Select(ParseIdentifier).ToArray()
            : [];
        return new VersionParts(core, prerelease);
    }

    private static VersionIdentifier ParseIdentifier(string value)
    {
        if (value.All(char.IsDigit))
        {
            if (value.Length > 1 && value[0] == '0')
            {
                throw new UpdateManifestException("Numeric prerelease identifiers must not contain leading zeroes.");
            }

            return new VersionIdentifier(true, BigInteger.Parse(value, CultureInfo.InvariantCulture), value);
        }

        return new VersionIdentifier(false, BigInteger.Zero, value);
    }

    private static int CompareCore(BigInteger[] left, BigInteger[] right)
    {
        for (var index = 0; index < left.Length; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int CompareIdentifier(VersionIdentifier left, VersionIdentifier right)
    {
        if (left.Numeric != right.Numeric)
        {
            return left.Numeric ? -1 : 1;
        }

        return left.Numeric
            ? left.Number.CompareTo(right.Number)
            : string.Compare(left.Text, right.Text, StringComparison.Ordinal);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    private sealed record VersionParts(BigInteger[] Core, VersionIdentifier[] Prerelease);

    private sealed record VersionIdentifier(bool Numeric, BigInteger Number, string Text);

    private sealed class UpdateProcessRunner : IUpdateProcessRunner
    {
        public async Task<int> RunAsync(
            ProcessStartInfo startInfo,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                throw new InvalidOperationException("Update installer process could not be started.");
            }

            var standardOutput = DrainAsync(process.StandardOutput, progress, cancellationToken);
            var standardError = DrainAsync(process.StandardError, progress, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }

                throw;
            }
        }

        private static async Task DrainAsync(
            StreamReader reader,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                progress?.Report(new UpdateProgress("install", line));
            }
        }
    }
}
