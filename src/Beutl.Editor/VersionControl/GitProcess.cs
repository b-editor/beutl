using System.ComponentModel;
using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

// Runs one command through System.Diagnostics.Process with every standard stream redirected.
//
// Cancellation and timeouts kill the command's process tree. A descendant can escape that kill (on
// Unix, one whose parent has already exited) and keep the output pipes open, so they are drained only
// for a bounded time before reading stops. Nothing waits for such a descendant: a repository lock it
// still holds surfaces as Git's own lock error on a later command.
internal static class GitProcess
{
    private static readonly TimeSpan s_drainGracePeriod = TimeSpan.FromSeconds(2);

    // Throws Win32Exception when the command cannot start, TimeoutException when the timeout elapses
    // first, and OperationCanceledException when the caller cancels.
    public static async Task<(int ExitCode, TOutput Output, string Error)> RunAsync<TOutput>(
        ProcessStartInfo startInfo,
        byte[]? standardInput,
        Func<StreamReader, CancellationToken, Task<TOutput>> readStandardOutput,
        Func<StreamReader, CancellationToken, Task<string>> readStandardError,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        try
        {
            using var stopReading = new CancellationTokenSource();
            Task<TOutput> output = readStandardOutput(process.StandardOutput, stopReading.Token);
            Task<string> error = readStandardError(process.StandardError, stopReading.Token);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout is { } limit)
            {
                cancellation.CancelAfter(limit);
            }

            Task input = WriteStandardInputAsync(
                process.StandardInput.BaseStream,
                standardInput,
                cancellation.Token);
            Task completion = Task.WhenAll(
                process.WaitForExitAsync(CancellationToken.None),
                input,
                output,
                error);
            try
            {
                await completion.WaitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                KillProcessTree(process);
                await DrainAsync(completion).ConfigureAwait(false);
                stopReading.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Git did not finish within {timeout}.");
            }

            return (
                process.ExitCode,
                await output.ConfigureAwait(false),
                await error.ConfigureAwait(false));
        }
        finally
        {
            CloseStandardStreams(process);
        }
    }

    // On Windows this also ends the descendants of a command that has already exited, because the
    // handle Process holds keeps its id reserved. On Unix that id may have been reused, so Process
    // leaves an exited command alone.
    internal static void KillProcessTree(Process process, Action<Process>? killProcessTree = null)
    {
        try
        {
            killProcessTree ??= static target => target.Kill(entireProcessTree: true);
            killProcessTree(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or Win32Exception
                                   or NotSupportedException
                                   or AggregateException)
        {
        }
    }

    private static async Task WriteStandardInputAsync(
        Stream stream,
        byte[]? input,
        CancellationToken cancellationToken)
    {
        // A command that exits, or closes its input, before reading all of it breaks the pipe. Both
        // the write and the close report that; the exit code and stderr say what happened.
        try
        {
            if (input is not null)
            {
                await stream.WriteAsync(input, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }

    // Gives the killed tree time to exit and the readers time to reach the end of the pipes.
    private static async Task DrainAsync(Task completion)
    {
        Task drained = ObserveAsync(completion);
        try
        {
            await drained.WaitAsync(s_drainGracePeriod).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    // Process leaves streams it has handed out open.
    private static void CloseStandardStreams(Process process)
    {
        TryDispose(() => process.StandardInput.BaseStream);
        TryDispose(() => process.StandardOutput.BaseStream);
        TryDispose(() => process.StandardError.BaseStream);
    }

    private static void TryDispose(Func<Stream> getStream)
    {
        try
        {
            getStream().Dispose();
        }
        catch (Exception)
        {
        }
    }
}
