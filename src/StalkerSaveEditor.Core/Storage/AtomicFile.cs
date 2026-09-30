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
            File.Move(temp, full, overwrite: true);
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
}
