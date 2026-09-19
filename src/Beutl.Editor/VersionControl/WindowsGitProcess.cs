using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
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
// A command runs only inside its job. One that cannot join is ended before it runs, having run none of
// its own code, and started again outside the job this process is in, where that job lets it leave. A
// command that still cannot join is not run at all, and its start fails.
[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage(Justification = CoverageJustification)]
internal sealed partial class WindowsGitProcess : GitProcess
{
    private const int StreamBufferSize = 4096;
    private const int CreateSuspended = 0x00000004;
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int CreateBreakawayFromJob = 0x01000000;
    private const int CreateNoWindow = 0x08000000;
    private const int StartUseStandardHandles = 0x00000100;
    private const int DuplicateSameAccess = 2;
    private const int ErrorBadExecutableFormat = 193;
    private const int ErrorExecutableMachineTypeMismatch = 216;
    private const int JobObjectBasicAccountingInformationClass = 1;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitBreakawayOk = 0x00000800;
    private const uint WaitObject0 = 0;

    private static readonly Encoding s_utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly ILogger s_logger = Log.CreateLogger<WindowsGitProcess>();

    // Suspended commands that could not be ended, each held by its handle until it has ended.
    private static readonly HashSet<SafeProcessHandle> s_unendedCommands = [];

    // Process.Start creates its inheritable pipe ends and its process under this lock, so that one start
    // does not hand another's pipes to its child. Without it the suspended start is not made at all: a
    // start the runtime made meanwhile could inherit this command's pipe ends and hold them open.
    private static readonly object? s_createProcessLock =
        typeof(Process).GetField("s_createProcessLock", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);

    private static int s_missingLockReported;

    private readonly Process _process;
    private readonly SafeProcessHandle _handle;
    private readonly SafeJobObjectHandle _job;
    private readonly StreamWriter _standardInput;
    private readonly StreamReader _standardOutput;
    private readonly StreamReader _standardError;

    private WindowsGitProcess(
        Process process,
        SafeProcessHandle handle,
        SafeJobObjectHandle job,
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

    public override bool OwnsDescendants => true;

    // False where the runtime does not expose the lock Process.Start takes, so commands start with
    // Process and own no job.
    internal static bool IsSupported => s_createProcessLock is not null;

    // Null only where the runtime does not expose the lock Process.Start takes, so that the caller starts
    // the command with Process instead. Any other start either gives the command a job of its own or
    // throws, without the command having run.
    internal static WindowsGitProcess? TryStart(ProcessStartInfo startInfo)
    {
        if (s_createProcessLock is not { } createProcessLock)
        {
            if (Interlocked.Exchange(ref s_missingLockReported, 1) == 0)
            {
                s_logger.LogWarning(
                    "The runtime does not expose the lock Process.Start takes; Git commands start without a job of their own.");
            }

            return null;
        }

        char[] commandLine = WindowsProcessArguments.BuildCommandLine(startInfo);
        char[] environment = WindowsProcessArguments.BuildEnvironmentBlock(startInfo.Environment);
        string? workingDirectory = string.IsNullOrEmpty(startInfo.WorkingDirectory)
            ? null
            : startInfo.WorkingDirectory;

        SafeJobObjectHandle job = CreateJob(startInfo.FileName);
        SuspendedCommand? command = null;
        Process? process = null;
        StreamWriter? standardInput = null;
        StreamReader? standardOutput = null;
        StreamReader? standardError = null;
        bool resumed = false;
        try
        {
            command = StartInJob(createProcessLock, job, commandLine, environment, startInfo.FileName, workingDirectory);
            process = Process.GetProcessById(command.ProcessId);

            // Everything that can fail is done before the command runs, so a failure never leaves a
            // command that has already done work.
            standardInput = new StreamWriter(
                new WindowsGitPipeStream(command.TakeInput(), PipeDirection.Out),
                startInfo.StandardInputEncoding ?? s_utf8,
                StreamBufferSize)
            {
                AutoFlush = true,
            };
            standardOutput = new StreamReader(
                new WindowsGitPipeStream(command.TakeOutput(), PipeDirection.In),
                startInfo.StandardOutputEncoding ?? s_utf8,
                detectEncodingFromByteOrderMarks: true,
                StreamBufferSize);
            standardError = new StreamReader(
                new WindowsGitPipeStream(command.TakeError(), PipeDirection.In),
                startInfo.StandardErrorEncoding ?? s_utf8,
                detectEncodingFromByteOrderMarks: true,
                StreamBufferSize);

            if (Native.ResumeThread(command.Thread) == -1)
            {
                throw CreateStartException(Marshal.GetLastPInvokeError(), startInfo.FileName, workingDirectory);
            }

            resumed = true;
            return new WindowsGitProcess(
                process,
                command.Process,
                job,
                standardInput,
                standardOutput,
                standardError);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or ArgumentException
                                   or IOException
                                   or NotSupportedException)
        {
            throw new Win32Exception($"Git '{startInfo.FileName}' could not be started. {ex.Message}", ex);
        }
        finally
        {
            command?.CloseThread();
            if (!resumed)
            {
                command?.Abandon();
                process?.Dispose();
                job.Dispose();
                standardInput?.Dispose();
                standardOutput?.Dispose();
                standardError?.Dispose();
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
        if (Native.TerminateJobObject(_job, 1))
        {
            return;
        }

        // No failure of this call is known to be transient, so it is not repeated. The job's members
        // are the command's descendants, reachable through the tree while their parents live; any that
        // survive both keep cleanup unconfirmed until they exit.
        int error = Marshal.GetLastPInvokeError();
        s_logger.LogWarning(
            "The job of Git process {ProcessId} could not be terminated ({Error}: {Message}); ending its process tree instead.",
            _process.Id,
            error,
            Marshal.GetPInvokeErrorMessage(error));
        ManagedGitProcess.KillTree(_process);
    }

    public override async Task WaitForGroupExitAsync()
    {
        await WaitForExitAsync().ConfigureAwait(false);
        await PollUntilAsync(IsJobEmpty).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        CloseStandardStreams();
        _process.Dispose();
        _handle.Dispose();
        _job.Dispose();
    }

    // Accounting that cannot be read is not taken as an empty job.
    private unsafe bool IsJobEmpty()
    {
        JobObjectBasicAccountingInformation accounting = default;
        return Native.QueryInformationJobObject(
                   _job,
                   JobObjectBasicAccountingInformationClass,
                   &accounting,
                   (uint)sizeof(JobObjectBasicAccountingInformation),
                   0)
               && accounting.ActiveProcesses == 0;
    }

    private static SuspendedCommand StartInJob(
        object createProcessLock,
        SafeJobObjectHandle job,
        char[] commandLine,
        char[] environment,
        string fileName,
        string? workingDirectory)
    {
        SuspendedCommand command = CreateSuspendedCommand(
            createProcessLock,
            commandLine,
            environment,
            fileName,
            workingDirectory,
            leaveCallerJob: false);
        if (Native.AssignProcessToJobObject(job, command.Process))
        {
            return command;
        }

        int error = Marshal.GetLastPInvokeError();
        command.Abandon();

        // The job this process is in may not take a job below it. A command that leaves that job can
        // still join one of its own, where the job lets it leave.
        try
        {
            command = CreateSuspendedCommand(
                createProcessLock,
                commandLine,
                environment,
                fileName,
                workingDirectory,
                leaveCallerJob: true);
        }
        catch (Win32Exception)
        {
            throw CreateJobException(error, fileName);
        }

        if (Native.AssignProcessToJobObject(job, command.Process))
        {
            return command;
        }

        error = Marshal.GetLastPInvokeError();
        command.Abandon();
        throw CreateJobException(error, fileName);
    }

    private static unsafe SuspendedCommand CreateSuspendedCommand(
        object createProcessLock,
        char[] commandLine,
        char[] environment,
        string fileName,
        string? workingDirectory,
        bool leaveCallerJob)
    {
        SafePipeHandle? parentInput = null;
        SafePipeHandle? parentOutput = null;
        SafePipeHandle? parentError = null;
        SafePipeHandle? childInput = null;
        SafePipeHandle? childOutput = null;
        SafePipeHandle? childError = null;
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
                int flags = CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment
                            | (leaveCallerJob ? CreateBreakawayFromJob : 0);
                ProcessInformation information = default;
                // CreateProcess may write to the command line, so each attempt gets a copy of its own.
                char[] writableCommandLine = (char[])commandLine.Clone();
                fixed (char* commandLinePointer = writableCommandLine)
                fixed (char* environmentPointer = environment)
                {
                    created = Native.CreateProcessW(
                        null,
                        commandLinePointer,
                        0,
                        0,
                        true,
                        flags,
                        environmentPointer,
                        workingDirectory,
                        &startup,
                        &information);
                }

                if (!created)
                {
                    throw CreateStartException(Marshal.GetLastPInvokeError(), fileName, workingDirectory);
                }

                return new SuspendedCommand(
                    new SafeProcessHandle(information.Process, ownsHandle: true),
                    information.ProcessId,
                    information.Thread,
                    parentInput,
                    parentOutput,
                    parentError);
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
                }
            }
        }
    }

    // As Process does: an inheritable pipe whose end for this process is replaced by a duplicate that
    // the command does not inherit.
    private static void CreatePipe(
        out SafePipeHandle parentHandle,
        out SafePipeHandle childHandle,
        bool parentInputs)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = 1,
        };
        SafePipeHandle inheritableParent;
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

    // A descendant may still leave on purpose, as setsid lets it on Unix. A job that cannot allow that is
    // not used, since starting a process with CREATE_BREAKAWAY_FROM_JOB would fail inside it.
    private static unsafe SafeJobObjectHandle CreateJob(string fileName)
    {
        SafeJobObjectHandle job = Native.CreateJobObjectW(0, null);
        if (job.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw CreateJobException(error, fileName);
        }

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
            int error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw CreateJobException(error, fileName);
        }

        return job;
    }

    // TerminateProcess only begins the end of a process, which nothing can stop once begun, so the
    // handle may be closed as soon as it succeeds. It fails for a process that has already ended.
    private static bool TryEnd(SafeProcessHandle process, out int error)
    {
        if (Native.TerminateProcess(process, 1))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastPInvokeError();
        return Native.WaitForSingleObject(process, 0) == WaitObject0;
    }

    private static async Task KeepUntilEndedAsync(SafeProcessHandle process)
    {
        lock (s_unendedCommands)
        {
            s_unendedCommands.Add(process);
        }

        await PollUntilAsync(() => TryEnd(process, out _)).ConfigureAwait(false);
        lock (s_unendedCommands)
        {
            s_unendedCommands.Remove(process);
        }

        process.Dispose();
    }

    // Worded as Process words a failed start.
    private static Win32Exception CreateStartException(int error, string fileName, string? workingDirectory)
    {
        string reason = error is ErrorBadExecutableFormat or ErrorExecutableMachineTypeMismatch
            ? "The specified executable is not a valid application for this OS platform."
            : Marshal.GetPInvokeErrorMessage(error);
        return new Win32Exception(
            error,
            $"An error occurred trying to start process '{fileName}' with working directory "
            + $"'{workingDirectory ?? Directory.GetCurrentDirectory()}'. {reason}");
    }

    private static Win32Exception CreateJobException(int error, string fileName)
        => new(
            error,
            $"Git '{fileName}' was not started, because it could not be given a job object of its own. "
            + Marshal.GetPInvokeErrorMessage(error));

    // A command created suspended, with this process's ends of its pipes until streams take them.
    private sealed class SuspendedCommand(
        SafeProcessHandle process,
        int processId,
        nint thread,
        SafePipeHandle input,
        SafePipeHandle output,
        SafePipeHandle error)
    {
        private SafePipeHandle? _input = input;
        private SafePipeHandle? _output = output;
        private SafePipeHandle? _error = error;
        private bool _abandoned;

        public SafeProcessHandle Process { get; } = process;

        public int ProcessId { get; } = processId;

        public nint Thread { get; private set; } = thread;

        public SafePipeHandle TakeInput() => Take(ref _input);

        public SafePipeHandle TakeOutput() => Take(ref _output);

        public SafePipeHandle TakeError() => Take(ref _error);

        public void CloseThread()
        {
            if (Thread != 0)
            {
                Native.CloseHandle(Thread);
                Thread = 0;
            }
        }

        // A command that was never resumed has run none of its own code, so ending it undoes nothing.
        // Safe to repeat, so a cleanup path can never replace the failure that led to it.
        public void Abandon()
        {
            if (_abandoned)
            {
                return;
            }

            _abandoned = true;
            bool ended = TryEnd(Process, out int error);
            CloseThread();
            _input?.Dispose();
            _output?.Dispose();
            _error?.Dispose();
            if (ended)
            {
                Process.Dispose();
                return;
            }

            // Its handle is the only way left to end it, so it is not closed until the command has ended.
            s_logger.LogWarning(
                "Git process {ProcessId}, which never ran, could not be ended ({Error}: {Message}); ending it is tried again until it has ended.",
                ProcessId,
                error,
                Marshal.GetPInvokeErrorMessage(error));
            _ = KeepUntilEndedAsync(Process);
        }

        private static SafePipeHandle Take(ref SafePipeHandle? handle)
        {
            SafePipeHandle taken = handle ?? throw new InvalidOperationException("The pipe end was already taken.");
            handle = null;
            return taken;
        }
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
            out SafePipeHandle readPipe,
            out SafePipeHandle writePipe,
            ref SecurityAttributes pipeAttributes,
            int size);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DuplicateHandle(
            nint sourceProcess,
            SafePipeHandle sourceHandle,
            nint targetProcess,
            out SafePipeHandle targetHandle,
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

        [LibraryImport("kernel32.dll")]
        internal static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

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
