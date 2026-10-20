using System.Diagnostics;

namespace Beutl.UnitTests.TestInfrastructure;

internal static class TestProcesses
{
    // Runs the process to its exit and returns what it wrote to standard output and standard error. A process still
    // running at the timeout is killed with its whole tree, and the TimeoutException carries what it printed.
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        ProcessStartInfo start,
        TimeSpan timeout,
        string? input = null)
    {
        start.RedirectStandardInput = input is not null;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            try
            {
                try { await process.StandardInput.WriteAsync(input); }
                finally { process.StandardInput.Close(); }
            }
            catch (IOException)
            {
                // A process that exits before reading its input makes both the write and Close's flush fail with
                // EPIPE. The caller's exit code and output assertions still report the early exit.
            }
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException error)
        {
            // Kill does not wait, so reap the tree before reporting what it printed.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"{start.FileName} {string.Join(' ', start.ArgumentList)} did not exit within {timeout}.\n"
                + await stdout + await stderr,
                error);
        }

        return (process.ExitCode, await stdout, await stderr);
    }
}
