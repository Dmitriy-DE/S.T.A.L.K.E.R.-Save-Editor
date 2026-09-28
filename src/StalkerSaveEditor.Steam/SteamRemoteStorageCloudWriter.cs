using System.Diagnostics;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Steam;

public enum SteamRemoteStorageWriteStatus
{
    Verified,
    Uncertain,
}

public sealed record SteamRemoteStorageWriteAvailability(bool CanWrite, string Reason, string? ReleaseId);

public sealed record SteamRemoteStorageWriteResult(
    SteamRemoteStorageWriteStatus Status,
    int AppId,
    string ReleaseId,
    string RemotePath,
    string BackupPath,
    string RecoveryPath,
    string OutputSha256,
    string? Reason);

public sealed class SteamRemoteStorageWriteException(string message, Exception? innerException = null)
    : IOException(message, innerException);

/// <summary>Explicitly writes a prepared X-Ray save through Steam RemoteStorage.</summary>
public sealed class SteamRemoteStorageCloudWriter
{
    private readonly Func<string?> _findLibrary;
    private readonly ISteamWorkerProcessRunner _worker;
    private readonly TimeSpan _persistedPollInterval;

    public SteamRemoteStorageCloudWriter()
        : this(SteamLibraryLocator.FindLibraryPath, new SteamWorkerProcessRunner(), TimeSpan.FromSeconds(2))
    {
    }

    internal SteamRemoteStorageCloudWriter(
        Func<string?> findLibrary,
        ISteamWorkerProcessRunner worker,
        TimeSpan persistedPollInterval)
    {
        ArgumentNullException.ThrowIfNull(findLibrary);
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(persistedPollInterval, TimeSpan.Zero);

        _findLibrary = findLibrary;
        _worker = worker;
        _persistedPollInterval = persistedPollInterval;
    }

    public SteamRemoteStorageWriteAvailability CheckAvailability(int appId)
    {
        if (!SteamCloudSaveProfiles.TryGet(appId, out var profile))
        {
            return new SteamRemoteStorageWriteAvailability(
                false,
                "RemoteStorage save writing is supported only for official X-Ray trilogy releases.",
                null);
        }

        try
        {
            return _findLibrary() is not null
                ? new SteamRemoteStorageWriteAvailability(
                    true,
                    "Steam libsteam_api is available; the Steam client session is checked when you write.",
                    profile.ReleaseId)
                : new SteamRemoteStorageWriteAvailability(false, "Steam libsteam_api was not found.", profile.ReleaseId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new SteamRemoteStorageWriteAvailability(
                false,
                $"Could not locate Steam libsteam_api: {exception.Message}",
                profile.ReleaseId);
        }
    }

    public async Task<SteamRemoteStorageWriteResult> WriteAsync(
        int appId,
        PreparedEdit prepared,
        string remotePath,
        string backupDirectory,
        int persistedTimeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var availability = CheckAvailability(appId);
        if (!availability.CanWrite || availability.ReleaseId is null ||
            !SteamCloudSaveProfiles.TryGet(appId, out var profile))
        {
            throw new SteamRemoteStorageWriteException(availability.Reason);
        }

        string normalizedPath;
        try
        {
            if (!profile.TryNormalizeSavePath(remotePath, out normalizedPath))
            {
                throw new InvalidDataException("Remote path is outside this release's save-file allow-list.");
            }

            if (prepared.Data.IsEmpty || prepared.Data.Length > SteamNativeRemoteStorage.MaximumFileBytes ||
                !profile.HasExpectedFormat(prepared.Data.Span))
            {
                throw new InvalidDataException("Prepared bytes are not a valid save for the selected Steam release.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or XRayFormatException)
        {
            throw new SteamRemoteStorageWriteException(exception.Message, exception);
        }

        try
        {
            var transport = new RemoteStorageWriteTransport(
                appId,
                normalizedPath,
                profile,
                _findLibrary,
                _worker,
                _persistedPollInterval);
            var receipt = await SteamCloudWriteTransaction.UploadAsync(
                transport,
                prepared,
                normalizedPath,
                backupDirectory,
                persistedTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
            return new SteamRemoteStorageWriteResult(
                receipt.Status == CloudWriteStatus.Verified
                    ? SteamRemoteStorageWriteStatus.Verified
                    : SteamRemoteStorageWriteStatus.Uncertain,
                appId,
                profile.ReleaseId,
                receipt.RemotePath,
                receipt.BackupPath,
                receipt.RecoveryPath,
                receipt.OutputSha256,
                receipt.Reason);
        }
        catch (CloudTransactionException exception)
        {
            throw new SteamRemoteStorageWriteException(exception.Message, exception);
        }
    }

    private sealed class RemoteStorageWriteTransport(
        int appId,
        string remotePath,
        SteamCloudSaveProfile profile,
        Func<string?> findLibrary,
        ISteamWorkerProcessRunner worker,
        TimeSpan persistedPollInterval) : ICloudWriteTransport
    {
        public CloudWriteCapability WriteCapability =>
            new(findLibrary() is not null, "Steam RemoteStorage writer is available.");

        public ValueTask<byte[]> ReadFileAsync(string requestedPath, CancellationToken cancellationToken = default)
        {
            EnsurePath(requestedPath);
            return new ValueTask<byte[]>(worker.ReadAsync(
                appId,
                remotePath,
                SteamReadOnlyClient.DefaultTimeout,
                cancellationToken));
        }

        public async ValueTask WriteFileAsync(
            string requestedPath,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            EnsurePath(requestedPath);
            await worker.WriteAsync(
                appId,
                remotePath,
                data,
                SteamReadOnlyClient.DefaultTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        public ValueTask SyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<bool> WaitForPersistedAsync(
            string requestedPath,
            int expectedSize,
            int timeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            EnsurePath(requestedPath);
            var elapsed = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            while (elapsed.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var files = await worker.ListAsync(
                    appId,
                    SteamReadOnlyClient.DefaultTimeout,
                    cancellationToken).ConfigureAwait(false);
                var matching = files.FirstOrDefault(file =>
                    string.Equals(
                        file.Name.Replace('\\', '/'),
                        remotePath,
                        StringComparison.Ordinal));
                if (matching is { Exists: true, IsPersisted: true } && matching.Size == expectedSize)
                {
                    return true;
                }

                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(
                    remaining < persistedPollInterval ? remaining : persistedPollInterval,
                    cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private void EnsurePath(string requestedPath)
        {
            if (!profile.TryNormalizeSavePath(requestedPath, out var normalized) ||
                !string.Equals(normalized, remotePath, StringComparison.Ordinal))
            {
                throw new CloudWriteNotAttemptedException("RemoteStorage operation path changed after validation.");
            }
        }
    }
}
