using System.ComponentModel;
using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

// System.Diagnostics.Process, for a start that cannot give the command a group of its own: a Unix C
// library without the posix_spawn chdir action, a runtime that reaps every child, or a Windows start
// that could not be made suspended. Descendants are reached through the process tree. On Windows the
// handle Process holds keeps the command's id reserved after it exits, so the tree is still walked then
// and cleanup waits until no descendant is left; on Unix the tree is walked only while the command is
// alive, and only the command itself can be waited for.
internal sealed class ManagedGitProcess : GitProcess
{
    private readonly Process _process;
    private volatile bool _killRequested;

    private ManagedGitProcess(Process process)
    {
        _process = process;
    }

    public override int Id => _process.Id;

    public override StreamWriter StandardInput => _process.StandardInput;

    public override StreamReader StandardOutput => _process.StandardOutput;

    public override StreamReader StandardError => _process.StandardError;

    public override bool OwnsDescendants => false;

    public static new ManagedGitProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        return new ManagedGitProcess(process);
    }

    public override Task WaitForExitAsync() => _process.WaitForExitAsync(CancellationToken.None);

    public override bool TryGetExitCode(out int exitCode)
    {
        exitCode = _process.ExitCode;
        return true;
    }

    public override void Kill()
    {
        _killRequested = true;
        KillTree(_process);
    }

    public override async Task WaitForGroupExitAsync()
    {
        await WaitForExitAsync().ConfigureAwait(false);
        if (OperatingSystem.IsWindows())
        {
            await PollUntilAsync(IsTreeGone).ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        CloseStandardStreams();
        _process.Dispose();
    }

    internal static void KillTree(Process process)
    {
        try
        {
            // On Windows the walk works from an exited command too; on Unix its id may already be reused.
            if (OperatingSystem.IsWindows() || !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or Win32Exception
                                   or NotSupportedException
                                   or AggregateException)
        {
        }
    }

    private bool IsTreeGone()
        => !OperatingSystem.IsWindows()
           || WindowsProcessTree.IsTreeGone(_process, _process.SafeHandle, _killRequested);
}
