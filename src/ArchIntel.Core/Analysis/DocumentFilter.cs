namespace ArchIntel.Analysis;

public sealed class DocumentFilter
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "obj",
        "bin",
        ".git",
        ".vs",
        "node_modules"
    };

    private static readonly string[] GeneratedSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".designer.cs",
        ".generated.cs",
        ".assemblyattributes.cs"
    ];

    public bool IsExcluded(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (GeneratedSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (fileName.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (ExcludedDirectories.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
