using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

// A Git command together with every process it starts, owned from launch. On Linux and macOS the
// command leads a new session, and on Windows it runs in a job object of its own. A descendant that
// outlives the command is still reached through that group, where a walk of the process tree loses it
// as soon as its parent has exited.
//
// A descendant that leaves the group on purpose (setsid, setpgid, or CREATE_BREAKAWAY_FROM_JOB) is not
// owned: it is neither killed nor waited for, and closing the command's pipes detaches it instead.
internal abstract class GitProcess : IDisposable
{
    public abstract int Id { get; }

    public abstract StreamWriter StandardInput { get; }

    public abstract StreamReader StandardOutput { get; }

    public abstract StreamReader StandardError { get; }

    // False when the platform could not give the command a group of its own. Kill then falls back to
    // the process tree, and WaitForGroupExitAsync can only observe the launched process.
    public abstract bool OwnsDescendants { get; }

    public static GitProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardInput
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError)
        {
            throw new ArgumentException(
                "A Git process is started directly with all standard streams redirected.",
                nameof(startInfo));
        }

        if (!OperatingSystem.IsWindows() && UnixGitProcess.IsSupported)
        {
            return UnixGitProcess.Start(startInfo);
        }

        return ManagedGitProcess.Start(startInfo);
    }

    // Completes when the launched process has exited. Its exit does not release the group.
    public abstract Task WaitForExitAsync();

    // Valid once WaitForExitAsync has completed. Collecting the status may release the group, so a
    // kill has to come before it. False when the status could not be collected, which is never a
    // success.
    public abstract bool TryGetExitCode(out int exitCode);

    // Terminates every process the group still owns.
    public abstract void Kill();

    // Completes once the launched process has exited and no owned process remains, releasing the
    // group. A member that survives Kill keeps it incomplete until the member has gone. Call it only
    // after Kill; otherwise it would wait for background work a finished command left running.
    public abstract Task WaitForGroupExitAsync();

    public void CloseStandardStreams()
    {
        TryDispose(() => StandardInput.BaseStream);
        TryDispose(() => StandardOutput.BaseStream);
        TryDispose(() => StandardError.BaseStream);
    }

    public abstract void Dispose();

    private protected static async Task PollUntilAsync(Func<bool> condition)
    {
        int delay = 1;
        while (!condition())
        {
            await Task.Delay(delay).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 1000);
        }
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
