namespace ArchIntel.IO;

public static class Paths
{
    public const string ToolDirectoryName = ".archtool";
    public const string CacheDirectoryName = "cache";
    public const string ReportsDirectoryName = "reports";

    public static string GetDefaultRootDirectory(string baseDirectory)
    {
        return Path.Combine(baseDirectory, ToolDirectoryName);
    }

    public static string GetCacheDirectory(string baseDirectory, string? overridePath)
    {
        return ResolvePath(baseDirectory, overridePath, CacheDirectoryName);
    }

    public static string GetReportsDirectory(string baseDirectory, string? overridePath)
    {
        return ResolvePath(baseDirectory, overridePath, ReportsDirectoryName);
    }

    private static string ResolvePath(string baseDirectory, string? overridePath, string fallbackLeaf)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(GetDefaultRootDirectory(baseDirectory), fallbackLeaf);
    }
}
