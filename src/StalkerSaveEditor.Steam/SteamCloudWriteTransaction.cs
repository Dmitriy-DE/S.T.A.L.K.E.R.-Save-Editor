using System.Globalization;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Steam;

internal interface ICloudWriteTransport
{
    CloudWriteCapability WriteCapability { get; }

    ValueTask<byte[]> ReadFileAsync(string remotePath, CancellationToken cancellationToken = default);

    ValueTask WriteFileAsync(
        string remotePath,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    ValueTask SyncAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> WaitForPersistedAsync(
        string remotePath,
        int expectedSize,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}

internal sealed record CloudWriteCapability(bool Writable, string Reason);

internal sealed class CloudWriteNotAttemptedException(string message) : IOException(message);

internal enum CloudWriteStatus
{
    Verified,
    Uncertain,
}

internal sealed record CloudWriteReceipt(
    CloudWriteStatus Status,
    string RemotePath,
    string BackupPath,
    string RecoveryPath,
    string OutputSha256,
    string? Reason = null);

internal sealed class CloudTransactionException(string message, Exception? innerException = null)
    : IOException(message, innerException);

internal static class SteamCloudWriteTransaction
{
    public static async Task<CloudWriteReceipt> UploadAsync(
        ICloudWriteTransport transport,
        PreparedEdit prepared,
        string remotePath,
        string backupDirectory,
        int persistedTimeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(persistedTimeoutSeconds);

        var output = prepared.Data;
        var outputSha256 = Sha256(output.Span);
        if (!string.Equals(outputSha256, prepared.OutputSha256, StringComparison.Ordinal))
        {
            throw new CloudTransactionException("Prepared output SHA256 does not match its bytes.");
        }

        var capability = transport.WriteCapability;
        if (!capability.Writable)
        {
            throw new CloudTransactionException($"Cloud write is disabled before any I/O: {capability.Reason}");
        }

        byte[] fresh;
        try
        {
            fresh = await transport.ReadFileAsync(remotePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new CloudTransactionException($"Cloud read before write failed: {exception.Message}", exception);
        }

        var freshSha256 = Sha256(fresh);
        if (!string.Equals(freshSha256, prepared.SourceSha256, StringComparison.Ordinal))
        {
            throw new CloudTransactionException(
                $"Cloud source changed after analysis: expected {prepared.SourceSha256}, found {freshSha256}.");
        }

        var backupPath = string.Empty;
        var recoveryPath = string.Empty;
        try
        {
            var directory = Path.GetFullPath(backupDirectory);
            Directory.CreateDirectory(directory);
            (backupPath, recoveryPath) = ArtifactPaths(remotePath, directory);
            WriteExclusive(backupPath, fresh);
            WriteExclusive(recoveryPath, output.Span);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new CloudTransactionException($"Could not create cloud backup and recovery files: {exception.Message}", exception);
        }

        try
        {
            await transport.WriteFileAsync(remotePath, output, cancellationToken).ConfigureAwait(false);
        }
        catch (CloudWriteNotAttemptedException exception)
        {
            throw new CloudTransactionException($"Cloud write was not attempted: {exception.Message}", exception);
        }
        catch (Exception exception)
        {
            return Uncertain(
                remotePath,
                backupPath,
                recoveryPath,
                outputSha256,
                $"WriteFile result is uncertain after request: {exception.Message}");
        }

        try
        {
            await transport.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Uncertain(
                remotePath,
                backupPath,
                recoveryPath,
                outputSha256,
                $"Cloud sync failed after WriteFile; result is uncertain: {exception.Message}");
        }

        bool persisted;
        try
        {
            persisted = await transport.WaitForPersistedAsync(
                remotePath,
                output.Length,
                persistedTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Uncertain(
                remotePath,
                backupPath,
                recoveryPath,
                outputSha256,
                $"Persisted check failed after WriteFile; result is uncertain: {exception.Message}");
        }

        if (!persisted)
        {
            return Uncertain(
                remotePath,
                backupPath,
                recoveryPath,
                outputSha256,
                "Steam did not confirm persisted=true after WriteFile; result is uncertain.");
        }

        byte[] readback;
        try
        {
            readback = await transport.ReadFileAsync(remotePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Uncertain(
                remotePath,
                backupPath,
                recoveryPath,
                outputSha256,
                $"Cloud read-back failed after WriteFile; result is uncertain: {exception.Message}");
        }

        if (!string.Equals(Sha256(readback), outputSha256, StringComparison.Ordinal))
        {
            return Uncertain(
                remotePath,
                backupPath,
                recoveryPath,
                outputSha256,
                "Cloud read-back SHA256 mismatch after WriteFile; result is uncertain.");
        }

        return new CloudWriteReceipt(
            CloudWriteStatus.Verified,
            remotePath,
            backupPath,
            recoveryPath,
            outputSha256);
    }

    private static CloudWriteReceipt Uncertain(
        string remotePath,
        string backupPath,
        string recoveryPath,
        string outputSha256,
        string reason) => new(
        CloudWriteStatus.Uncertain,
        remotePath,
        backupPath,
        recoveryPath,
        outputSha256,
        reason);

    private static (string BackupPath, string RecoveryPath) ArtifactPaths(string remotePath, string directory)
    {
        var leaf = remotePath.Replace('\\', '/').Split('/').Last();
        var stem = Path.GetFileNameWithoutExtension(leaf);
        var safeStem = new string(stem.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_').ToArray()).Trim('.', '_');
        if (safeStem.Length > 80)
        {
            safeStem = safeStem[..80];
        }

        if (safeStem.Length == 0)
        {
            safeStem = "cloud-save";
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
        var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var prefix = $"{safeStem}_{stamp}_{token}";
        return (
            Path.Combine(directory, $"{prefix}_ORIGINAL.sav"),
            Path.Combine(directory, $"{prefix}_EDITED.sav"));
    }

    private static void WriteExclusive(string path, ReadOnlySpan<byte> data)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
