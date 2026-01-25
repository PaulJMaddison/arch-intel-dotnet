using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public static class ProjectIdentity
{
    public static string CreateStableId(Project project)
    {
        var source = project.FilePath ?? project.Name;
        var normalized = project.FilePath is null
            ? source
            : Path.GetFullPath(source);

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
