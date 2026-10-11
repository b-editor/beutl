using System.Runtime.Versioning;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

[SupportedOSPlatform("linux")]
internal static class DefaultFontResolver
{
    public static SKTypeface Resolve(string executable = "fc-match", int timeoutMilliseconds = 3000)
    {
        using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
        LinuxFontMatchProcess? process = null;
        try
        {
            process = new LinuxFontMatchProcess(executable);

            // Drain both pipes while waiting, and bound the entire query, including output reads.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellation.Token);
            Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, error)
                .WaitAsync(cancellation.Token).GetAwaiter().GetResult();

            process.Stop();
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
                    process.Dispose();
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
