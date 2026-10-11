using System.Diagnostics;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

internal static class DefaultFontResolver
{
    public static SKTypeface Resolve(string executable = "fc-match", int timeoutMilliseconds = 3000)
    {
        using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                ArgumentList = { "--format", "%{file}" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        bool started = false;
        try
        {
            started = process.Start();
            if (!started)
                return SKTypeface.Default;

            // Drain both pipes while waiting, and bound the entire query, including output reads.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellation.Token);
            Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, error)
                .WaitAsync(cancellation.Token).GetAwaiter().GetResult();

            string file = output.GetAwaiter().GetResult().Trim();
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(file))
                return SKTypeface.FromFile(file) ?? SKTypeface.Default;
        }
        catch (Exception ex)
        {
            Log.CreateLogger(typeof(DefaultFontResolver)).LogWarning(ex,
                "Failed to resolve the default font with {Executable}; using the Skia default", executable);
        }
        finally
        {
            cancellation.Cancel();
            if (started)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(1000);
                    }
                }
                catch (Exception ex)
                {
                    Log.CreateLogger(typeof(DefaultFontResolver)).LogWarning(ex,
                        "Failed to stop the default font query {Executable}", executable);
                }
            }
        }

        return SKTypeface.Default;
    }
}
