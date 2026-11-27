using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Agents;

internal static partial class AgentWorkerProcess
{
    // Providers inherit the worker's group on Unix and its job on Windows. This lets
    // the desktop reap descendants even when the worker itself has already exited.
    internal static void IsolateOnUnix()
    {
        if (!OperatingSystem.IsWindows() && SetProcessGroup(0, 0) != 0 && GetProcessGroup() != Environment.ProcessId)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not isolate the agent worker process group.");
    }

    internal static SafeFileHandle CreateJob(Process process)
    {
        const uint killOnJobClose = 0x2000;
        const int extendedLimitInformation = 9;
        var job = CreateJobObject(0, 0);
        if (!job.IsInvalid)
        {
            var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = killOnJobClose } };
            if (SetInformationJobObject(job, extendedLimitInformation, in limits, (uint)Marshal.SizeOf<JobLimits>())
                && AssignProcessToJobObject(job, process.SafeHandle))
                return job;
        }

        int error = Marshal.GetLastPInvokeError();
        job.Dispose();
        throw new Win32Exception(error, "Could not isolate the agent worker in a Windows job.");
    }

    internal static void KillUnixGroup(int processId)
    {
        const int sigkill = 9;
        if (!OperatingSystem.IsWindows())
            Kill(-processId, sigkill);
    }

    [LibraryImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static partial int SetProcessGroup(int process, int group);

    [LibraryImport("libc", EntryPoint = "getpgrp")]
    private static partial int GetProcessGroup();

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int processOrGroup, int signal);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial SafeFileHandle CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int infoClass, in JobLimits info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
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
    private struct JobLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations;
        public ulong WriteOperations;
        public ulong OtherOperations;
        public ulong ReadBytes;
        public ulong WriteBytes;
        public ulong OtherBytes;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUse;
        public nuint PeakJobMemoryUse;
    }
}
