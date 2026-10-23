using System.Diagnostics;
using System.Runtime.Versioning;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

[SupportedOSPlatform("linux")]
internal static class DefaultFontResolver
{
    private const int TimeoutMilliseconds = 3000;

    public static SKTypeface Resolve(string executable = "fc-match")
    {
        using var cancellation = new CancellationTokenSource(TimeoutMilliseconds);
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(executable, ["--format", "%{file}"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            // Drain both pipes while waiting, and bound the entire query, including output reads.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellation.Token);
            Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, error)
                .WaitAsync(cancellation.Token).GetAwaiter().GetResult();

            string file = output.GetAwaiter().GetResult().TrimEnd('\r', '\n');
            SKTypeface? face = process.ExitCode == 0 && !string.IsNullOrEmpty(file) ? SKTypeface.FromFile(file) : null;
            if (face is not null)
                return face;

            string diagnostic = error.GetAwaiter().GetResult();
            Log.CreateLogger(typeof(DefaultFontResolver)).LogDebug(
                "Default font query {Executable} fell back to the Skia default: exit code {ExitCode}, font path {FontFile}, stderr {StandardError}",
                executable, process.ExitCode, file, diagnostic[..Math.Min(diagnostic.Length, 512)]);
        }
        catch (Exception ex)
        {
            Log.CreateLogger(typeof(DefaultFontResolver)).LogWarning(ex,
                "Failed to resolve the default font with {Executable}; using the Skia default", executable);
        }
        finally
        {
            cancellation.Cancel();
            if (process is not null)
            {
                try
                {
                    // A query that timed out is still running, with any children it started.
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Log.CreateLogger(typeof(DefaultFontResolver)).LogWarning(ex,
                        "Failed to stop the default font query {Executable}", executable);
                }

                process.Dispose();
            }
        }

        return SKTypeface.Default;
    }
}
