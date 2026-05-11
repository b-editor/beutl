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
            // Existence alone is not enough — a stale or non-executable file at a probed
            // location would otherwise shadow a working ffmpeg on PATH.
            if (File.Exists(candidate) && CanLaunch(candidate))
                return candidate;
        }

        return CanLaunch("ffmpeg") ? "ffmpeg" : null;
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

    private static bool CanLaunch(string path)
    {
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return false;

            if (!process.WaitForExit(2000))
            {
                // Disposing the Process object does not terminate the underlying process,
                // so explicitly kill it to avoid leaving a stray ffmpeg around.
                try { process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }
}
