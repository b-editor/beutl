using System.Diagnostics;

namespace Beutl.Services.WindowCapture;

internal static class FFmpegBinaryLocator
{
    private static string ExecutableFileName =>
        OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    public static string? Find()
    {
        foreach (string candidate in EnumerateCandidatePaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return CanLaunchOnPath() ? "ffmpeg" : null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        string home = BeutlEnvironment.GetHomeDirectoryPath();
        yield return Path.Combine(home, "ffmpeg", ExecutableFileName);
        yield return Path.Combine(home, "ffmpeg", "bin", ExecutableFileName);

        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/bin/ffmpeg";
            yield return "/usr/local/bin/ffmpeg";
            yield return "/opt/homebrew/opt/ffmpeg/bin/ffmpeg";
            yield return "/usr/local/opt/ffmpeg/bin/ffmpeg";
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native",
                "ffmpeg.exe");
        }
    }

    private static bool CanLaunchOnPath()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return false;

            process.WaitForExit(2000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
