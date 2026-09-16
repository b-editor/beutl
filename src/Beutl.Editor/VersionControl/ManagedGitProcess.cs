using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor.VersionControl;

// System.Diagnostics.Process, in a job object on Windows. Without a job, Windows still owns the command's
// descendants through the process tree, which the handle Process holds keeps walkable after the command
// has exited. This is also the fallback for a Unix platform that cannot start a session at launch, where
// descendants are reached through the tree only while the command is alive.
internal sealed partial class ManagedGitProcess : GitProcess
{
    private readonly Process _process;
    private readonly SafeJobObjectHandle? _job;
    // A descendant created before this time may have started outside the job and is owned wherever it
    // is. One found outside the job that was created later left it on purpose.
    private readonly long _ownedSince;
    private volatile bool _killRequested;

    private ManagedGitProcess(Process process, SafeJobObjectHandle? job, long ownedSince)
    {
        _process = process;
        _job = job;
        _ownedSince = ownedSince;
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

        long ownedSince = 0;
        if (job is not null && !TryAdopt(job, process, out ownedSince))
        {
            job.Dispose();
            job = null;
        }

        return new ManagedGitProcess(process, job, ownedSince);
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
        KillCore();
    }

    public override async Task WaitForGroupExitAsync()
    {
        await WaitForExitAsync().ConfigureAwait(false);
        if (OperatingSystem.IsWindows())
        {
            await PollUntilAsync(_job is null ? IsProcessTreeGone : IsJobGroupGone).ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        CloseStandardStreams();
        _process.Dispose();
        _job?.Dispose();
    }

    private void KillCore()
    {
        if (_job is not null && OperatingSystem.IsWindows())
        {
            WindowsJobMethods.TerminateJobObject(_job, 1);
            WindowsProcessTree.TerminateEarlyDescendantsOutsideJob(_job, _process, _ownedSince);
            return;
        }

        try
        {
            // On Windows the walk works from an exited command too; on Unix its id may already be reused.
            if (OperatingSystem.IsWindows() || !_process.HasExited)
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
    }

    // Neither an empty job nor an empty tree is assumed: accounting or a process list that cannot be
    // read leaves the group unconfirmed.
    private unsafe bool IsJobGroupGone()
    {
        if (_job is null || !OperatingSystem.IsWindows())
        {
            return true;
        }

        JobObjectBasicAccountingInformation accounting = default;
        if (!WindowsJobMethods.QueryInformationJobObject(
                _job,
                WindowsJobMethods.JobObjectBasicAccountingInformationClass,
                &accounting,
                (uint)sizeof(JobObjectBasicAccountingInformation),
                0)
            || accounting.ActiveProcesses != 0)
        {
            return false;
        }

        // An early descendant that could not be moved into the job is invisible to its accounting.
        bool? hasEarlyDescendant = WindowsProcessTree.HasEarlyDescendantOutsideJob(_job, _process, _ownedSince);
        if (hasEarlyDescendant == true && _killRequested)
        {
            KillCore();
        }

        return hasEarlyDescendant == false;
    }

    // Without a job every live descendant is owned, and a process list that cannot be read leaves the
    // tree unconfirmed.
    private bool IsProcessTreeGone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        bool? hasDescendant = WindowsProcessTree.HasLiveDescendant(_process);
        if (hasDescendant == true && _killRequested)
        {
            KillCore();
        }

        return hasDescendant == false;
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

        // A descendant may still leave on purpose, as setsid lets it on Unix. A job that cannot allow
        // that is not used: starting a process with CREATE_BREAKAWAY_FROM_JOB would fail inside it.
        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = WindowsJobMethods.JobObjectLimitBreakawayOk,
            },
        };
        bool configured;
        unsafe
        {
            configured = WindowsJobMethods.SetInformationJobObject(
                job,
                WindowsJobMethods.JobObjectExtendedLimitInformationClass,
                &limits,
                (uint)sizeof(JobObjectExtendedLimitInformation));
        }

        if (!configured)
        {
            job.Dispose();
            return null;
        }

        return job;
    }

    // Process cannot start a suspended process, so the command is already running when it joins the
    // job. Whatever it started meanwhile is moved in too, so that their own children are born inside.
    // One that cannot be moved stays owned through the tree: Kill ends it and IsJobGroupGone waits for
    // it. A command that exited before it could join still leaves its descendants to adopt; a live
    // command that cannot join is owned through the tree instead, because its later children would
    // be born outside.
    private static bool TryAdopt(SafeJobObjectHandle job, Process process, out long ownedSince)
    {
        ownedSince = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (!WindowsJobMethods.AssignProcessToJobObject(job, process.SafeHandle)
                && !process.HasExited)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }

        ownedSince = WindowsProcessTree.AdoptEarlyDescendants(job, process);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
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
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicAccountingInformation
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessBasicInformation
    {
        public uint ExitStatus;
        public nint PebBaseAddress;
        public nuint AffinityMask;
        public int BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
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
    private static unsafe class WindowsProcessTree
    {
        private const uint SnapshotProcesses = 0x00000002;
        private const uint ProcessTerminate = 0x0001;
        private const uint ProcessSetQuota = 0x0100;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint Synchronize = 0x00100000;
        private const int ProcessBasicInformationClass = 0;
        private const uint WaitObject0 = 0;

        // Moves into the job every live descendant it does not hold, repeating while a pass moves any in
        // case one started another before it joined; the bound only stops a command that keeps
        // spawning faster than the passes. Returns the time from which the job owns the command. A
        // process that breaks away before then is moved back like the rest.
        internal static long AdoptEarlyDescendants(SafeJobObjectHandle job, Process root)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool adopted = false;
                bool listed = TryVisitLiveDescendants(root, long.MaxValue, handle =>
                {
                    if (IsOutsideJob(handle, job) && WindowsJobMethods.AssignProcessToJobObject(job, handle))
                    {
                        adopted = true;
                    }
                });
                if (!listed || !adopted)
                {
                    break;
                }
            }

            // Read after the passes, on the clock process creation times come from. A creation time
            // is never later than this for a process that already existed.
            WindowsJobMethods.GetSystemTimePreciseAsFileTime(out long now);
            return now;
        }

        internal static void TerminateEarlyDescendantsOutsideJob(
            SafeJobObjectHandle job,
            Process root,
            long ownedSince)
        {
            TryVisitLiveDescendants(root, ownedSince, handle =>
            {
                if (IsOutsideJob(handle, job))
                {
                    WindowsJobMethods.TerminateProcess(handle, 1);
                }
            });
        }

        // Null when the process list cannot be read.
        internal static bool? HasLiveDescendant(Process root)
        {
            bool found = false;
            return TryVisitLiveDescendants(root, long.MaxValue, _ => found = true) ? found : null;
        }

        // Null when the process list cannot be read.
        internal static bool? HasEarlyDescendantOutsideJob(
            SafeJobObjectHandle job,
            Process root,
            long ownedSince)
        {
            bool found = false;
            return TryVisitLiveDescendants(root, ownedSince, handle => found |= IsOutsideJob(handle, job))
                ? found
                : null;
        }

        // Membership that cannot be read counts as outside, so such a process is never taken as gone.
        private static bool IsOutsideJob(SafeProcessHandle handle, SafeJobObjectHandle job)
            => !WindowsJobMethods.IsProcessInJob(handle, job, out int inJob) || inJob == 0;

        private static bool TryVisitLiveDescendants(
            Process root,
            long createdNoLaterThan,
            Action<SafeProcessHandle> visit)
        {
            long rootCreationTime;
            try
            {
                // Process keeps the command's handle, so its creation time stays readable after it
                // has exited, and its id stays reserved.
                if (!WindowsJobMethods.GetProcessTimes(root.SafeHandle, out rootCreationTime, out _, out _, out _))
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return false;
            }

            if (!TryListProcesses(out List<ProcessTreeEntry> candidates))
            {
                return false;
            }

            ProcessTreeWalk.VisitLiveDescendants(
                root.Id,
                rootCreationTime,
                createdNoLaterThan,
                candidates,
                Open,
                Identify,
                HasExited,
                visit);
            return true;
        }

        private static bool TryListProcesses(out List<ProcessTreeEntry> entries)
        {
            entries = [];
            nint snapshot = WindowsJobMethods.CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == -1)
            {
                return false;
            }

            try
            {
                ProcessEntry entry = default;
                entry.Size = (uint)sizeof(ProcessEntry);
                if (!WindowsJobMethods.Process32FirstW(snapshot, &entry))
                {
                    return false;
                }

                do
                {
                    entries.Add(new ProcessTreeEntry((int)entry.ProcessId, (int)entry.ParentProcessId));
                    entry.Size = (uint)sizeof(ProcessEntry);
                }
                while (WindowsJobMethods.Process32NextW(snapshot, &entry));

                return true;
            }
            finally
            {
                WindowsJobMethods.CloseHandle(snapshot);
            }
        }

        // The rights to move and end a process are asked for first. A process that grants only the
        // right to be queried is still observed, so a descendant that cannot be ended keeps the group
        // unconfirmed.
        private static SafeProcessHandle? Open(int processId)
        {
            SafeProcessHandle handle = WindowsJobMethods.OpenProcess(
                ProcessQueryLimitedInformation | Synchronize | ProcessSetQuota | ProcessTerminate,
                false,
                (uint)processId);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                handle = WindowsJobMethods.OpenProcess(
                    ProcessQueryLimitedInformation | Synchronize,
                    false,
                    (uint)processId);
            }

            if (!handle.IsInvalid)
            {
                return handle;
            }

            handle.Dispose();
            return null;
        }

        private static ProcessTreeIdentity? Identify(SafeProcessHandle handle)
        {
            ProcessBasicInformation information = default;
            if (WindowsJobMethods.NtQueryInformationProcess(
                    handle,
                    ProcessBasicInformationClass,
                    &information,
                    (uint)sizeof(ProcessBasicInformation),
                    out _) != 0
                || !WindowsJobMethods.GetProcessTimes(handle, out long creationTime, out _, out _, out _))
            {
                return null;
            }

            return new ProcessTreeIdentity((int)information.InheritedFromUniqueProcessId, creationTime);
        }

        private static bool HasExited(SafeProcessHandle handle)
            => WindowsJobMethods.WaitForSingleObject(handle, 0) == WaitObject0;
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
        internal static partial bool IsProcessInJob(
            SafeProcessHandle process,
            SafeJobObjectHandle job,
            out int result);

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
        internal static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool Process32FirstW(nint snapshot, ProcessEntry* entry);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool Process32NextW(nint snapshot, ProcessEntry* entry);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetProcessTimes(
            SafeProcessHandle process,
            out long creationTime,
            out long exitTime,
            out long kernelTime,
            out long userTime);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);

        [LibraryImport("kernel32.dll")]
        internal static partial void GetSystemTimePreciseAsFileTime(out long systemTime);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationProcess(
            SafeProcessHandle process,
            int informationClass,
            ProcessBasicInformation* information,
            uint informationLength,
            out uint returnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(nint handle);
    }
}

internal readonly record struct ProcessTreeEntry(int Id, int ParentId);

internal readonly record struct ProcessTreeIdentity(int ParentId, long CreationTime);

// Finds the live descendants of a process from a listing of (id, parent id) pairs, which can be stale by
// the time a process is opened. A candidate counts only if the process behind the opened handle names the
// matched parent as its own and was created no earlier than that parent, so an id reused since the
// listing, or a child of an older process that once held the parent's id, is never taken for a
// descendant. A matched parent's handle stays open until the walk ends, so its id cannot be reused
// meanwhile. An exited process is not visited, but its children are still searched. A process created
// after the cutoff is left out together with everything below it, which can only be newer still.
internal static class ProcessTreeWalk
{
    internal static void VisitLiveDescendants<THandle>(
        int rootId,
        long rootCreationTime,
        long createdNoLaterThan,
        IReadOnlyList<ProcessTreeEntry> candidates,
        Func<int, THandle?> open,
        Func<THandle, ProcessTreeIdentity?> identify,
        Func<THandle, bool> hasExited,
        Action<THandle> visit)
        where THandle : class, IDisposable
    {
        var parents = new Queue<(int Id, long CreationTime)>();
        var matched = new List<THandle>();
        var seen = new HashSet<int> { rootId };
        parents.Enqueue((rootId, rootCreationTime));
        try
        {
            while (parents.TryDequeue(out (int Id, long CreationTime) parent))
            {
                foreach (ProcessTreeEntry candidate in candidates)
                {
                    if (candidate.ParentId != parent.Id || seen.Contains(candidate.Id))
                    {
                        continue;
                    }

                    THandle? handle = open(candidate.Id);
                    if (handle is null)
                    {
                        continue;
                    }

                    if (identify(handle) is not { } identity
                        || identity.ParentId != parent.Id
                        || identity.CreationTime < parent.CreationTime
                        || identity.CreationTime > createdNoLaterThan)
                    {
                        handle.Dispose();
                        continue;
                    }

                    matched.Add(handle);
                    seen.Add(candidate.Id);
                    parents.Enqueue((candidate.Id, identity.CreationTime));
                    if (!hasExited(handle))
                    {
                        visit(handle);
                    }
                }
            }
        }
        finally
        {
            foreach (THandle handle in matched)
            {
                handle.Dispose();
            }
        }
    }
}
