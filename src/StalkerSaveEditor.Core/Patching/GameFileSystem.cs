namespace StalkerSaveEditor.Core.Patching;

/// <summary>Filesystem boundary shared by game-file patch providers and the Companion installer.</summary>
internal interface IGameFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option);
    IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option);
    byte[] ReadAllBytes(string path);
    string ReadAllText(string path);
    Stream OpenRead(string path);
    FileAttributes GetAttributes(string path);
    void CreateDirectory(string path);
    void WriteAllBytes(string path, byte[] bytes);
    void Move(string source, string destination, bool overwrite);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
}

internal sealed class PhysicalGameFileSystem : IGameFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) => Directory.EnumerateFiles(path, pattern, option);
    public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option) => Directory.EnumerateDirectories(path, pattern, option);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
}

/// <summary>Writes a complete game-managed file to a sibling temporary file, then atomically renames it.</summary>
internal static class AtomicGameFileWriter
{
    public static void Write(IGameFileSystem fileSystem, string path, byte[] bytes, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Target path has no directory.", nameof(path));
        fileSystem.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            fileSystem.WriteAllBytes(temporary, bytes);
            fileSystem.Move(temporary, path, overwrite);
        }
        finally
        {
            if (fileSystem.FileExists(temporary)) fileSystem.DeleteFile(temporary);
        }
    }
}
