using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public static class ProjectIdentity
{
    public static string CreateStableId(Project project, string repoRootPath)
    {
        var normalized = NormalizeProjectPath(project, repoRootPath);

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeProjectPath(Project project, string repoRootPath)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            return project.Name.ToLowerInvariant();
        }

        var fullPath = Path.GetFullPath(project.FilePath);
        var repoRoot = Path.GetFullPath(repoRootPath);
        var relative = Path.GetRelativePath(repoRoot, fullPath);
        return relative.Replace('\\', '/').ToLowerInvariant();
    }
}
