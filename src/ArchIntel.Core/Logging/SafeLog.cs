using Microsoft.Extensions.Logging;

namespace ArchIntel.Logging;

public static class SafeLog
{
    private const int MaxValueLength = 256;

    public static void Info(ILogger logger, string message, params object?[] args)
    {
        logger.LogInformation(message, args);
    }

    public static void Warn(ILogger logger, string message, params object?[] args)
    {
        logger.LogWarning(message, args);
    }

    public static void Error(ILogger logger, string message, params object?[] args)
    {
        logger.LogError(message, args);
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFileName(path);
    }

    public static string SanitizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= MaxValueLength)
        {
            return trimmed;
        }

        return trimmed[..MaxValueLength] + "...";
    }
}
