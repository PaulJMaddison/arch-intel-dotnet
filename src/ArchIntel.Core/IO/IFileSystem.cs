namespace ArchIntel.IO;

public interface IFileSystem
{
    bool FileExists(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);
    void CreateDirectory(string path);
    Stream OpenRead(string path);
}
