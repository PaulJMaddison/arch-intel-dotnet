namespace ArchIntel.Analysis;

public static class CanonicalPath
{
    public static string Normalize(string? path, string? repoRootPath, bool? windowsMode = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedInput = path.Trim().Replace('\\', '/');
        var useWindowsRules = windowsMode ?? OperatingSystem.IsWindows();
        var comparison = useWindowsRules ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!Path.IsPathRooted(normalizedInput))
        {
            return NormalizeSeparators(normalizedInput, useWindowsRules);
        }

        var fullPath = Path.GetFullPath(normalizedInput);
        if (!string.IsNullOrWhiteSpace(repoRootPath))
        {
            var root = Path.GetFullPath(repoRootPath);
            var relative = Path.GetRelativePath(root, fullPath);
            if (IsPathUnderRoot(relative))
            {
                return NormalizeSeparators(relative, useWindowsRules);
            }

            if (fullPath.StartsWith(root, comparison))
            {
                return NormalizeSeparators(Path.GetRelativePath(root, fullPath), useWindowsRules);
            }
        }

        return NormalizeSeparators(fullPath, useWindowsRules);
    }

    private static bool IsPathUnderRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return true;
        }

        return !relativePath.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }

    private static string NormalizeSeparators(string path, bool useWindowsRules)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0)
        {
            normalized = string.Empty;
        }

        return useWindowsRules ? normalized.ToLowerInvariant() : normalized;
    }
}
