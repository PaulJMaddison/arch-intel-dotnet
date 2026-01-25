using System.Security.Cryptography;
using System.Text;
using ArchIntel.IO;

namespace ArchIntel.Analysis;

public sealed class DocumentHashService
{
    private readonly IFileSystem _fileSystem;

    public DocumentHashService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public string GetContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string GetFileHash(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
