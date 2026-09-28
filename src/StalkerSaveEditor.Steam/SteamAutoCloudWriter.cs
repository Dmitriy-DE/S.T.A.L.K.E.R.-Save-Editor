using System.Diagnostics;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Steam;

public interface ISteamCloudWebReader
{
    /// <summary>Lists web-readable Steam Cloud files for applications that use Auto-Cloud.</summary>
    Task<IReadOnlyList<SteamCloudFile>> ListCloudFilesAsync(
        int appId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<SteamCloudFile>>(
            new NotSupportedException("This Steam web reader does not provide cloud file listing."));

    /// <summary>Reads the current bytes from Steam Cloud, refreshing its file list and signed URL first.</summary>
    Task<byte[]> ReadFreshFileAsync(string remotePath, CancellationToken cancellationToken = default);
}

public enum SteamAutoCloudWriteStatus
{
    Verified,
    Uncertain,
}

public sealed record SteamAutoCloudAvailability(bool CanWrite, string Reason, string? LocalRoot);

public sealed record SteamAutoCloudWriteResult(
    SteamAutoCloudWriteStatus Status,
    string RemotePath,
    string BackupPath,
    string RecoveryPath,
    string OutputSha256,
    string? Reason);

public sealed class SteamAutoCloudWriteException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public sealed class SteamAutoCloudWriter
{
    private static readonly TimeSpan DefaultSessionSettle = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultSessionStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultReadPollInterval = TimeSpan.FromSeconds(5);
    private readonly ISteamCloudWebReader _webReader;
    private readonly Func<int, string?> _findRoot;
    private readonly ISteamGameSessionRunner _sessionRunner;
    private readonly TimeSpan _sessionSettle;
    private readonly TimeSpan _sessionStartupTimeout;
    private readonly TimeSpan _readPollInterval;

    public SteamAutoCloudWriter(ISteamCloudWebReader webReader, TimeSpan? sessionSettle = null)
        : this(
            webReader,
            SteamAutoCloudRootLocator.FindRoot,
            new SteamWorkerProcessRunner(),
            sessionSettle ?? DefaultSessionSettle,
            DefaultSessionStartupTimeout,
            DefaultReadPollInterval)
    {
    }

    internal SteamAutoCloudWriter(
        ISteamCloudWebReader webReader,
        Func<int, string?> findRoot,
        ISteamGameSessionRunner sessionRunner,
        TimeSpan sessionSettle,
        TimeSpan sessionStartupTimeout,
        TimeSpan readPollInterval)
    {
        ArgumentNullException.ThrowIfNull(webReader);
        ArgumentNullException.ThrowIfNull(findRoot);
        ArgumentNullException.ThrowIfNull(sessionRunner);
        if (sessionSettle < TimeSpan.Zero || sessionStartupTimeout <= TimeSpan.Zero || readPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionSettle), "Steam Auto-Cloud timing values are invalid.");
        }

        _webReader = webReader;
        _findRoot = findRoot;
        _sessionRunner = sessionRunner;
        _sessionSettle = sessionSettle;
        _sessionStartupTimeout = sessionStartupTimeout;
        _readPollInterval = readPollInterval;
    }

    public SteamAutoCloudAvailability CheckAvailability(int appId)
    {
        if (appId != SteamAutoCloudRootLocator.Stalker2AppId)
        {
            return new SteamAutoCloudAvailability(false, "Auto-Cloud writing is supported only for S.T.A.L.K.E.R. 2.", null);
        }

        try
        {
            var root = _findRoot(appId);
            return root is not null && Directory.Exists(Path.Combine(root, SteamAutoCloudRootLocator.Stalker2DirectoryName))
                ? new SteamAutoCloudAvailability(true, "S.T.A.L.K.E.R. 2 Auto-Cloud folder is available.", root)
                : new SteamAutoCloudAvailability(false, "The S.T.A.L.K.E.R. 2 WinAppDataLocal Auto-Cloud folder was not found.", null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new SteamAutoCloudAvailability(false, $"Could not locate the S.T.A.L.K.E.R. 2 Auto-Cloud folder: {exception.Message}", null);
        }
    }

    public async Task<SteamAutoCloudWriteResult> WriteAsync(
        int appId,
        PreparedEdit prepared,
        string remotePath,
        string backupDirectory,
        int persistedTimeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var availability = CheckAvailability(appId);
        if (!availability.CanWrite || availability.LocalRoot is null)
        {
            throw new SteamAutoCloudWriteException(availability.Reason);
        }

        string targetPath;
        string fullBackupDirectory;
        try
        {
            targetPath = SteamAutoCloudRootLocator.ResolveLocalPath(availability.LocalRoot, remotePath);
            fullBackupDirectory = Path.GetFullPath(backupDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new SteamAutoCloudWriteException($"The Auto-Cloud write path is invalid: {exception.Message}", exception);
        }

        if (!Directory.Exists(Path.GetDirectoryName(targetPath)!) || IsInside(fullBackupDirectory, availability.LocalRoot))
        {
            throw new SteamAutoCloudWriteException(
                "The Auto-Cloud save directory is missing or the backup directory is inside the Steam-synced folder.");
        }

        try
        {
            var transport = new SteamAutoCloudWriteTransport(
                appId,
                availability.LocalRoot,
                _webReader,
                _sessionRunner,
                _sessionSettle,
                _sessionStartupTimeout,
                _readPollInterval);
            var receipt = await SteamCloudWriteTransaction.UploadAsync(
                transport,
                prepared,
                remotePath,
                backupDirectory,
                persistedTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
            return new SteamAutoCloudWriteResult(
                receipt.Status == CloudWriteStatus.Verified
                    ? SteamAutoCloudWriteStatus.Verified
                    : SteamAutoCloudWriteStatus.Uncertain,
                receipt.RemotePath,
                receipt.BackupPath,
                receipt.RecoveryPath,
                receipt.OutputSha256,
                receipt.Reason);
        }
        catch (CloudTransactionException exception)
        {
            throw new SteamAutoCloudWriteException(exception.Message, exception);
        }
    }

    private static bool IsInside(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.GetFullPath(rootPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(candidate, root, comparison) ||
            candidate.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, comparison);
    }

    private sealed class SteamAutoCloudWriteTransport : ICloudWriteTransport
    {
        private const int MaximumFileBytes = 64 * 1024 * 1024;
        private readonly int _appId;
        private readonly string _localRoot;
        private readonly ISteamCloudWebReader _webReader;
        private readonly ISteamGameSessionRunner _sessionRunner;
        private readonly TimeSpan _sessionSettle;
        private readonly TimeSpan _sessionStartupTimeout;
        private readonly TimeSpan _readPollInterval;
        private byte[]? _preWriteCloudBytes;
        private string? _lastWriteSha256;
        private bool _writeAttempted;

        public SteamAutoCloudWriteTransport(
            int appId,
            string localRoot,
            ISteamCloudWebReader webReader,
            ISteamGameSessionRunner sessionRunner,
            TimeSpan sessionSettle,
            TimeSpan sessionStartupTimeout,
            TimeSpan readPollInterval)
        {
            _appId = appId;
            _localRoot = localRoot;
            _webReader = webReader;
            _sessionRunner = sessionRunner;
            _sessionSettle = sessionSettle;
            _sessionStartupTimeout = sessionStartupTimeout;
            _readPollInterval = readPollInterval;
        }

        public CloudWriteCapability WriteCapability =>
            _appId == SteamAutoCloudRootLocator.Stalker2AppId &&
            Directory.Exists(Path.Combine(_localRoot, SteamAutoCloudRootLocator.Stalker2DirectoryName))
                ? new CloudWriteCapability(true, "S.T.A.L.K.E.R. 2 Auto-Cloud uses a local file and game session.")
                : new CloudWriteCapability(false, "The S.T.A.L.K.E.R. 2 Auto-Cloud folder is unavailable.");

        public async ValueTask<byte[]> ReadFileAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            ValidateRemotePath(remotePath);
            var bytes = await _webReader.ReadFreshFileAsync(remotePath, cancellationToken).ConfigureAwait(false);
            if (!_writeAttempted)
            {
                _preWriteCloudBytes = bytes.ToArray();
            }

            return bytes;
        }

        public async ValueTask WriteFileAsync(
            string remotePath,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capability = WriteCapability;
            if (!capability.Writable)
            {
                throw new CloudWriteNotAttemptedException(capability.Reason);
            }

            if (_preWriteCloudBytes is null)
            {
                throw new CloudWriteNotAttemptedException("Fresh Steam web source bytes were not read before the local write.");
            }

            if (data.Length > MaximumFileBytes)
            {
                throw new CloudWriteNotAttemptedException("The Auto-Cloud output exceeds the supported save size.");
            }

            var targetPath = ResolveTarget(remotePath);
            if (!Directory.Exists(Path.GetDirectoryName(targetPath)!))
            {
                throw new CloudWriteNotAttemptedException("The local Auto-Cloud save directory does not exist.");
            }

            await using var session = await _sessionRunner.StartSessionAsync(
                _appId,
                _sessionStartupTimeout,
                cancellationToken).ConfigureAwait(false);
            if (_sessionSettle > TimeSpan.Zero)
            {
                await Task.Delay(_sessionSettle, cancellationToken).ConfigureAwait(false);
            }

            byte[] latestCloudBytes;
            try
            {
                latestCloudBytes = await _webReader.ReadFreshFileAsync(remotePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new CloudWriteNotAttemptedException($"Could not refresh the cloud source immediately before writing: {exception.Message}");
            }

            if (!latestCloudBytes.AsSpan().SequenceEqual(_preWriteCloudBytes))
            {
                throw new CloudWriteNotAttemptedException("Steam Cloud source changed before the local Auto-Cloud write.");
            }

            byte[] localBytes;
            try
            {
                localBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CloudWriteNotAttemptedException($"Could not verify the local Auto-Cloud source before writing: {exception.Message}");
            }

            if (!localBytes.AsSpan().SequenceEqual(_preWriteCloudBytes))
            {
                throw new CloudWriteNotAttemptedException("The local Auto-Cloud source changed after Steam game-session synchronization.");
            }

            WriteAtomically(targetPath, data.Span);
            _writeAttempted = true;
            _lastWriteSha256 = Sha256(data.Span);
        }

        public ValueTask SyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<bool> WaitForPersistedAsync(
            string remotePath,
            int expectedSize,
            int timeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            ValidateRemotePath(remotePath);
            if (_lastWriteSha256 is null)
            {
                return false;
            }

            var elapsed = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            while (elapsed.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var bytes = await _webReader.ReadFreshFileAsync(remotePath, cancellationToken).ConfigureAwait(false);
                    if (bytes.Length == expectedSize && string.Equals(Sha256(bytes), _lastWriteSha256, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A read failure is not proof that Steam has finished syncing; continue bounded polling.
                }

                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(remaining < _readPollInterval ? remaining : _readPollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            return false;
        }

        private string ResolveTarget(string remotePath)
        {
            try
            {
                return SteamAutoCloudRootLocator.ResolveLocalPath(_localRoot, remotePath);
            }
            catch (ArgumentException exception)
            {
                throw new CloudWriteNotAttemptedException(exception.Message);
            }
        }

        private void ValidateRemotePath(string remotePath)
        {
            _ = ResolveTarget(remotePath);
        }

        private static void WriteAtomically(string targetPath, ReadOnlySpan<byte> data)
        {
            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.editor-part");
            var committed = false;
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(data);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, targetPath, overwrite: true);
                committed = true;
            }
            catch (Exception exception) when (!committed && exception is IOException or UnauthorizedAccessException)
            {
                throw new CloudWriteNotAttemptedException($"The local Auto-Cloud file was not replaced: {exception.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (IOException) when (committed)
                {
                    // The destination is already committed; do not change its outcome due to temporary cleanup.
                }
            }
        }

        private static string Sha256(ReadOnlySpan<byte> bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
