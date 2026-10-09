using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed record GitCommandResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool StdoutTruncated = false,
    byte[]? StdoutBytes = null);

internal enum GitCommandExecutionKind
{
    Local,
    // Local work that runs the user's own programs, such as commit hooks or a commit signer. They can
    // wait on the user or set up tools, so only cancellation stops them.
    LocalUnbounded,
    LocalWithLfs,
    Network,
}

[Flags]
internal enum GitExecutionPolicy
{
    Unbounded = 0,
    LocalTimeout = 1 << 0,
    DefaultOpenSshBatchMode = 1 << 1,
}

internal sealed record GitCommandOptions(
    GitCommandExecutionKind ExecutionKind,
    IReadOnlyDictionary<string, string?>? EnvironmentOverrides = null,
    int? MaxStdoutBytes = null,
    string? StandardInput = null,
    bool UseLiteralPathspecs = true,
    byte[]? StandardInputBytes = null,
    bool CaptureStdoutBytes = false)
{
    public static GitCommandOptions Local { get; } = new(GitCommandExecutionKind.Local);

    public static GitCommandOptions Network { get; } = new(GitCommandExecutionKind.Network);
}

internal sealed class GitRepositoryLockEventArgs(
    RepositoryInfo repository,
    GitOperationException exception) : EventArgs
{
    public RepositoryInfo Repository { get; } = repository;

    public GitOperationException Exception { get; } = exception;
}

internal readonly record struct RepositoryLockFileIdentity(
    uint VolumeSerialNumber,
    ulong FileIndex);

internal readonly record struct RepositoryLockFileSnapshot(
    DateTimeOffset LastWriteTimeUtc,
    RepositoryLockFileIdentity? Identity);

internal interface IGitCliRunner
{
    bool HasActiveProcess { get; }

    Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? stderrProgress = null);

    RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository);

    bool RemoveRecoverableRepositoryLock(
        RepositoryInfo repository,
        RepositoryLockInfo lockInfo);
}

internal sealed partial class GitCliRunner : IGitCliRunner
{
    private const string DefaultSshCommand = "ssh -oBatchMode=yes";
    private static readonly string[] s_repositoryLocalEnvironmentVariables =
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_AUTHOR_DATE",
        "GIT_AUTHOR_EMAIL",
        "GIT_AUTHOR_NAME",
        "GIT_CEILING_DIRECTORIES",
        "GIT_COMMITTER_DATE",
        "GIT_COMMITTER_EMAIL",
        "GIT_COMMITTER_NAME",
        "GIT_CONFIG",
        "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT",
        "GIT_OBJECT_DIRECTORY",
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_IMPLICIT_WORK_TREE",
        "GIT_GRAFT_FILE",
        "GIT_INDEX_FILE",
        "GIT_NO_REPLACE_OBJECTS",
        "GIT_NAMESPACE",
        "GIT_REPLACE_REF_BASE",
        "GIT_PREFIX",
        "GIT_SHALLOW_FILE",
        "GIT_COMMON_DIR",
    ];
    private static readonly TimeSpan s_cleanupGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_defaultLocalTimeout = TimeSpan.FromSeconds(30);
    internal const int UncollectedExitCode = -1;
    private static readonly Encoding s_utf8WithoutPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const string UncollectedExitStatusDiagnostic =
        "[Git's exit status could not be collected; the process was reaped outside Beutl]";
    internal static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(10);
    private readonly string _gitPath;
    private readonly TimeSpan _localTimeout;
    private readonly IReadOnlyDictionary<string, string?>? _environmentOverrides;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string> _readAllText;
    private readonly bool _supportsConditionalLockDeletion;
    private readonly Func<string, RepositoryLockFileSnapshot?> _readLockFileSnapshot;
    private readonly Func<string, RepositoryLockFileSnapshot, bool> _deleteLockFileConditionally;
    private readonly Func<string, IEnumerable<string>> _enumerateFileSystemEntries;
    private readonly Action<GitProcess> _killProcessGroup;
    private readonly Action<GitProcess> _closeRedirectedStreams;
    private readonly object _quarantineSync = new();
    private readonly ConditionalWeakTable<RepositoryLockInfo, RepositoryLockFileIdentityBox>
        _lockFileIdentities = new();
    private TaskCompletionSource? _quarantineQuiesced;
    private int _activeProcesses;
    private int _quarantinedProcesses;

    internal GitCliRunner(string gitPath)
        : this(
            gitPath,
            s_defaultLocalTimeout,
            environmentOverrides: null,
            timeProvider: null)
    {
    }

    internal GitCliRunner(
        string gitPath,
        TimeSpan localTimeout,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        TimeProvider? timeProvider = null,
        Func<string, string>? readAllText = null,
        bool? supportsConditionalLockDeletion = null,
        Func<string, RepositoryLockFileSnapshot?>? readLockFileSnapshot = null,
        Func<string, RepositoryLockFileSnapshot, bool>? deleteLockFileConditionally = null,
        Func<string, IEnumerable<string>>? enumerateFileSystemEntries = null,
        Action<GitProcess>? killProcessGroup = null,
        Action<GitProcess>? closeRedirectedStreams = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitPath);
        if (localTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(localTimeout));
        }

        _gitPath = gitPath;
        _localTimeout = localTimeout;
        _environmentOverrides = environmentOverrides;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _readAllText = readAllText ?? File.ReadAllText;
        _supportsConditionalLockDeletion = supportsConditionalLockDeletion
                                           ?? OperatingSystem.IsWindows();
        _readLockFileSnapshot = readLockFileSnapshot
                                ?? (OperatingSystem.IsWindows()
                                    ? TryReadWindowsLockFileSnapshot
                                    : TryReadPortableLockFileSnapshot);
        _deleteLockFileConditionally = deleteLockFileConditionally
                                       ?? TryDeleteWindowsLockFileConditionally;
        _enumerateFileSystemEntries = enumerateFileSystemEntries
                                      ?? (static path => Directory.EnumerateFileSystemEntries(
                                          path,
                                          "*",
                                          SearchOption.TopDirectoryOnly));
        _killProcessGroup = killProcessGroup ?? (static process => process.Kill());
        _closeRedirectedStreams = closeRedirectedStreams
                                  ?? (static process => process.CloseStandardStreams());
    }

    public event EventHandler<GitRepositoryLockEventArgs>? RepositoryLockFailed;

    public bool HasActiveProcess => Volatile.Read(ref _activeProcesses) > 0;

    public async Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? stderrProgress = null)
    {
        ValidateCommand(repository, arguments, options);
        if (options.StandardInput is not null && options.StandardInputBytes is not null)
        {
            throw new ArgumentException(
                "Only one standard-input representation can be supplied.",
                nameof(options));
        }

        (ProcessStartInfo startInfo, GitExecutionPolicy executionPolicy) = await PrepareStartAsync(
            repository,
            arguments,
            options,
            cancellationToken).ConfigureAwait(false);
        return await RunProcessAsync(
            repository,
            startInfo,
            executionPolicy,
            cancellationToken,
            stderrProgress,
            options.MaxStdoutBytes,
            options.StandardInput,
            options.StandardInputBytes,
            options.CaptureStdoutBytes,
            throwOnFailure: true).ConfigureAwait(false);
    }

    internal async Task<ProcessStartInfo> CreateStartInfoAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(repository, arguments, options);
        (ProcessStartInfo startInfo, _) = await PrepareStartAsync(
            repository,
            arguments,
            options,
            cancellationToken).ConfigureAwait(false);
        return startInfo;
    }

    private static void ValidateCommand(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxStdoutBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    // Waits for quarantined processes to finish, then builds the start info under the command's execution policy.
    private async Task<(ProcessStartInfo StartInfo, GitExecutionPolicy ExecutionPolicy)> PrepareStartAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        await WaitForQuarantineAsync(cancellationToken).ConfigureAwait(false);
        GitExecutionPolicy executionPolicy = await ResolveExecutionPolicyAsync(
            repository,
            options,
            cancellationToken).ConfigureAwait(false);
        ProcessStartInfo startInfo = CreateStartInfo(
            repository,
            arguments,
            executionPolicy,
            options.EnvironmentOverrides,
            options.UseLiteralPathspecs);
        return (startInfo, executionPolicy);
    }

    private async Task<GitCommandResult> RunProcessAsync(
        RepositoryInfo repository,
        ProcessStartInfo startInfo,
        GitExecutionPolicy executionPolicy,
        CancellationToken cancellationToken,
        IProgress<string>? stderrProgress,
        int? maxStdoutBytes,
        string? standardInput,
        byte[]? standardInputBytes,
        bool captureStdoutBytes,
        bool throwOnFailure)
    {
        GitProcess? process = null;
        bool processQuarantined = false;
        Interlocked.Increment(ref _activeProcesses);
        try
        {
            try
            {
                process = GitProcess.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new GitOperationException(-1, ex.Message);
            }

            Task<(string Output, byte[]? OutputBytes, bool Truncated)> stdoutTask =
                CaptureStandardOutputAsync(
                process.StandardOutput.BaseStream,
                maxStdoutBytes,
                captureStdoutBytes);
            Task<string> stderrTask = ReadStandardErrorAsync(
                process.StandardError,
                stderrProgress);
            using var timeoutCts = executionPolicy.HasFlag(GitExecutionPolicy.LocalTimeout)
                ? new CancellationTokenSource(_localTimeout)
                : null;
            using var linkedCts = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            Task stdinTask = WriteStandardInputAsync(
                process.StandardInput,
                standardInput,
                standardInputBytes,
                linkedCts.Token);
            Task processExitTask = process.WaitForExitAsync();
            Task completion = Task.WhenAll(
                processExitTask,
                stdinTask,
                stdoutTask,
                stderrTask);

            try
            {
                await completion.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                _killProcessGroup(process);
                _closeRedirectedStreams(process);
                Task cleanup = CompleteCleanupAsync(
                    process,
                    completion,
                    processExitTask,
                    stdinTask,
                    stdoutTask,
                    stderrTask);
                if (!await WaitForCleanupGracePeriodAsync(cleanup).ConfigureAwait(false))
                {
                    processQuarantined = true;
                    QuarantineProcess(process, cleanup);
                }

                if (!cancellationToken.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
                {
                    throw new TimeoutException($"Git did not finish within {_localTimeout}.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            (string stdout, byte[]? stdoutBytes, bool stdoutTruncated) =
                await stdoutTask.ConfigureAwait(false);
            string stderr = GitDiagnosticSanitizer.RedactCredentials(
                await stderrTask.ConfigureAwait(false));
            // A status that could not be collected is a failure, never a success.
            int exitCode = process.TryGetExitCode(out int collectedExitCode)
                ? collectedExitCode
                : UncollectedExitCode;
            if (exitCode == UncollectedExitCode)
            {
                stderr = stderr.Length == 0 || stderr.EndsWith('\n')
                    ? stderr + UncollectedExitStatusDiagnostic
                    : $"{stderr}\n{UncollectedExitStatusDiagnostic}";
            }

            if (throwOnFailure && exitCode != 0)
            {
                var exception = new GitOperationException(exitCode, stderr);
                if (exception.IsRepositoryLockFailure)
                {
                    RepositoryLockFailed?.Invoke(
                        this,
                        new GitRepositoryLockEventArgs(repository, exception));
                }

                throw exception;
            }

            return new GitCommandResult(
                exitCode,
                stdout,
                stderr,
                stdoutTruncated,
                stdoutBytes);
        }
        finally
        {
            if (!processQuarantined)
            {
                process?.Dispose();
                Interlocked.Decrement(ref _activeProcesses);
            }
        }
    }

    private async Task WaitForQuarantineAsync(CancellationToken cancellationToken)
    {
        Task? quarantine;
        lock (_quarantineSync)
        {
            quarantine = _quarantineQuiesced?.Task;
        }

        if (quarantine is not null)
        {
            await quarantine.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void QuarantineProcess(GitProcess process, Task cleanup)
    {
        lock (_quarantineSync)
        {
            _quarantinedProcesses++;
            _quarantineQuiesced ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = CompleteQuarantinedProcessAsync(process, cleanup);
    }

    private async Task CompleteQuarantinedProcessAsync(GitProcess process, Task cleanup)
    {
        try
        {
            await cleanup.ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
            Interlocked.Decrement(ref _activeProcesses);

            TaskCompletionSource? quiesced = null;
            lock (_quarantineSync)
            {
                _quarantinedProcesses--;
                if (_quarantinedProcesses == 0)
                {
                    quiesced = _quarantineQuiesced;
                    _quarantineQuiesced = null;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    // Cleanup is confirmed only when the pipes have closed and no process the command owned remains.
    // A member that survives the kill may still hold a repository lock, so it keeps the runner
    // quarantined even after the command itself has exited.
    private static async Task CompleteCleanupAsync(GitProcess process, params Task[] tasks)
    {
        foreach (Task task in tasks)
        {
            await ObserveCleanupTaskAsync(task).ConfigureAwait(false);
        }

        await process.WaitForGroupExitAsync().ConfigureAwait(false);
    }

    private static async Task<bool> WaitForCleanupGracePeriodAsync(Task cleanup)
    {
        try
        {
            await cleanup.WaitAsync(s_cleanupGracePeriod).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task ObserveCleanupTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    public static IReadOnlyList<string> SplitNullSeparated(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    internal ProcessStartInfo CreateStartInfo(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitExecutionPolicy executionPolicy,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        bool useLiteralPathspecs = true)
    {
        var startInfo = new ProcessStartInfo(_gitPath)
        {
            WorkingDirectory = repository.RepoRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Git reads its input as UTF-8. Process would otherwise encode it with the console code page
            // on Windows, which is the ANSI code page for an application without a console.
            StandardInputEncoding = s_utf8WithoutPreamble,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (string name in s_repositoryLocalEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }

        ApplyEnvironmentOverrides(startInfo, _environmentOverrides);
        ApplyEnvironmentOverrides(startInfo, environmentOverrides);
        GitProcessEnvironment.ConfigureToolSearchPath(startInfo);

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_LITERAL_PATHSPECS"] = useLiteralPathspecs ? "1" : "0";
        startInfo.Environment["LC_ALL"] = "C";
        if (executionPolicy.HasFlag(GitExecutionPolicy.DefaultOpenSshBatchMode)
            && !HasConfiguredSshCommandEnvironment(startInfo)
            && IsDefaultOpenSshVariant(GetSshVariantEnvironment(startInfo)))
        {
            startInfo.Environment["GIT_SSH_COMMAND"] = DefaultSshCommand;
        }

        return startInfo;
    }

    private async Task<GitExecutionPolicy> ResolveExecutionPolicyAsync(
        RepositoryInfo repository,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (options.ExecutionKind == GitCommandExecutionKind.Local)
        {
            return GitExecutionPolicy.LocalTimeout;
        }

        if (options.ExecutionKind == GitCommandExecutionKind.LocalUnbounded)
        {
            return GitExecutionPolicy.Unbounded;
        }

        bool localWithLfs = options.ExecutionKind == GitCommandExecutionKind.LocalWithLfs;
        if (!localWithLfs && options.ExecutionKind != GitCommandExecutionKind.Network)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        ProcessStartInfo environmentProbe = CreateStartInfo(
            repository,
            [],
            GitExecutionPolicy.Unbounded,
            options.EnvironmentOverrides,
            options.UseLiteralPathspecs);
        if (HasConfiguredSshCommandEnvironment(environmentProbe))
        {
            return GitExecutionPolicy.Unbounded;
        }

        ProcessStartInfo configProbe = CreateStartInfo(
            repository,
            ["config", "--null", "--get-regexp", "^(core\\.sshcommand|ssh\\.variant)$"],
            GitExecutionPolicy.LocalTimeout,
            options.EnvironmentOverrides,
            options.UseLiteralPathspecs);
        GitCommandResult configResult = await RunProcessAsync(
            repository,
            configProbe,
            GitExecutionPolicy.LocalTimeout,
            cancellationToken,
            stderrProgress: null,
            maxStdoutBytes: null,
            standardInput: null,
            standardInputBytes: null,
            captureStdoutBytes: false,
            throwOnFailure: false).ConfigureAwait(false);

        if (configResult.ExitCode == 1)
        {
            return IsDefaultOpenSshVariant(GetSshVariantEnvironment(environmentProbe))
                ? GitExecutionPolicy.DefaultOpenSshBatchMode
                : GitExecutionPolicy.Unbounded;
        }

        if (configResult.ExitCode != 0)
        {
            return GitExecutionPolicy.Unbounded;
        }

        bool foundConfiguration = false;
        bool hasConfiguredSshCommand = false;
        string? configuredVariant = null;
        foreach (string record in SplitNullSeparated(configResult.Stdout))
        {
            int separator = record.IndexOf('\n');
            if (separator < 0)
            {
                continue;
            }

            string name = record[..separator];
            string value = record[(separator + 1)..];
            if (string.Equals(name, "core.sshcommand", StringComparison.OrdinalIgnoreCase))
            {
                foundConfiguration = true;
                hasConfiguredSshCommand = true;
            }
            else if (string.Equals(name, "ssh.variant", StringComparison.OrdinalIgnoreCase))
            {
                foundConfiguration = true;
                configuredVariant = value;
            }
        }

        if (!foundConfiguration || hasConfiguredSshCommand)
        {
            return GitExecutionPolicy.Unbounded;
        }

        string? effectiveVariant = GetSshVariantEnvironment(environmentProbe) ?? configuredVariant;
        return IsDefaultOpenSshVariant(effectiveVariant)
            ? GitExecutionPolicy.DefaultOpenSshBatchMode
            : GitExecutionPolicy.Unbounded;
    }

    private static void ApplyEnvironmentOverrides(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string?>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        foreach ((string key, string? value) in overrides)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }
    }

    private static bool HasConfiguredSshCommandEnvironment(ProcessStartInfo startInfo)
        => startInfo.Environment.ContainsKey("GIT_SSH_COMMAND")
           || startInfo.Environment.ContainsKey("GIT_SSH");

    private static string? GetSshVariantEnvironment(ProcessStartInfo startInfo)
        => startInfo.Environment.TryGetValue("GIT_SSH_VARIANT", out string? variant)
            ? variant
            : null;

    private static bool IsDefaultOpenSshVariant(string? variant)
        => variant is null
           || string.Equals(variant.Trim(), "ssh", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteStandardInputAsync(
        StreamWriter writer,
        string? input,
        byte[]? inputBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            if (inputBytes is not null)
            {
                await writer.BaseStream.WriteAsync(inputBytes, cancellationToken)
                    .ConfigureAwait(false);
                await writer.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (input is not null)
            {
                await writer.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            writer.Close();
        }
    }
}
