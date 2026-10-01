using System.Runtime.InteropServices;

namespace StalkerSaveEditor.Core.Storage;

/// <summary>
/// Replace-by-rename write for the application's own small files (caches, profiles, hotkeys): a unique temp file in
/// the same folder, flushed to disk, then moved over the target. Concurrent writers never share a temp name, a
/// failed write leaves no temp behind, and readers see either the old or the new file. Not for game or save files:
/// those go through their own journaled writers.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes, bool ownerOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            DurableFile.WriteNew(temp, bytes, ownerOnly);
            DurableFile.Move(temp, full, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static void WriteAllText(string path, string text) =>
        WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));
}

/// <summary>
/// The one "create a new file and make it durable" step every writer uses: fails if the file exists, writes through
/// to disk, optionally restricts it to the owner (saves, backups, journals, drafts), and removes a half-written file.
/// </summary>
public static class DurableFile
{
    public static void WriteNew(string path, ReadOnlySpan<byte> bytes, bool ownerOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var created = false;
        try
        {
            using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            });
            created = true;
            if (ownerOnly && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Rename plus a flush of the destination folder. On POSIX a rename lives in the directory entry, so without
    /// this a power loss shortly after "saved" can bring the old file back (or lose a new one).
    /// </summary>
    public static void Move(string source, string destination, bool overwrite)
    {
        File.Move(source, destination, overwrite);
        SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
    }

    /// <summary>
    /// Best effort: Windows has no directory flush (NTFS journals the rename), the browser has no disk, and a file
    /// system that refuses fsync on a directory must not turn a finished write into an error.
    /// </summary>
    public static void SyncDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        try
        {
            var descriptor = Posix.Open(System.Text.Encoding.UTF8.GetBytes(directory + "\0"), Posix.ReadOnly);
            if (descriptor < 0) return;
            try
            {
                _ = Posix.Fsync(descriptor);
            }
            finally
            {
                _ = Posix.Close(descriptor);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private static class Posix
    {
        public const int ReadOnly = 0;

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(byte[] path, int flags);

        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        public static extern int Fsync(int descriptor);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int descriptor);
    }
}
