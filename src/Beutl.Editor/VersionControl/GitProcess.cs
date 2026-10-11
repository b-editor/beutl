using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beutl.Editor.VersionControl;

// Runs one command through System.Diagnostics.Process with every standard stream redirected.
//
// A process the command leaves behind can keep its pipes open: a hook's background job after the
// command exits, or, after a kill, a descendant the kill could not reach (on Unix, one whose parent
// had already exited). The pipes are therefore drained only for a bounded time once the command has
// exited or been killed; then reading stops and the readers return what they have read. Nothing
// waits for such a process: a repository lock it still holds surfaces as Git's own lock error on a
// later command.
internal static partial class GitProcess
{
    private const int SigTerm = 15;
    private static readonly TimeSpan s_drainGracePeriod = TimeSpan.FromSeconds(2);

    // The readers get a token that stops them; a stopped reader returns what it has read. Throws
    // Win32Exception when the command cannot start, TimeoutException when the timeout elapses first,
    // and OperationCanceledException when the caller cancels.
    //
    // A kill leaves the lock files Git holds, such as .git/index.lock, behind. Given a stop grace
    // period, a command that is canceled or times out on Unix is first sent SIGTERM, on which Git
    // removes its lock files and signals the filters and hooks it runs, and its process tree is
    // killed only if it is still running when the grace period ends. A descendant that Git does not
    // stop then outlives it, as a descendant of an exited command does. Windows has no such request
    // for a process without a console, so there the command is killed at once.
    public static async Task<(int ExitCode, TOutput Output, string Error)> RunAsync<TOutput>(
        ProcessStartInfo startInfo,
        byte[]? standardInput,
        Func<StreamReader, CancellationToken, Task<TOutput>> readStandardOutput,
        Func<StreamReader, CancellationToken, Task<string>> readStandardError,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        TimeSpan? stopGracePeriod = null)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        try
        {
            using var stopPipes = new CancellationTokenSource();
            Task<TOutput> output = readStandardOutput(process.StandardOutput, stopPipes.Token);
            Task<string> error = readStandardError(process.StandardError, stopPipes.Token);
            Task input = WriteStandardInputAsync(
                process.StandardInput.BaseStream,
                standardInput,
                stopPipes.Token);
            Task pipes = Task.WhenAll(input, output, error);
            Task exit = process.WaitForExitAsync(CancellationToken.None);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout is { } limit)
            {
                cancellation.CancelAfter(limit);
            }

            try
            {
                await exit.WaitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (stopGracePeriod is { } gracePeriod && RequestStop(process))
                {
                    await WaitWithinAsync(exit, gracePeriod).ConfigureAwait(false);
                }

                KillProcessTree(process);
                await DrainAsync(Task.WhenAll(exit, pipes)).ConfigureAwait(false);
                stopPipes.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Git did not finish within {timeout}.");
            }

            // The command has exited, so its result stands: the caller's deadline no longer applies,
            // and the drain is bounded on its own.
            if (await DrainAsync(pipes).ConfigureAwait(false))
            {
                await pipes.ConfigureAwait(false);
            }
            else
            {
                stopPipes.Cancel();
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

    // Reads text to the end of the pipe, or until reading is stopped, and returns what it read.
    public static async Task<(string Text, bool Complete)> ReadTextAsync(
        TextReader reader,
        CancellationToken stopReading)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer, stopReading).ConfigureAwait(false)) > 0)
            {
                text.Append(buffer, 0, count);
            }
        }
        catch (OperationCanceledException) when (stopReading.IsCancellationRequested)
        {
            return (text.ToString(), false);
        }

        return (text.ToString(), true);
    }

    // On Windows this also ends the descendants of a command that has already exited, because the
    // handle Process holds keeps its id reserved. On Unix nothing reserves the id once the command has
    // been reaped, so an exited command is left alone. Process.Kill already skips a child whose exit
    // it has recorded, and it records the exit when it reaps the child; this guard keeps the rule
    // explicit instead of relying on that.
    internal static void KillProcessTree(Process process, Action<Process>? killProcessTree = null)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && process.HasExited)
            {
                return;
            }

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

    // Sends SIGTERM to a command that is still running and reports whether it was sent. Like
    // KillProcessTree, it leaves an exited command alone, so a reused id is never signaled.
    private static bool RequestStop(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return !process.HasExited && SendSignal(process.Id, SigTerm) == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or Win32Exception
                                   or NotSupportedException
                                   or DllNotFoundException
                                   or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int SendSignal(int processId, int signal);

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

    // Waits up to the grace period and reports whether the task finished in time.
    private static Task<bool> DrainAsync(Task task)
    {
        return WaitWithinAsync(task, s_drainGracePeriod);
    }

    private static async Task<bool> WaitWithinAsync(Task task, TimeSpan period)
    {
        Task observed = ObserveAsync(task);
        try
        {
            await observed.WaitAsync(period).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
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
