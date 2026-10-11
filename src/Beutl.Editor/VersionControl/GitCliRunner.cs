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

// LocalUnbounded and LocalWithLfs commands hold the index lock while filters or hooks run: add,
// commit, restore, switch, merge and stash. On Unix a canceled one is first sent SIGTERM, on which Git
// removes its lock files, and is killed only if it is still running after a grace period. Local
// commands are short and Network ones hold no lock while they transfer, so both are killed at once.
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
    private static readonly TimeSpan s_defaultLocalTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_defaultStopGracePeriod = TimeSpan.FromSeconds(5);
    internal const int IncompleteStdoutExitCode = -1;
    private static readonly Encoding s_utf8WithoutPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const string IncompleteStdoutDiagnostic =
        "[Git exited, but a process it started still held its standard output open, so the output was not read to its end]";
    internal static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(10);
    private readonly string _gitPath;
    private readonly TimeSpan _localTimeout;
    private readonly TimeSpan _stopGracePeriod;
    private readonly IReadOnlyDictionary<string, string?>? _environmentOverrides;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string> _readAllText;
    private readonly bool _supportsConditionalLockDeletion;
    private readonly Func<string, RepositoryLockFileSnapshot?> _readLockFileSnapshot;
    private readonly Func<string, RepositoryLockFileSnapshot, bool> _deleteLockFileConditionally;
    private readonly Func<string, IEnumerable<string>> _enumerateFileSystemEntries;
    private readonly ConditionalWeakTable<RepositoryLockInfo, RepositoryLockFileIdentityBox>
        _lockFileIdentities = new();
    private int _activeProcesses;

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
        TimeSpan? stopGracePeriod = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitPath);
        if (localTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(localTimeout));
        }

        if (stopGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stopGracePeriod));
        }

        _gitPath = gitPath;
        _localTimeout = localTimeout;
        _stopGracePeriod = stopGracePeriod ?? s_defaultStopGracePeriod;
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
            // Git reads its input as UTF-8.
            options.StandardInputBytes
            ?? (options.StandardInput is null ? null : s_utf8WithoutPreamble.GetBytes(options.StandardInput)),
            options.CaptureStdoutBytes,
            throwOnFailure: true,
            stopGracefully: options.ExecutionKind is GitCommandExecutionKind.LocalUnbounded
                or GitCommandExecutionKind.LocalWithLfs).ConfigureAwait(false);
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

    // Builds the start info under the command's execution policy.
    private async Task<(ProcessStartInfo StartInfo, GitExecutionPolicy ExecutionPolicy)> PrepareStartAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
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
        byte[]? standardInput,
        bool captureStdoutBytes,
        bool throwOnFailure,
        bool stopGracefully = false)
    {
        int exitCode;
        (string Output, byte[]? OutputBytes, bool Truncated) stdout;
        string stderr;
        Interlocked.Increment(ref _activeProcesses);
        try
        {
            (exitCode, stdout, stderr) = await GitProcess.RunAsync(
                startInfo,
                standardInput,
                (reader, token) => CaptureStandardOutputAsync(
                    reader.BaseStream,
                    maxStdoutBytes,
                    captureStdoutBytes,
                    token),
                (reader, token) => ReadStandardErrorAsync(reader, stderrProgress, token),
                executionPolicy.HasFlag(GitExecutionPolicy.LocalTimeout) ? _localTimeout : null,
                cancellationToken,
                stopGracefully ? _stopGracePeriod : null).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new GitOperationException(-1, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeProcesses);
        }

        stderr = GitDiagnosticSanitizer.RedactCredentials(stderr);
        // A process the command left behind can hold stdout past its exit, and reading then stops
        // before the end. A caller that set a limit checks StdoutTruncated; any other caller needs all
        // of the output, so the command counts as failed.
        if (exitCode == 0 && stdout.Truncated && maxStdoutBytes is null)
        {
            exitCode = IncompleteStdoutExitCode;
            stderr = stderr.Length == 0 || stderr.EndsWith('\n')
                ? stderr + IncompleteStdoutDiagnostic
                : $"{stderr}\n{IncompleteStdoutDiagnostic}";
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
            stdout.Output,
            stderr,
            stdout.Truncated,
            stdout.OutputBytes);
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
}
