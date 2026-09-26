namespace StalkerSaveEditor.Core.Backups;

public interface ILocalSaveFileSystem
{
    byte[] ReadAllBytes(string path);

    void WriteNew(string path, byte[] data);

    void Replace(string sourcePath, string destinationPath);

    void DeleteIfExists(string path);
}
