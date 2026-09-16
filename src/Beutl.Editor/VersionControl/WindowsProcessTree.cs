using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static Beutl.Editor.VersionControl.WindowsInterop;

namespace Beutl.Editor.VersionControl;

// Finds the live descendants of a Windows command that is not in a job. The handle a caller holds keeps
// the command's id reserved after it exits, so its children are still found by their parent id.
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const int ProcessBasicInformationClass = 0;
    private const uint WaitObject0 = 0;

    // True once no descendant of the root is alive. A process list that cannot be read leaves the tree
    // unconfirmed, and a descendant found alive after a kill is ended again.
    internal static bool IsTreeGone(Process root, SafeProcessHandle rootHandle, bool killRequested)
    {
        bool? hasDescendant = HasLiveDescendant(root.Id, rootHandle);
        if (hasDescendant == true && killRequested)
        {
            ManagedGitProcess.KillTree(root);
        }

        return hasDescendant == false;
    }

    // Null when the process list cannot be read.
    private static bool? HasLiveDescendant(int rootId, SafeProcessHandle rootHandle)
    {
        if (!Native.GetProcessTimes(rootHandle, out long rootCreationTime, out _, out _, out _)
            || !TryListProcesses(out List<ProcessTreeEntry> candidates))
        {
            return null;
        }

        bool found = false;
        ProcessTreeWalk.VisitLiveDescendants<SafeProcessHandle>(
            rootId,
            rootCreationTime,
            candidates,
            Open,
            Identify,
            HasExited,
            _ => found = true);
        return found;
    }

    private static bool TryListProcesses(out List<ProcessTreeEntry> entries)
    {
        entries = [];
        nint snapshot = Native.CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == -1)
        {
            return false;
        }

        try
        {
            ProcessEntry entry = default;
            entry.Size = (uint)sizeof(ProcessEntry);
            if (!Native.Process32FirstW(snapshot, &entry))
            {
                return false;
            }

            do
            {
                entries.Add(new ProcessTreeEntry((int)entry.ProcessId, (int)entry.ParentProcessId));
                entry.Size = (uint)sizeof(ProcessEntry);
            }
            while (Native.Process32NextW(snapshot, &entry));

            return true;
        }
        finally
        {
            Native.CloseHandle(snapshot);
        }
    }

    private static SafeProcessHandle? Open(int processId)
    {
        SafeProcessHandle handle = Native.OpenProcess(
            ProcessQueryLimitedInformation | Synchronize,
            false,
            (uint)processId);
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
        if (Native.NtQueryInformationProcess(
                handle,
                ProcessBasicInformationClass,
                &information,
                (uint)sizeof(ProcessBasicInformation),
                out _) != 0
            || !Native.GetProcessTimes(handle, out long creationTime, out _, out _, out _))
        {
            return null;
        }

        return new ProcessTreeIdentity((int)information.InheritedFromUniqueProcessId, creationTime);
    }

    private static bool HasExited(SafeProcessHandle handle)
        => Native.WaitForSingleObject(handle, 0) == WaitObject0;

    private static partial class Native
    {
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

// Structures for the Windows process and job calls. They are plain data, so their layout is checked on
// every platform.
internal static class WindowsInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        public int Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
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
}

internal readonly record struct ProcessTreeEntry(int Id, int ParentId);

internal readonly record struct ProcessTreeIdentity(int ParentId, long CreationTime);

// Finds the live descendants of a process from a listing of (id, parent id) pairs, which can be stale by
// the time a process is opened. A candidate counts only if the process behind the opened handle names the
// matched parent as its own and was created no earlier than that parent, so an id reused since the
// listing, or a child of an older process that once held the parent's id, is never taken for a
// descendant. A matched parent's handle stays open until the walk ends, so its id cannot be reused
// meanwhile. An exited process is not visited, but its children are still searched.
internal static class ProcessTreeWalk
{
    internal static void VisitLiveDescendants<THandle>(
        int rootId,
        long rootCreationTime,
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
                        || identity.CreationTime < parent.CreationTime)
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
