using System.Runtime.InteropServices;
using Microsoft.Build.Locator;

namespace ArchIntel.Analysis;

public static class MsBuildAvailability
{
    public static bool IsAvailable()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return true;
        }

        try
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            if (instances.Any(instance => HasSdkDirectory(instance.MSBuildPath)))
            {
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        var dotnetRoot = ResolveDotnetRoot();
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return false;
        }

        var sdkPath = Path.Combine(dotnetRoot, "sdk");
        return Directory.Exists(sdkPath) && Directory.EnumerateDirectories(sdkPath).Any();
    }

    private static bool HasSdkDirectory(string? msBuildPath)
    {
        if (string.IsNullOrWhiteSpace(msBuildPath))
        {
            return false;
        }

        var sdkPath = Path.Combine(msBuildPath, "Sdks");
        return Directory.Exists(sdkPath) && Directory.EnumerateDirectories(sdkPath).Any();
    }

    private static string? ResolveDotnetRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return envRoot;
        }

        var envRootX86 = Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)");
        if (!string.IsNullOrWhiteSpace(envRootX86) && Directory.Exists(envRootX86))
        {
            return envRootX86;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var windowsCandidates = new[]
            {
                Path.Combine(programFiles, "dotnet"),
                Path.Combine(programFilesX86, "dotnet")
            };

            return windowsCandidates.FirstOrDefault(Directory.Exists);
        }

        var unixCandidates = new[]
        {
            "/usr/share/dotnet",
            "/usr/local/share/dotnet",
            "/opt/dotnet"
        };

        return unixCandidates.FirstOrDefault(Directory.Exists);
    }
}
