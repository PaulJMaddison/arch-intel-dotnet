namespace ArchIntel.IO;

public static class Paths
{
    public const string ToolDirectoryName = ".archintel";
    public const string CacheDirectoryName = "cache";
    public const string ReportsDirectoryName = "";

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

        var root = GetDefaultRootDirectory(baseDirectory);
        if (string.IsNullOrWhiteSpace(fallbackLeaf))
        {
            return root;
        }

        return Path.Combine(root, fallbackLeaf);
    }
}
