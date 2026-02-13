using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public sealed record CanonicalProjectIdentity(
    string ProjectId,
    string RoslynProjectId,
    string ProjectName,
    string ProjectPath);

public static class ProjectIdentity
{
    public static CanonicalProjectIdentity Create(Project project, string repoRootPath)
    {
        var projectPath = NormalizeProjectPath(project, repoRootPath);
        return new CanonicalProjectIdentity(
            CreateStableId(projectPath),
            project.Id.Id.ToString(),
            project.Name,
            projectPath);
    }

    public static string CreateStableId(Project project, string repoRootPath)
    {
        return CreateStableId(NormalizeProjectPath(project, repoRootPath));
    }

    private static string CreateStableId(string normalizedProjectPath)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedProjectPath));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeProjectPath(Project project, string repoRootPath)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            return project.Name.ToLowerInvariant();
        }

        return CanonicalPath.Normalize(project.FilePath, repoRootPath);
    }
}
