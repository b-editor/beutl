using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static Beutl.Editor.VersionControl.WindowsInterop;

namespace Beutl.Editor.VersionControl;

// Finds the live descendants of a Windows command that is not in a job. The handle a caller holds keeps
// the command's id reserved after it exits, so its children are still found by their parent id. The
// system's process list names every process, including one this process may not open, so a descendant
// that cannot be inspected is still counted.
[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage(Justification = WindowsInterop.CoverageJustification)]
internal static unsafe partial class WindowsProcessTree
{
    private const int SystemProcessInformationClass = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int InitialListingSize = 256 * 1024;
    private const int MaximumListingSize = 64 * 1024 * 1024;

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
            || !TryListProcesses(out List<ProcessTreeEntry> listing))
        {
            return null;
        }

        return ProcessTreeWalk.HasLiveDescendant(rootId, rootCreationTime, listing);
    }

    private static bool TryListProcesses(out List<ProcessTreeEntry> entries)
    {
        entries = [];
        int length = InitialListingSize;
        while (length <= MaximumListingSize)
        {
            byte* buffer = (byte*)NativeMemory.Alloc((nuint)length);
            try
            {
                uint required;
                int status = Native.NtQuerySystemInformation(
                    SystemProcessInformationClass,
                    buffer,
                    (uint)length,
                    &required);
                if (status == StatusInfoLengthMismatch)
                {
                    // Past the maximum the loop ends and the listing counts as unreadable.
                    length = (int)Math.Min(
                        MaximumListingSize + 1L,
                        Math.Max(length * 2L, required + (long)InitialListingSize));
                    continue;
                }

                // As for Process, a negative status is a failure and any other is a listing.
                return status >= 0
                       && SystemProcessListing.TryRead(
                           new ReadOnlySpan<byte>(buffer, (int)Math.Min(required, (uint)length)),
                           entries);
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        return false;
    }

    private static partial class Native
    {
        [LibraryImport("ntdll.dll")]
        internal static partial int NtQuerySystemInformation(
            int informationClass,
            byte* information,
            uint informationLength,
            uint* returnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetProcessTimes(
            SafeProcessHandle process,
            out long creationTime,
            out long exitTime,
            out long kernelTime,
            out long userTime);
    }
}

// Reads the entries of a SYSTEM_PROCESS_INFORMATION listing, which is taken at one moment, so every id,
// parent id and creation time in it agree. Kept apart from the call that fills it, so it is checked on
// every platform.
internal static class SystemProcessListing
{
    internal static bool TryRead(ReadOnlySpan<byte> listing, List<ProcessTreeEntry> entries)
    {
        int entrySize = Unsafe.SizeOf<SystemProcessInformation>();
        int offset = 0;
        while (listing.Length - offset >= entrySize)
        {
            SystemProcessInformation information =
                MemoryMarshal.Read<SystemProcessInformation>(listing[offset..]);
            entries.Add(new ProcessTreeEntry(
                (int)information.UniqueProcessId,
                (int)information.InheritedFromUniqueProcessId,
                information.CreateTime,
                // A process that has exited but is still referenced is listed without threads.
                IsAlive: information.NumberOfThreads > 0));
            if (information.NextEntryOffset == 0)
            {
                return true;
            }

            // The next entry starts past this one and inside the listing; anything else is not a listing.
            if (information.NextEntryOffset < (uint)entrySize
                || information.NextEntryOffset > (uint)(listing.Length - offset))
            {
                return false;
            }

            offset += (int)information.NextEntryOffset;
        }

        return false;
    }
}

// Structures for the Windows process and job calls. They are plain data, so their layout is checked on
// every platform.
internal static class WindowsInterop
{
    // The native Windows calls cannot run on the Linux machines that measure coverage. What can run
    // anywhere (the command line, the environment block, the listing and the tree search) is kept outside
    // the excluded types and tested on every platform.
    internal const string CoverageJustification =
        "Windows-only native process and job calls; coverage is measured on Linux.";

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

    // The leading fields of SYSTEM_PROCESS_INFORMATION, as the runtime reads them for Process.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessInformation
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UnicodeString ImageName;
        public int BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }
}

internal readonly record struct ProcessTreeEntry(int Id, int ParentId, long CreationTime, bool IsAlive);

// Searches one consistent process listing for a live descendant of a process. A listed process is a
// child of a listed parent only if it was created no earlier than that parent: a child of an older
// process that once held the parent's id was created before the parent took the id, so it is never
// taken for a descendant. An exited process that is still listed is searched below but not counted.
internal static class ProcessTreeWalk
{
    internal static bool HasLiveDescendant(
        int rootId,
        long rootCreationTime,
        IReadOnlyList<ProcessTreeEntry> listing)
    {
        var parents = new Queue<(int Id, long CreationTime)>();
        var seen = new HashSet<int> { rootId };
        parents.Enqueue((rootId, rootCreationTime));
        while (parents.TryDequeue(out (int Id, long CreationTime) parent))
        {
            foreach (ProcessTreeEntry entry in listing)
            {
                if (entry.ParentId != parent.Id
                    || entry.CreationTime < parent.CreationTime
                    || !seen.Add(entry.Id))
                {
                    continue;
                }

                if (entry.IsAlive)
                {
                    return true;
                }

                parents.Enqueue((entry.Id, entry.CreationTime));
            }
        }

        return false;
    }
}
