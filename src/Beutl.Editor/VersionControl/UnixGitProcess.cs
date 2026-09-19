using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor.VersionControl;

// posix_spawn with a new session, so the command's process group is owned before its first
// instruction runs. Process has no way to ask for one.
//
// The launched process leads the group and is not reaped until the group is released. As an unreaped
// zombie its id cannot be given to another process, and neither can the group id while any member
// remains, so a signal sent to the group reaches only processes this command started.
[UnsupportedOSPlatform("windows")]
internal sealed partial class UnixGitProcess : GitProcess
{
    private const int OpaqueStorageSize = 1024;
    private const int SignalSetStorageSize = 256;
    private const int SignalInformationStorageSize = 512;
    private const int StreamBufferSize = 4096;
    private const short PosixSpawnSetProcessGroup = 0x02;
    private const short PosixSpawnSetSignalMask = 0x08;
    private const int IdTypeProcess = 1;
    private const int WaitNoHang = 1;
    private const int WaitExited = 4;
    private const int SignalKill = 9;
    private const int ErrorNoEntry = 2;
    private const int ErrorNoProcess = 3;
    private const int ErrorInterrupted = 4;
    private const int ErrorInvalidArgument = 22;

    private static readonly bool s_isSupported = ProbeSupport();
    // Set once a child turns out to have been reaped by something else. The runtime reaps every child
    // as soon as it exits when this process started with SIGCHLD ignored, so an id could be reused
    // before it is signalled. Later commands then keep Process, which the runtime reaps in step with.
    private static volatile bool s_childrenReapedElsewhere;

    private readonly object _sync = new();
    private readonly TaskCompletionSource _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _pid;
    private readonly bool _ownsGroup;
    private readonly StreamWriter _standardInput;
    private readonly StreamReader _standardOutput;
    private readonly StreamReader _standardError;
    private bool _exitObserved;
    private bool _killRequested;
    // Set once the launched process has been reaped, here or elsewhere. From then on its id may name
    // an unrelated process, so no signal is sent to it again.
    private bool _released;
    private bool _exitStatusCollected;
    private int _exitCode;
    private bool _disposed;

    private UnixGitProcess(
        int pid,
        SpawnedProcessState state,
        StreamWriter standardInput,
        StreamReader standardOutput,
        StreamReader standardError)
    {
        _pid = pid;
        _ownsGroup = state == SpawnedProcessState.LeadsGroup;
        _released = state == SpawnedProcessState.ReapedElsewhere;
        _standardInput = standardInput;
        _standardOutput = standardOutput;
        _standardError = standardError;
    }

    private enum SpawnedProcessState
    {
        LeadsGroup,
        InCallerGroup,
        ReapedElsewhere,
    }

    internal static bool IsSupported => s_isSupported && !s_childrenReapedElsewhere;

    internal static void ResetChildrenReapedElsewhereForTesting() => s_childrenReapedElsewhere = false;

    public override int Id => _pid;

    public override StreamWriter StandardInput => _standardInput;

    public override StreamReader StandardOutput => _standardOutput;

    public override StreamReader StandardError => _standardError;

    public override bool OwnsDescendants => _ownsGroup;

    private static short PosixSpawnSetSession => OperatingSystem.IsMacOS() ? (short)0x0400 : (short)0x0080;

    private static int WaitNoWait => OperatingSystem.IsMacOS() ? 0x20 : 0x01000000;

    public static new UnixGitProcess Start(ProcessStartInfo startInfo)
    {
        string path = ResolveExecutable(startInfo.FileName);
        string? workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? null
            : startInfo.WorkingDirectory;
        // Process passes the file name as given as the first argument, and so does this.
        string[] arguments = [startInfo.FileName, .. startInfo.ArgumentList];
        string[] environment = startInfo.Environment
            .Where(static pair => pair.Value is not null)
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        var placeholders = new List<AnonymousPipeServerStream>();
        AnonymousPipeServerStream? standardInput = null;
        AnonymousPipeServerStream? standardOutput = null;
        AnonymousPipeServerStream? standardError = null;
        try
        {
            standardInput = CreatePipe(PipeDirection.Out, placeholders);
            standardOutput = CreatePipe(PipeDirection.In, placeholders);
            standardError = CreatePipe(PipeDirection.In, placeholders);
            (int pid, SpawnedProcessState state) = Spawn(
                path,
                workingDirectory,
                arguments,
                environment,
                standardInput.ClientSafePipeHandle,
                standardOutput.ClientSafePipeHandle,
                standardError.ClientSafePipeHandle);
            standardInput.DisposeLocalCopyOfClientHandle();
            standardOutput.DisposeLocalCopyOfClientHandle();
            standardError.DisposeLocalCopyOfClientHandle();

            // The stream shapes Process gives a redirected child, so readers behave the same.
            var process = new UnixGitProcess(
                pid,
                state,
                new StreamWriter(
                    standardInput,
                    startInfo.StandardInputEncoding ?? Encoding.Default,
                    StreamBufferSize)
                {
                    AutoFlush = true,
                },
                new StreamReader(
                    standardOutput,
                    startInfo.StandardOutputEncoding ?? Encoding.Default,
                    detectEncodingFromByteOrderMarks: true,
                    StreamBufferSize),
                new StreamReader(
                    standardError,
                    startInfo.StandardErrorEncoding ?? Encoding.Default,
                    detectEncodingFromByteOrderMarks: true,
                    StreamBufferSize));
            process.StartExitWatcher();
            return process;
        }
        catch
        {
            standardInput?.Dispose();
            standardOutput?.Dispose();
            standardError?.Dispose();
            throw;
        }
        finally
        {
            foreach (AnonymousPipeServerStream placeholder in placeholders)
            {
                placeholder.Dispose();
            }
        }
    }

    public override Task WaitForExitAsync() => _exited.Task;

    public override bool TryGetExitCode(out int exitCode)
    {
        lock (_sync)
        {
            if (!_exitObserved)
            {
                throw new InvalidOperationException("The process has not exited.");
            }

            ReapLocked();
            exitCode = _exitCode;
            return _exitStatusCollected;
        }
    }

    public override void Kill()
    {
        lock (_sync)
        {
            _killRequested = true;
            SendKillLocked();
        }
    }

    public override async Task WaitForGroupExitAsync()
    {
        await _exited.Task.ConfigureAwait(false);
        if (_ownsGroup && OperatingSystem.IsLinux())
        {
            bool? verified = null;
            await PollUntilAsync(() =>
            {
                lock (_sync)
                {
                    if (_released)
                    {
                        return true;
                    }

                    // The unreaped leader keeps the group id reserved, so every process reporting it
                    // was started by this command. A zombie member has already gone.
                    verified = LinuxProcessGroup.HasLiveMember(_pid);
                    if (verified == true)
                    {
                        if (_killRequested)
                        {
                            // Reach a member forked while an earlier signal was being delivered.
                            SendKillLocked();
                        }

                        return false;
                    }

                    ReapLocked();
                    return true;
                }
            }).ConfigureAwait(false);
            if (verified == false)
            {
                return;
            }
        }

        lock (_sync)
        {
            ReapLocked();
        }

        if (_ownsGroup)
        {
            // Without a process list the group can only be probed, and only once the leader is reaped:
            // until then the leader itself answers. Signal 0 cannot affect a process that reused the
            // id; such a reuse could only delay completion, never report a live member as gone.
            await PollUntilAsync(() => Native.kill(-_pid, 0) != 0
                                       && Marshal.GetLastPInvokeError() == ErrorNoProcess)
                .ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            if (_exitObserved)
            {
                ReapLocked();
            }
        }

        CloseStandardStreams();
    }

    private void SendKillLocked()
    {
        if (_released)
        {
            return;
        }

        if (_ownsGroup)
        {
            Native.kill(-_pid, SignalKill);
            return;
        }

        // The unreaped process still reserves its id, so the tree walk starts from this command.
        try
        {
            using Process process = Process.GetProcessById(_pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or Win32Exception
                                   or NotSupportedException
                                   or AggregateException)
        {
            Native.kill(_pid, SignalKill);
        }
    }

    private void StartExitWatcher()
    {
        var thread = new Thread(static state => ((UnixGitProcess)state!).WatchForExit())
        {
            IsBackground = true,
            Name = "Git process exit watcher",
        };
        thread.UnsafeStart(this);
    }

    private unsafe void WatchForExit()
    {
        byte* information = stackalloc byte[SignalInformationStorageSize];
        int error;
        do
        {
            // WNOWAIT leaves the process a zombie, which keeps its id and its group id reserved.
            error = Native.waitid(IdTypeProcess, (uint)_pid, information, WaitExited | WaitNoWait) == 0
                ? 0
                : Marshal.GetLastPInvokeError();
        }
        while (error == ErrorInterrupted);

        lock (_sync)
        {
            _exitObserved = true;
            if (error != 0)
            {
                // Reaped by something else, so the id may already be reused and the status is lost.
                MarkReapedElsewhere();
            }
            else if (_killRequested)
            {
                SendKillLocked();
            }

            if (_disposed)
            {
                ReapLocked();
            }
        }

        _exited.TrySetResult();
    }

    private unsafe void ReapLocked()
    {
        if (_released)
        {
            return;
        }

        int status = 0;
        int result;
        do
        {
            result = Native.waitpid(_pid, &status, WaitNoHang);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == ErrorInterrupted);

        if (result == _pid)
        {
            int terminationSignal = status & 0x7f;
            // Process reports a signal as 128 plus its number, as a shell does.
            _exitCode = terminationSignal == 0 ? (status >> 8) & 0xff : 128 + terminationSignal;
            _exitStatusCollected = true;
            _released = true;
        }
        else if (result < 0)
        {
            MarkReapedElsewhere();
        }
    }

    private void MarkReapedElsewhere()
    {
        _released = true;
        s_childrenReapedElsewhere = true;
    }

    private static AnonymousPipeServerStream CreatePipe(
        PipeDirection direction,
        List<AnonymousPipeServerStream> placeholders)
    {
        while (true)
        {
            // Both ends are close-on-exec. The child receives its end only through dup2, which clears
            // the flag on the copy.
            var pipe = new AnonymousPipeServerStream(direction, HandleInheritability.None);
            if (pipe.ClientSafePipeHandle.DangerousGetHandle() > 2)
            {
                return pipe;
            }

            // This process has a standard descriptor closed and the pipe reused it. Duplicating onto
            // 0, 1 and 2 in turn could then overwrite an end a later action still reads, so keep the
            // low descriptor occupied until the command has started and take the next pipe instead.
            placeholders.Add(pipe);
        }
    }

    private static unsafe (int Pid, SpawnedProcessState State) Spawn(
        string path,
        string? workingDirectory,
        string[] arguments,
        string[] environment,
        SafePipeHandle standardInput,
        SafePipeHandle standardOutput,
        SafePipeHandle standardError)
    {
        bool inputReferenced = false;
        bool outputReferenced = false;
        bool errorReferenced = false;
        byte* attributes = null;
        byte* actions = null;
        bool attributesInitialized = false;
        bool actionsInitialized = false;
        nint pathPointer = 0;
        nint workingDirectoryPointer = 0;
        byte** argumentPointers = null;
        byte** environmentPointers = null;
        try
        {
            standardInput.DangerousAddRef(ref inputReferenced);
            standardOutput.DangerousAddRef(ref outputReferenced);
            standardError.DangerousAddRef(ref errorReferenced);
            pathPointer = Marshal.StringToCoTaskMemUTF8(path);
            workingDirectoryPointer = workingDirectory is null
                ? 0
                : Marshal.StringToCoTaskMemUTF8(workingDirectory);
            argumentPointers = AllocateNullTerminatedArray(arguments);
            environmentPointers = AllocateNullTerminatedArray(environment);

            // posix_spawnattr_t and posix_spawn_file_actions_t are a structure in glibc and a pointer
            // in macOS; the storage is larger than either.
            attributes = (byte*)NativeMemory.AllocZeroed(OpaqueStorageSize);
            actions = (byte*)NativeMemory.AllocZeroed(OpaqueStorageSize);
            ThrowOnSpawnError(Native.posix_spawnattr_init(attributes), path, workingDirectory);
            attributesInitialized = true;
            int error = Native.posix_spawnattr_setflags(
                attributes,
                (short)(PosixSpawnSetSession | PosixSpawnSetSignalMask));
            if (error == ErrorInvalidArgument)
            {
                // A C library without POSIX_SPAWN_SETSID can still give the command a group.
                error = Native.posix_spawnattr_setflags(
                    attributes,
                    PosixSpawnSetProcessGroup | PosixSpawnSetSignalMask);
                if (error == 0)
                {
                    error = Native.posix_spawnattr_setpgroup(attributes, 0);
                }
            }

            ThrowOnSpawnError(error, path, workingDirectory);
            // Start with no signal blocked, whatever the calling thread blocks, as Process does. Zeroed
            // storage is the empty set in every supported C library.
            byte* signalSet = stackalloc byte[SignalSetStorageSize];
            new Span<byte>(signalSet, SignalSetStorageSize).Clear();
            ThrowOnSpawnError(
                Native.posix_spawnattr_setsigmask(attributes, signalSet),
                path,
                workingDirectory);

            ThrowOnSpawnError(Native.posix_spawn_file_actions_init(actions), path, workingDirectory);
            actionsInitialized = true;
            ThrowOnSpawnError(
                Native.posix_spawn_file_actions_adddup2(
                    actions,
                    (int)standardInput.DangerousGetHandle(),
                    0),
                path,
                workingDirectory);
            ThrowOnSpawnError(
                Native.posix_spawn_file_actions_adddup2(
                    actions,
                    (int)standardOutput.DangerousGetHandle(),
                    1),
                path,
                workingDirectory);
            ThrowOnSpawnError(
                Native.posix_spawn_file_actions_adddup2(
                    actions,
                    (int)standardError.DangerousGetHandle(),
                    2),
                path,
                workingDirectory);
            if (workingDirectoryPointer != 0)
            {
                ThrowOnSpawnError(
                    Native.posix_spawn_file_actions_addchdir_np(actions, (byte*)workingDirectoryPointer),
                    path,
                    workingDirectory);
            }

            int pid;
            ThrowOnSpawnError(
                Native.posix_spawn(
                    &pid,
                    (byte*)pathPointer,
                    actions,
                    attributes,
                    argumentPointers,
                    environmentPointers),
                path,
                workingDirectory);

            return (pid, GetSpawnedProcessState(pid));
        }
        finally
        {
            if (actionsInitialized)
            {
                Native.posix_spawn_file_actions_destroy(actions);
            }

            if (attributesInitialized)
            {
                Native.posix_spawnattr_destroy(attributes);
            }

            NativeMemory.Free(actions);
            NativeMemory.Free(attributes);
            FreeNullTerminatedArray(environmentPointers);
            FreeNullTerminatedArray(argumentPointers);
            Marshal.FreeCoTaskMem(workingDirectoryPointer);
            Marshal.FreeCoTaskMem(pathPointer);
            if (errorReferenced)
            {
                standardError.DangerousRelease();
            }

            if (outputReferenced)
            {
                standardOutput.DangerousRelease();
            }

            if (inputReferenced)
            {
                standardInput.DangerousRelease();
            }
        }
    }

    // Only a confirmed group is signalled as one. A C library that ignored the session flag would leave
    // the command in this process's group, which is never a target. A lookup that fails is confirmed
    // through the child's own state instead: some kernels do not report the group of a child that has
    // already exited, and a child reaped elsewhere no longer reserves its id at all.
    private static unsafe SpawnedProcessState GetSpawnedProcessState(int pid)
    {
        int processGroup = Native.getpgid(pid);
        if (processGroup == pid)
        {
            return SpawnedProcessState.LeadsGroup;
        }

        if (processGroup >= 0)
        {
            return SpawnedProcessState.InCallerGroup;
        }

        byte* information = stackalloc byte[SignalInformationStorageSize];
        int error;
        do
        {
            new Span<byte>(information, SignalInformationStorageSize).Clear();
            error = Native.waitid(
                IdTypeProcess,
                (uint)pid,
                information,
                WaitExited | WaitNoHang | WaitNoWait) == 0
                ? 0
                : Marshal.GetLastPInvokeError();
        }
        while (error == ErrorInterrupted);

        if (error != 0)
        {
            s_childrenReapedElsewhere = true;
            return SpawnedProcessState.ReapedElsewhere;
        }

        // si_signo leads siginfo_t on every supported platform and stays zero unless the child is
        // waitable, which here means an unreaped zombie that still reserves the group it was given.
        return Unsafe.ReadUnaligned<int>(information) != 0
            ? SpawnedProcessState.LeadsGroup
            : SpawnedProcessState.InCallerGroup;
    }

    private static void ThrowOnSpawnError(int error, string path, string? workingDirectory)
    {
        if (error != 0)
        {
            throw CreateStartException(error, path, workingDirectory);
        }
    }

    private static Win32Exception CreateStartException(int error, string path, string? workingDirectory)
        => new(
            error,
            $"An error occurred trying to start process '{path}' with working directory "
            + $"'{workingDirectory ?? Environment.CurrentDirectory}'. {Marshal.GetPInvokeErrorMessage(error)}");

    // A rooted name is used as given, as Process does. Any other name is searched for on this process's
    // PATH, which is also what Process and execvp search rather than the child's, skipping empty entries
    // as Process does. Unlike Process it never looks beside the application or in the current directory,
    // and the match is made absolute because the child would otherwise resolve it after its chdir.
    private static string ResolveExecutable(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (pathValue ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.GetFullPath(Path.Combine(directory, fileName));
            if (IsExecutableFile(candidate))
            {
                return candidate;
            }
        }

        throw CreateStartException(ErrorNoEntry, fileName, null);
    }

    private static bool IsExecutableFile(string path)
    {
        const UnixFileMode Executable = UnixFileMode.UserExecute
                                        | UnixFileMode.GroupExecute
                                        | UnixFileMode.OtherExecute;
        try
        {
            return File.Exists(path) && (File.GetUnixFileMode(path) & Executable) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static unsafe byte** AllocateNullTerminatedArray(string[] values)
    {
        var pointers = (byte**)NativeMemory.AllocZeroed((nuint)(values.Length + 1), (nuint)sizeof(byte*));
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Contains('\0', StringComparison.Ordinal))
            {
                FreeNullTerminatedArray(pointers);
                throw new ArgumentException("A process argument or environment entry contains a null character.");
            }

            pointers[i] = (byte*)Marshal.StringToCoTaskMemUTF8(values[i]);
        }

        return pointers;
    }

    private static unsafe void FreeNullTerminatedArray(byte** pointers)
    {
        if (pointers is null)
        {
            return;
        }

        for (byte** current = pointers; *current is not null; current++)
        {
            Marshal.FreeCoTaskMem((nint)(*current));
        }

        NativeMemory.Free(pointers);
    }

    private static bool ProbeSupport()
    {
        // The chdir action arrived in glibc 2.29 and macOS 10.15. Without it the command cannot be
        // started in its repository, so such a platform keeps Process.
        return (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
               && NativeLibrary.TryLoad("libc", typeof(UnixGitProcess).Assembly, null, out nint library)
               && NativeLibrary.TryGetExport(library, "posix_spawn_file_actions_addchdir_np", out _);
    }

    private static unsafe partial class Native
    {
        [LibraryImport("libc")]
        internal static partial int posix_spawnattr_init(byte* attributes);

        [LibraryImport("libc")]
        internal static partial int posix_spawnattr_destroy(byte* attributes);

        [LibraryImport("libc")]
        internal static partial int posix_spawnattr_setflags(byte* attributes, short flags);

        [LibraryImport("libc")]
        internal static partial int posix_spawnattr_setpgroup(byte* attributes, int processGroup);

        [LibraryImport("libc")]
        internal static partial int posix_spawnattr_setsigmask(byte* attributes, byte* signalSet);

        [LibraryImport("libc")]
        internal static partial int posix_spawn_file_actions_init(byte* actions);

        [LibraryImport("libc")]
        internal static partial int posix_spawn_file_actions_destroy(byte* actions);

        [LibraryImport("libc")]
        internal static partial int posix_spawn_file_actions_adddup2(
            byte* actions,
            int descriptor,
            int newDescriptor);

        [LibraryImport("libc")]
        internal static partial int posix_spawn_file_actions_addchdir_np(byte* actions, byte* path);

        [LibraryImport("libc")]
        internal static partial int posix_spawn(
            int* pid,
            byte* path,
            byte* actions,
            byte* attributes,
            byte** arguments,
            byte** environment);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int waitid(int idType, uint id, byte* information, int options);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int waitpid(int pid, int* status, int options);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int kill(int pid, int signal);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int getpgid(int pid);
    }

    private static class LinuxProcessGroup
    {
        private enum MemberState
        {
            Gone,
            Dead,
            Live,
        }

        // Null when /proc cannot be listed, so the caller probes the group id instead. Membership comes
        // from getpgid, which answers for any process in this namespace; only a member's state is read
        // from /proc, and a state that cannot be read counts as live. The group leader is expected to be
        // a zombie and is skipped either way.
        internal static bool? HasLiveMember(int processGroupId)
        {
            Span<byte> buffer = stackalloc byte[256];
            try
            {
                foreach (string directory in Directory.EnumerateDirectories("/proc"))
                {
                    if (int.TryParse(
                            Path.GetFileName(directory.AsSpan()),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int pid)
                        && pid != processGroupId
                        && Native.getpgid(pid) == processGroupId
                        && ReadMemberState(directory, buffer) == MemberState.Live)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            return false;
        }

        // /proc/<pid>/stat reads "pid (comm) state ...". The command name may contain spaces and
        // parentheses, but no field after it contains a parenthesis.
        private static MemberState ReadMemberState(string directory, Span<byte> buffer)
        {
            int count;
            try
            {
                using SafeFileHandle handle = File.OpenHandle(Path.Combine(directory, "stat"));
                count = RandomAccess.Read(handle, buffer, 0);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                                       || (ex is IOException && ex.HResult == ErrorNoProcess))
            {
                // Reaped since getpgid answered.
                return MemberState.Gone;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return MemberState.Live;
            }

            if (count == 0)
            {
                return MemberState.Gone;
            }

            ReadOnlySpan<byte> stat = buffer[..count];
            int nameEnd = stat.LastIndexOf((byte)')');
            if (nameEnd < 0 || nameEnd + 2 >= stat.Length)
            {
                return MemberState.Live;
            }

            return stat[nameEnd + 2] is (byte)'Z' or (byte)'X' or (byte)'x'
                ? MemberState.Dead
                : MemberState.Live;
        }
    }
}
