using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beutl.Media;

[SupportedOSPlatform("linux")]
internal sealed partial class LinuxFontMatchProcess : IDisposable
{
    private int _pid;

    public LinuxFontMatchProcess(string executable)
    {
        var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        AnonymousPipeServerStream? error = null;
        try
        {
            error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            StandardOutput = new StreamReader(output);
            StandardError = new StreamReader(error);
            Start(executable, output, error);
        }
        catch
        {
            try { Stop(); }
            finally
            {
                output.Dispose();
                error?.Dispose();
            }
            throw;
        }
    }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public int ExitCode { get; private set; }

    private unsafe void Start(string executable, AnonymousPipeServerStream output, AnonymousPipeServerStream error)
    {
        // Opaque spawn objects on Linux glibc/musl fit within these aligned buffers.
        nint* actions = stackalloc nint[128];
        nint* attributes = stackalloc nint[128];
        Check(InitializeActions((nint)actions));
        try
        {
            Check(InitializeAttributes((nint)attributes));
            try
            {
                Check(AddDuplicate((nint)actions, output.ClientSafePipeHandle.DangerousGetHandle().ToInt32(), 1));
                Check(AddDuplicate((nint)actions, error.ClientSafePipeHandle.DangerousGetHandle().ToInt32(), 2));
                Check(SetFlags((nint)attributes, 0x02)); // POSIX_SPAWN_SETPGROUP
                Check(SetGroup((nint)attributes, 0)); // The child's PID becomes its group ID before exec.
                using var arguments = new Utf8Vector([executable, "--format", "%{file}"]);
                using var environment = new Utf8Vector(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                    .Select(entry => $"{entry.Key}={entry.Value}").ToArray());
                fixed (nint* argv = arguments.Values, envp = environment.Values)
                {
                    Check(Spawn(out int pid, executable, (nint)actions, (nint)attributes, argv, envp));
                    _pid = pid;
                }
            }
            finally
            {
                DestroyAttributes((nint)attributes);
            }
        }
        finally
        {
            DestroyActions((nint)actions);
        }

        output.DisposeLocalCopyOfClientHandle();
        error.DisposeLocalCopyOfClientHandle();
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // WNOWAIT retains the exited parent until group cleanup, preventing PID/group-ID reuse.
            if (WaitId(1, _pid, out SignalInfo info, 0x01000005) != 0) // P_PID, WEXITED | WNOHANG | WNOWAIT
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 4) continue; // EINTR
                throw new Win32Exception(error);
            }
            if (info.Signal != 0) return;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Stop()
    {
        if (_pid == 0) return;

        // Kill the dedicated group even when the parent is already a zombie: children may hold its pipes.
        if (Kill(-_pid, 9) != 0 && Marshal.GetLastPInvokeError() != 3) // SIGKILL, ESRCH
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            int result = WaitPid(_pid, out int status, 1); // WNOHANG
            if (result == _pid)
            {
                _pid = 0;
                ExitCode = (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
                return;
            }
            if (result < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 4) continue; // EINTR
                throw new Win32Exception(error);
            }
            if (elapsed.ElapsedMilliseconds >= 1000)
                throw new TimeoutException("The default font query did not stop after SIGKILL.");
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        try { Stop(); }
        finally
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    private static void Check(int error)
    {
        if (error != 0) throw new Win32Exception(error);
    }

    private sealed class Utf8Vector : IDisposable
    {
        public Utf8Vector(string[] values)
        {
            Values = new nint[values.Length + 1];
            try
            {
                for (int i = 0; i < values.Length; i++)
                    Values[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public nint[] Values { get; }

        public void Dispose()
        {
            foreach (nint value in Values) Marshal.FreeCoTaskMem(value);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct SignalInfo
    {
        [FieldOffset(0)] public int Signal;
    }

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    private static partial int InitializeActions(nint actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    private static partial int DestroyActions(nint actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    private static partial int AddDuplicate(nint actions, int source, int target);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    private static partial int InitializeAttributes(nint attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    private static partial int DestroyAttributes(nint attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    private static partial int SetFlags(nint attributes, short flags);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setpgroup")]
    private static partial int SetGroup(nint attributes, int group);

    [LibraryImport("libc", EntryPoint = "posix_spawnp", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int Spawn(out int pid, string executable, nint actions, nint attributes, nint* arguments, nint* environment);

    [LibraryImport("libc", EntryPoint = "waitid", SetLastError = true)]
    private static partial int WaitId(int type, int pid, out SignalInfo info, int options);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int pid, out int status, int options);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
}
