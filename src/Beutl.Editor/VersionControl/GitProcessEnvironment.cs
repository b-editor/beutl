using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

internal static class GitProcessEnvironment
{
    private static readonly string[] s_macToolDirectories = ["/opt/homebrew/bin", "/usr/local/bin"];

    public static void ConfigureToolSearchPath(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        startInfo.Environment.TryGetValue("PATH", out string? path);
        startInfo.Environment["PATH"] = GetMacToolSearchPath(path);
    }

    internal static string GetMacToolSearchPath(string? path)
    {
        // Finder/Dock launches do not inherit the user's shell configuration. System Git can
        // still be found, but its LFS hooks and filters also need Homebrew's git-lfs on PATH.
        path ??= "/usr/bin:/bin:/usr/sbin:/sbin";
        var directories = new HashSet<string>(path.Split(':'), StringComparer.Ordinal);
        foreach (string directory in s_macToolDirectories)
        {
            if (directories.Add(directory))
            {
                // Keep the caller's tool selection and empty/relative PATH components intact.
                path += ":" + directory;
            }
        }

        return path;
    }
}
