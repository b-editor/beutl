using System.Runtime.InteropServices;

namespace Beutl.UnitTests.Editor.VersionControl;

// libc calls the process tests use to look at a child the way a runtime or another reaper would.
internal static partial class UnixProcessTestMethods
{
    private const int ErrorNoProcess = 3;

    // A zombie is still signalable, so this stays true until the process has been reaped.
    internal static bool IsSignalable(int pid)
        => kill(pid, 0) == 0 || Marshal.GetLastPInvokeError() != ErrorNoProcess;

    // Blocks until the child exits and reaps it; returns the pid, or -1 when it was reaped elsewhere.
    internal static int Reap(int pid) => waitpid(pid, out _, 0);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int waitpid(int pid, out int status, int options);
}
