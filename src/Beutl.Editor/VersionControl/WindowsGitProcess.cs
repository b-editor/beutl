using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using static Beutl.Editor.VersionControl.WindowsInterop;

namespace Beutl.Editor.VersionControl;

// CreateProcess with CREATE_SUSPENDED, so the command joins its job object before its first instruction
// runs and every process it starts is born inside the job. Process has no way to ask for a suspended
// start. A descendant that leaves the job on purpose (CREATE_BREAKAWAY_FROM_JOB) is not owned.
//
// A command that cannot join a job is still owned through the process tree: the handle kept here holds
// its id reserved after it exits, so its children are still found by their parent id.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGitProcess : GitProcess
{
    private const int StreamBufferSize = 4096;
    private const int CreateSuspended = 0x00000004;
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int CreateNoWindow = 0x08000000;
    private const int StartUseStandardHandles = 0x00000100;
    private const int DuplicateSameAccess = 2;
    private const int JobObjectBasicAccountingInformationClass = 1;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitBreakawayOk = 0x00000800;

    private static readonly Encoding s_utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly ILogger s_logger = Log.CreateLogger<WindowsGitProcess>();

    // Process.Start creates its inheritable pipe ends and its process under this lock, so that one start
    // does not hand another's pipes to its child. Without it the suspended start is not made at all: a
    // start the runtime made meanwhile could inherit this command's pipe ends and hold them open.
    private static readonly object? s_createProcessLock =
        typeof(Process).GetField("s_createProcessLock", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);

    private readonly Process _process;
    private readonly SafeProcessHandle _handle;
    private readonly SafeJobObjectHandle? _job;
    private readonly StreamWriter _standardInput;
    private readonly StreamReader _standardOutput;
    private readonly StreamReader _standardError;
    private volatile bool _killRequested;

    private WindowsGitProcess(
        Process process,
        SafeProcessHandle handle,
        SafeJobObjectHandle? job,
        StreamWriter standardInput,
        StreamReader standardOutput,
        StreamReader standardError)
    {
        _process = process;
        _handle = handle;
        _job = job;
        _standardInput = standardInput;
        _standardOutput = standardOutput;
        _standardError = standardError;
    }

    public override int Id => _process.Id;

    public override StreamWriter StandardInput => _standardInput;

    public override StreamReader StandardOutput => _standardOutput;

    public override StreamReader StandardError => _standardError;

    public override bool OwnsDescendants => _job is not null;

    // Null when the suspended start could not be made. Nothing is left running then, so the caller can
    // start the command with Process instead, which also reports a start that cannot succeed at all in
    // its usual way.
    internal static WindowsGitProcess? TryStart(ProcessStartInfo startInfo)
    {
        if (s_createProcessLock is not { } createProcessLock)
        {
            return null;
        }

        char[] commandLine = WindowsProcessArguments.BuildCommandLine(startInfo);
        char[] environment = WindowsProcessArguments.BuildEnvironmentBlock(startInfo.Environment);
        string? workingDirectory = string.IsNullOrEmpty(startInfo.WorkingDirectory)
            ? null
            : startInfo.WorkingDirectory;

        SafeFileHandle? parentInput = null;
        SafeFileHandle? parentOutput = null;
        SafeFileHandle? parentError = null;
        SafeProcessHandle? processHandle = null;
        nint threadHandle = 0;
        Process? process = null;
        SafeJobObjectHandle? job = null;
        StreamWriter? standardInput = null;
        StreamReader? standardOutput = null;
        StreamReader? standardError = null;
        bool resumed = false;
        try
        {
            if (!TryCreateSuspended(
                    createProcessLock,
                    commandLine,
                    environment,
                    workingDirectory,
                    out parentInput,
                    out parentOutput,
                    out parentError,
                    out ProcessInformation information))
            {
                return null;
            }

            processHandle = new SafeProcessHandle(information.Process, ownsHandle: true);
            threadHandle = information.Thread;
            process = Process.GetProcessById(information.ProcessId);

            job = TryCreateJob();
            if (job is not null && !Native.AssignProcessToJobObject(job, processHandle))
            {
                job.Dispose();
                job = null;
            }

            // Everything that can fail is done before the command runs, so a failure never leaves a
            // command that has already done work to be started a second time.
            standardInput = new StreamWriter(
                new FileStream(parentInput, FileAccess.Write, StreamBufferSize, isAsync: false),
                startInfo.StandardInputEncoding ?? s_utf8,
                StreamBufferSize)
            {
                AutoFlush = true,
            };
            parentInput = null;
            standardOutput = new StreamReader(
                new FileStream(parentOutput, FileAccess.Read, StreamBufferSize, isAsync: false),
                startInfo.StandardOutputEncoding ?? s_utf8,
                detectEncodingFromByteOrderMarks: true,
                StreamBufferSize);
            parentOutput = null;
            standardError = new StreamReader(
                new FileStream(parentError, FileAccess.Read, StreamBufferSize, isAsync: false),
                startInfo.StandardErrorEncoding ?? s_utf8,
                detectEncodingFromByteOrderMarks: true,
                StreamBufferSize);
            parentError = null;

            if (Native.ResumeThread(threadHandle) == -1)
            {
                return null;
            }

            resumed = true;
            return new WindowsGitProcess(
                process,
                processHandle,
                job,
                standardInput,
                standardOutput,
                standardError);
        }
        catch (Exception ex) when (ex is Win32Exception
                                   or InvalidOperationException
                                   or ArgumentException
                                   or IOException
                                   or NotSupportedException)
        {
            return null;
        }
        finally
        {
            if (threadHandle != 0)
            {
                Native.CloseHandle(threadHandle);
            }

            if (!resumed)
            {
                // A command that was never resumed has run none of its own code, so ending it undoes
                // nothing.
                if (processHandle is not null)
                {
                    Native.TerminateProcess(processHandle, 1);
                    processHandle.Dispose();
                }

                process?.Dispose();
                job?.Dispose();
                standardInput?.Dispose();
                standardOutput?.Dispose();
                standardError?.Dispose();
                parentInput?.Dispose();
                parentOutput?.Dispose();
                parentError?.Dispose();
            }
        }
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
        await PollUntilAsync(_job is null ? IsTreeGone : IsJobEmpty).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        CloseStandardStreams();
        _process.Dispose();
        _handle.Dispose();
        _job?.Dispose();
    }

    private void KillCore()
    {
        if (_job is not null)
        {
            if (Native.TerminateJobObject(_job, 1))
            {
                return;
            }

            // No failure of this call is known to be transient, so it is not repeated. The job's
            // members are the command's descendants, reachable through the tree while their parents
            // live; any that survive both keep cleanup unconfirmed until they exit.
            int error = Marshal.GetLastPInvokeError();
            s_logger.LogWarning(
                "The job of Git process {ProcessId} could not be terminated ({Error}: {Message}); ending its process tree instead.",
                _process.Id,
                error,
                Marshal.GetPInvokeErrorMessage(error));
        }

        ManagedGitProcess.KillTree(_process);
    }

    // Accounting that cannot be read is not taken as an empty job.
    private unsafe bool IsJobEmpty()
    {
        JobObjectBasicAccountingInformation accounting = default;
        return _job is not null
               && Native.QueryInformationJobObject(
                   _job,
                   JobObjectBasicAccountingInformationClass,
                   &accounting,
                   (uint)sizeof(JobObjectBasicAccountingInformation),
                   0)
               && accounting.ActiveProcesses == 0;
    }

    private bool IsTreeGone()
        => WindowsProcessTree.IsTreeGone(_process, _handle, _killRequested);

    private static unsafe bool TryCreateSuspended(
        object createProcessLock,
        char[] commandLine,
        char[] environment,
        string? workingDirectory,
        [NotNullWhen(true)] out SafeFileHandle? parentInput,
        [NotNullWhen(true)] out SafeFileHandle? parentOutput,
        [NotNullWhen(true)] out SafeFileHandle? parentError,
        out ProcessInformation information)
    {
        parentInput = null;
        parentOutput = null;
        parentError = null;
        information = default;
        SafeFileHandle? childInput = null;
        SafeFileHandle? childOutput = null;
        SafeFileHandle? childError = null;
        bool created = false;
        lock (createProcessLock)
        {
            try
            {
                CreatePipe(out parentInput, out childInput, parentInputs: true);
                CreatePipe(out parentOutput, out childOutput, parentInputs: false);
                CreatePipe(out parentError, out childError, parentInputs: false);
                var startup = new StartupInfo
                {
                    Size = sizeof(StartupInfo),
                    Flags = StartUseStandardHandles,
                    StandardInput = childInput.DangerousGetHandle(),
                    StandardOutput = childOutput.DangerousGetHandle(),
                    StandardError = childError.DangerousGetHandle(),
                };
                ProcessInformation result = default;
                fixed (char* commandLinePointer = commandLine)
                fixed (char* environmentPointer = environment)
                {
                    created = Native.CreateProcessW(
                        null,
                        commandLinePointer,
                        0,
                        0,
                        true,
                        CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment,
                        environmentPointer,
                        workingDirectory,
                        &startup,
                        &result);
                }

                information = result;
                return created;
            }
            catch (Win32Exception)
            {
                return false;
            }
            finally
            {
                childInput?.Dispose();
                childOutput?.Dispose();
                childError?.Dispose();
                if (!created)
                {
                    parentInput?.Dispose();
                    parentOutput?.Dispose();
                    parentError?.Dispose();
                    parentInput = null;
                    parentOutput = null;
                    parentError = null;
                }
            }
        }
    }

    // As Process does: an inheritable pipe whose end for this process is replaced by a duplicate that
    // the command does not inherit.
    private static void CreatePipe(
        out SafeFileHandle parentHandle,
        out SafeFileHandle childHandle,
        bool parentInputs)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = 1,
        };
        SafeFileHandle inheritableParent;
        bool created = parentInputs
            ? Native.CreatePipe(out childHandle, out inheritableParent, ref attributes, 0)
            : Native.CreatePipe(out inheritableParent, out childHandle, ref attributes, 0);
        using (inheritableParent)
        {
            if (!created || childHandle.IsInvalid || inheritableParent.IsInvalid)
            {
                childHandle.Dispose();
                throw new Win32Exception();
            }

            nint currentProcess = Native.GetCurrentProcess();
            if (!Native.DuplicateHandle(
                    currentProcess,
                    inheritableParent,
                    currentProcess,
                    out parentHandle,
                    0,
                    false,
                    DuplicateSameAccess))
            {
                childHandle.Dispose();
                parentHandle.Dispose();
                throw new Win32Exception();
            }
        }
    }

    private static unsafe SafeJobObjectHandle? TryCreateJob()
    {
        SafeJobObjectHandle job = Native.CreateJobObjectW(0, null);
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
                LimitFlags = JobObjectLimitBreakawayOk,
            },
        };
        if (!Native.SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformationClass,
                &limits,
                (uint)sizeof(JobObjectExtendedLimitInformation)))
        {
            job.Dispose();
            return null;
        }

        return job;
    }

    private sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobObjectHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => Native.CloseHandle(handle);
    }

    private static unsafe partial class Native
    {
        [LibraryImport(
            "kernel32.dll",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CreateProcessW(
            string? applicationName,
            char* commandLine,
            nint processAttributes,
            nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            int creationFlags,
            char* environment,
            string? currentDirectory,
            StartupInfo* startupInfo,
            ProcessInformation* processInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CreatePipe(
            out SafeFileHandle readPipe,
            out SafeFileHandle writePipe,
            ref SecurityAttributes pipeAttributes,
            int size);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DuplicateHandle(
            nint sourceProcess,
            SafeFileHandle sourceHandle,
            nint targetProcess,
            out SafeFileHandle targetHandle,
            int desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int options);

        [LibraryImport("kernel32.dll")]
        internal static partial nint GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial int ResumeThread(nint thread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateProcess(SafeProcessHandle process, int exitCode);

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

// The command line and environment block CreateProcess receives, built exactly as Process builds them
// so that the command sees the same arguments and variables either way.
internal static class WindowsProcessArguments
{
    // The file name is quoted unless it already is, and each argument follows the rules the C runtime
    // uses to split a command line.
    internal static char[] BuildCommandLine(ProcessStartInfo startInfo)
    {
        var builder = new StringBuilder();
        ReadOnlySpan<char> fileName = startInfo.FileName.AsSpan().Trim();
        bool quoted = fileName.StartsWith('"') && fileName.EndsWith('"');
        if (!quoted)
        {
            builder.Append('"');
        }

        builder.Append(fileName);
        if (!quoted)
        {
            builder.Append('"');
        }

        if (startInfo.ArgumentList.Count > 0)
        {
            foreach (string argument in startInfo.ArgumentList)
            {
                AppendArgument(builder, argument);
            }
        }
        else if (!string.IsNullOrEmpty(startInfo.Arguments))
        {
            builder.Append(' ').Append(startInfo.Arguments);
        }

        // CreateProcess may write to the buffer, so it is a terminated array rather than a string.
        return [.. builder.ToString(), '\0'];
    }

    // Sorted by name without regard to case, as Windows expects, and ended by an empty entry.
    internal static char[] BuildEnvironmentBlock(IDictionary<string, string?> environment)
    {
        string[] names = [.. environment.Keys];
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder(8 * names.Length);
        foreach (string name in names)
        {
            if (environment[name] is { } value)
            {
                builder.Append(name).Append('=').Append(value).Append('\0');
            }
        }

        return [.. builder.ToString(), '\0', '\0'];
    }

    private static void AppendArgument(StringBuilder builder, string argument)
    {
        builder.Append(' ');
        if (argument.Length != 0 && !argument.Any(static c => char.IsWhiteSpace(c) || c == '"'))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        int index = 0;
        while (index < argument.Length)
        {
            char c = argument[index++];
            if (c == '\\')
            {
                int backslashes = 1;
                while (index < argument.Length && argument[index] == '\\')
                {
                    index++;
                    backslashes++;
                }

                if (index == argument.Length)
                {
                    // The closing quote follows, so every backslash is doubled.
                    builder.Append('\\', backslashes * 2);
                }
                else if (argument[index] == '"')
                {
                    builder.Append('\\', (backslashes * 2) + 1).Append('"');
                    index++;
                }
                else
                {
                    builder.Append('\\', backslashes);
                }
            }
            else if (c == '"')
            {
                builder.Append('\\').Append('"');
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
    }
}
