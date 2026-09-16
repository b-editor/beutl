using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

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

// System.Diagnostics.Process, in a job object on Windows. Also the fallback for a Unix platform that
// cannot start a session at launch, where descendants are reached only through the process tree.
internal sealed partial class ManagedGitProcess : GitProcess
{
    private readonly Process _process;
    private readonly SafeJobObjectHandle? _job;

    private ManagedGitProcess(Process process, SafeJobObjectHandle? job)
    {
        _process = process;
        _job = job;
    }

    public override int Id => _process.Id;

    public override StreamWriter StandardInput => _process.StandardInput;

    public override StreamReader StandardOutput => _process.StandardOutput;

    public override StreamReader StandardError => _process.StandardError;

    public override bool OwnsDescendants => _job is not null;

    public static new ManagedGitProcess Start(ProcessStartInfo startInfo)
    {
        SafeJobObjectHandle? job = TryCreateJob();
        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch
        {
            job?.Dispose();
            process.Dispose();
            throw;
        }

        // Process cannot start a suspended process, so the command is already running here. A
        // descendant it starts before this assignment is outside the job; Kill still reaches it through
        // the process tree while the command is alive.
        if (job is not null && !TryAssign(job, process))
        {
            job.Dispose();
            job = null;
        }

        return new ManagedGitProcess(process, job);
    }

    public override Task WaitForExitAsync() => _process.WaitForExitAsync(CancellationToken.None);

    public override bool TryGetExitCode(out int exitCode)
    {
        exitCode = _process.ExitCode;
        return true;
    }

    public override void Kill()
    {
        // The tree walk runs first, while the command is still alive to be walked from: it is what
        // reaches a descendant started before the job assignment.
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or Win32Exception
                                   or NotSupportedException
                                   or AggregateException)
        {
        }

        if (_job is not null && OperatingSystem.IsWindows())
        {
            WindowsJobMethods.TerminateJobObject(_job, 1);
        }
    }

    public override async Task WaitForGroupExitAsync()
    {
        await WaitForExitAsync().ConfigureAwait(false);
        await PollUntilAsync(HasNoActiveJobProcess).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        CloseStandardStreams();
        _process.Dispose();
        _job?.Dispose();
    }

    private static SafeJobObjectHandle? TryCreateJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        SafeJobObjectHandle job = WindowsJobMethods.CreateJobObjectW(0, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            return null;
        }

        // A descendant may still leave on purpose, as setsid lets it on Unix. Without this, starting
        // a process with CREATE_BREAKAWAY_FROM_JOB would fail inside the command.
        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = WindowsJobMethods.JobObjectLimitBreakawayOk,
            },
        };
        unsafe
        {
            WindowsJobMethods.SetInformationJobObject(
                job,
                WindowsJobMethods.JobObjectExtendedLimitInformationClass,
                &limits,
                (uint)sizeof(JobObjectExtendedLimitInformation));
        }

        return job;
    }

    private static bool TryAssign(SafeJobObjectHandle job, Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return WindowsJobMethods.AssignProcessToJobObject(job, process.SafeHandle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    // A job whose accounting cannot be read counts as empty once the command has exited, which is all
    // a process-tree kill could confirm.
    private unsafe bool HasNoActiveJobProcess()
    {
        if (_job is null || !OperatingSystem.IsWindows())
        {
            return true;
        }

        JobObjectBasicAccountingInformation accounting = default;
        return !WindowsJobMethods.QueryInformationJobObject(
                   _job,
                   WindowsJobMethods.JobObjectBasicAccountingInformationClass,
                   &accounting,
                   (uint)sizeof(JobObjectBasicAccountingInformation),
                   0)
               || accounting.ActiveProcesses == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    private sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobObjectHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
            => OperatingSystem.IsWindows() && WindowsJobMethods.CloseHandle(handle);
    }

    [SupportedOSPlatform("windows")]
    private static unsafe partial class WindowsJobMethods
    {
        internal const int JobObjectBasicAccountingInformationClass = 1;
        internal const int JobObjectExtendedLimitInformationClass = 9;
        internal const uint JobObjectLimitBreakawayOk = 0x00000800;

        [LibraryImport(
            "kernel32.dll",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeJobObjectHandle CreateJobObjectW(nint jobAttributes, string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            SafeJobObjectHandle job,
            int informationClass,
            void* information,
            uint informationLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(
            SafeJobObjectHandle job,
            SafeProcessHandle process);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateJobObject(SafeJobObjectHandle job, uint exitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryInformationJobObject(
            SafeJobObjectHandle job,
            int informationClass,
            void* information,
            uint informationLength,
            nint returnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(nint handle);
    }
}

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
        bool ownsGroup,
        StreamWriter standardInput,
        StreamReader standardOutput,
        StreamReader standardError)
    {
        _pid = pid;
        _ownsGroup = ownsGroup;
        _standardInput = standardInput;
        _standardOutput = standardOutput;
        _standardError = standardError;
    }

    internal static bool IsSupported => s_isSupported;

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
            (int pid, bool ownsGroup) = Spawn(
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
                ownsGroup,
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
                // Reaped by something else, such as a runtime that started with SIGCHLD ignored and
                // therefore reaps every child. The id may already be reused, and the status is lost.
                _released = true;
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
            _released = true;
        }
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

    private static unsafe (int Pid, bool OwnsGroup) Spawn(
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

            // A C library that ignored the session flag would leave the command in this process's
            // group. Signal only the command then: the group of this process is never a target.
            int processGroup = Native.getpgid(pid);
            return (pid, processGroup < 0 || processGroup == pid);
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

    // A rooted name is used as given, as Process does. Any other name is searched for on PATH only,
    // never beside the application or in the current directory, and the match is made absolute
    // because the child would otherwise resolve it after its chdir.
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
        // Null when /proc cannot be listed, so the caller probes the group id instead. The group
        // leader is expected to be a zombie and is skipped either way.
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
                        && TryReadGroupAndState(directory, buffer, out int group, out byte state)
                        && group == processGroupId
                        && state is not ((byte)'Z' or (byte)'X' or (byte)'x'))
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

        // /proc/<pid>/stat reads "pid (comm) state ppid pgrp ...". The command name may contain spaces
        // and parentheses, but no field after it contains a parenthesis.
        private static bool TryReadGroupAndState(
            string directory,
            Span<byte> buffer,
            out int group,
            out byte state)
        {
            group = 0;
            state = 0;
            int count;
            try
            {
                using SafeFileHandle handle = File.OpenHandle(Path.Combine(directory, "stat"));
                count = RandomAccess.Read(handle, buffer, 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The process exited while /proc was being listed.
                return false;
            }

            ReadOnlySpan<byte> fields = buffer[..count];
            int nameEnd = fields.LastIndexOf((byte)')');
            if (nameEnd < 0 || nameEnd + 2 >= fields.Length)
            {
                return false;
            }

            fields = fields[(nameEnd + 2)..];
            state = fields[0];
            // Skip the state and the parent id.
            for (int skipped = 0; skipped < 2; skipped++)
            {
                int separator = fields.IndexOf((byte)' ');
                if (separator < 0)
                {
                    return false;
                }

                fields = fields[(separator + 1)..];
            }

            int end = fields.IndexOf((byte)' ');
            return end > 0
                   && int.TryParse(
                       fields[..end],
                       NumberStyles.AllowLeadingSign,
                       CultureInfo.InvariantCulture,
                       out group);
        }
    }
}
