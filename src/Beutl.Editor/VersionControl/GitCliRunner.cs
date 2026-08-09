using System.Diagnostics;
using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed record GitCommandResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool StdoutTruncated = false);

internal enum GitCommandExecutionKind
{
    Local,
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
    bool UseLiteralPathspecs = true)
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

internal sealed class GitCliRunner : IGitCliRunner
{
    private const string DefaultSshCommand = "ssh -oBatchMode=yes";
    private static readonly string[] s_repositoryLocalEnvironmentVariables =
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_AUTHOR_EMAIL",
        "GIT_AUTHOR_NAME",
        "GIT_CEILING_DIRECTORIES",
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
        "GIT_REPLACE_REF_BASE",
        "GIT_PREFIX",
        "GIT_SHALLOW_FILE",
        "GIT_COMMON_DIR",
    ];
    private static readonly TimeSpan s_cleanupGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_defaultLocalTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(10);
    private readonly string _gitPath;
    private readonly TimeSpan _localTimeout;
    private readonly IReadOnlyDictionary<string, string?>? _environmentOverrides;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string> _readAllText;
    private readonly Action<string> _deleteFile;
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
        Action<string>? deleteFile = null)
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
        _deleteFile = deleteFile ?? File.Delete;
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
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxStdoutBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

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
        return await RunProcessAsync(
            repository,
            startInfo,
            executionPolicy,
            cancellationToken,
            stderrProgress,
            options.MaxStdoutBytes,
            options.StandardInput,
            throwOnFailure: true).ConfigureAwait(false);
    }

    internal async Task<ProcessStartInfo> CreateStartInfoAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxStdoutBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        GitExecutionPolicy executionPolicy = await ResolveExecutionPolicyAsync(
            repository,
            options,
            cancellationToken).ConfigureAwait(false);
        return CreateStartInfo(
            repository,
            arguments,
            executionPolicy,
            options.EnvironmentOverrides,
            options.UseLiteralPathspecs);
    }

    private async Task<GitCommandResult> RunProcessAsync(
        RepositoryInfo repository,
        ProcessStartInfo startInfo,
        GitExecutionPolicy executionPolicy,
        CancellationToken cancellationToken,
        IProgress<string>? stderrProgress,
        int? maxStdoutBytes,
        string? standardInput,
        bool throwOnFailure)
    {
        using var process = new Process { StartInfo = startInfo };
        Interlocked.Increment(ref _activeProcesses);
        try
        {
            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new GitOperationException(-1, ex.Message);
            }

            Task<(string Output, bool Truncated)> stdoutTask = ReadStandardOutputAsync(
                process.StandardOutput.BaseStream,
                maxStdoutBytes);
            Task<string> stderrTask = stderrProgress is null
                ? process.StandardError.ReadToEndAsync()
                : ReadStandardErrorAsync(process.StandardError, stderrProgress);
            using var timeoutCts = executionPolicy.HasFlag(GitExecutionPolicy.LocalTimeout)
                ? new CancellationTokenSource(_localTimeout)
                : null;
            using var linkedCts = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            Task stdinTask = WriteStandardInputAsync(
                process.StandardInput,
                standardInput,
                linkedCts.Token);
            Task processExitTask = process.WaitForExitAsync(CancellationToken.None);
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
                TryKillProcessTree(process);
                TryCloseRedirectedStreams(process);
                Task cleanup = Task.WhenAll(
                    ObserveCleanupTaskAsync(completion),
                    ObserveCleanupTaskAsync(processExitTask),
                    ObserveCleanupTaskAsync(stdinTask),
                    ObserveCleanupTaskAsync(stdoutTask),
                    ObserveCleanupTaskAsync(stderrTask));
                await WaitForCleanupGracePeriodAsync(cleanup).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
                {
                    throw new TimeoutException($"Git did not finish within {_localTimeout}.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            (string stdout, bool stdoutTruncated) = await stdoutTask.ConfigureAwait(false);
            string stderr = GitDiagnosticSanitizer.RedactCredentials(
                await stderrTask.ConfigureAwait(false));
            if (throwOnFailure && process.ExitCode != 0)
            {
                var exception = new GitOperationException(process.ExitCode, stderr);
                if (exception.IsRepositoryLockFailure)
                {
                    RepositoryLockFailed?.Invoke(
                        this,
                        new GitRepositoryLockEventArgs(repository, exception));
                }

                throw exception;
            }

            return new GitCommandResult(
                process.ExitCode,
                stdout,
                stderr,
                stdoutTruncated);
        }
        finally
        {
            Interlocked.Decrement(ref _activeProcesses);
        }
    }

    private static async Task WaitForCleanupGracePeriodAsync(Task cleanup)
    {
        try
        {
            await cleanup.WaitAsync(s_cleanupGracePeriod).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
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

    public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (HasActiveProcess)
        {
            return null;
        }

        try
        {
            foreach (string lockPath in GetRepositoryLockPaths(repository))
            {
                if (!File.Exists(lockPath))
                {
                    continue;
                }

                var lastWriteTime = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(lockPath),
                    TimeSpan.Zero);
                if (_timeProvider.GetUtcNow() - lastWriteTime > StaleLockAge)
                {
                    return new RepositoryLockInfo(lockPath, lastWriteTime);
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return null;
        }
    }

    public bool RemoveRecoverableRepositoryLock(
        RepositoryInfo repository,
        RepositoryLockInfo lockInfo)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(lockInfo);
        RepositoryLockInfo? current = GetRecoverableRepositoryLock(repository);
        if (current is null
            || !string.Equals(
                current.LockPath,
                Path.GetFullPath(lockInfo.LockPath),
                PathComparison)
            || current.LastWriteTimeUtc != lockInfo.LastWriteTimeUtc)
        {
            return false;
        }

        try
        {
            _deleteFile(current.LockPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or NotSupportedException
                                   or AggregateException)
        {
        }
    }

    private static void TryCloseRedirectedStreams(Process process)
    {
        TryCloseStream(() => process.StandardInput.BaseStream);
        TryCloseStream(() => process.StandardOutput.BaseStream);
        TryCloseStream(() => process.StandardError.BaseStream);
    }

    private static void TryCloseStream(Func<Stream> getStream)
    {
        try
        {
            getStream().Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static async Task WriteStandardInputAsync(
        TextWriter writer,
        string? input,
        CancellationToken cancellationToken)
    {
        try
        {
            if (input is not null)
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

    internal static async Task<string> ReadStandardErrorAsync(
        TextReader reader,
        IProgress<string> progress)
    {
        var output = new System.Text.StringBuilder();
        var progressLine = new System.Text.StringBuilder();
        var buffer = new char[256];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            output.Append(buffer, 0, count);
            for (int i = 0; i < count; i++)
            {
                char value = buffer[i];
                if (value is '\r' or '\n')
                {
                    if (progressLine.Length > 0)
                    {
                        progress.Report(GitDiagnosticSanitizer.RedactCredentials(
                            progressLine.ToString()));
                        progressLine.Clear();
                    }
                }
                else
                {
                    progressLine.Append(value);
                }
            }
        }

        if (progressLine.Length > 0)
        {
            progress.Report(GitDiagnosticSanitizer.RedactCredentials(
                progressLine.ToString()));
        }

        return GitDiagnosticSanitizer.RedactCredentials(output.ToString());
    }

    internal static async Task<(string Output, bool Truncated)> ReadStandardOutputAsync(
        Stream stream,
        int? maxBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (maxBytes is null)
        {
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            return (await reader.ReadToEndAsync().ConfigureAwait(false), false);
        }

        int limit = maxBytes.Value;
        var captured = new byte[limit];
        var buffer = new byte[8192];
        int capturedCount = 0;
        bool truncated = false;
        int count;
        while ((count = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            int copyCount = Math.Min(count, limit - capturedCount);
            if (copyCount > 0)
            {
                buffer.AsSpan(0, copyCount).CopyTo(captured.AsSpan(capturedCount));
                capturedCount += copyCount;
            }

            truncated |= copyCount < count;
        }

        int completeByteCount = GetCompleteUtf8PrefixLength(
            captured.AsSpan(0, capturedCount));
        return (
            Encoding.UTF8.GetString(captured, 0, completeByteCount),
            truncated);
    }

    private static int GetCompleteUtf8PrefixLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        int sequenceStart = bytes.Length - 1;
        while (sequenceStart > 0 && (bytes[sequenceStart] & 0xC0) == 0x80)
        {
            sequenceStart--;
        }

        int sequenceLength = bytes[sequenceStart] switch
        {
            < 0x80 => 1,
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 1,
        };
        return bytes.Length - sequenceStart < sequenceLength
            ? sequenceStart
            : bytes.Length;
    }

    private IReadOnlyList<string> GetRepositoryLockPaths(RepositoryInfo repository)
    {
        string gitDirectory = GetGitDirectory(repository);
        List<string> lockPaths =
        [
            Path.Combine(gitDirectory, "index.lock"),
            Path.Combine(gitDirectory, "HEAD.lock"),
        ];
        try
        {
            string commonDirectory = GetCommonDirectory(gitDirectory);
            lockPaths.Add(Path.Combine(commonDirectory, "config.lock"));
            string? branchLockPath = GetCurrentBranchLockPath(gitDirectory);
            if (branchLockPath is not null)
            {
                lockPaths.Add(branchLockPath);
            }

            lockPaths.AddRange(GetRefLockPaths(gitDirectory));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
        }

        return lockPaths;
    }

    private IReadOnlyList<string> GetRefLockPaths(string gitDirectory)
    {
        string commonDirectory = GetCommonDirectory(gitDirectory);
        string refsDirectory = Path.GetFullPath(Path.Combine(commonDirectory, "refs"));
        if (!Directory.Exists(refsDirectory))
        {
            return [];
        }

        string refsPrefix = Path.TrimEndingDirectorySeparator(refsDirectory)
                             + Path.DirectorySeparatorChar;
        var lockPaths = new List<string>();
        foreach (string candidate in Directory.EnumerateFiles(
                     refsDirectory,
                     "*.lock",
                     SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(candidate);
            if (!fullPath.StartsWith(refsPrefix, PathComparison))
            {
                continue;
            }

            if (ContainsReparsePoint(
                    commonDirectory,
                    Path.GetDirectoryName(fullPath)!))
            {
                continue;
            }

            lockPaths.Add(fullPath);
        }

        return lockPaths;
    }

    private string? GetCurrentBranchLockPath(string gitDirectory)
    {
        const string refPrefix = "ref: refs/heads/";
        string head = _readAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        if (!head.StartsWith(refPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string branchPath = head[refPrefix.Length..];
        if (string.IsNullOrWhiteSpace(branchPath)
            || Path.IsPathFullyQualified(branchPath)
            || branchPath
                .Split(['/', '\\'])
                .Any(static segment => segment is "" or "." or ".."))
        {
            return null;
        }

        string commonDirectory = GetCommonDirectory(gitDirectory);
        string headsDirectory = Path.GetFullPath(
            Path.Combine(commonDirectory, "refs", "heads"));
        string refPath = Path.GetFullPath(Path.Combine(
            headsDirectory,
            branchPath.Replace('/', Path.DirectorySeparatorChar)));
        string headsPrefix = Path.TrimEndingDirectorySeparator(headsDirectory)
                             + Path.DirectorySeparatorChar;
        if (!refPath.StartsWith(headsPrefix, PathComparison))
        {
            return null;
        }

        if (ContainsReparsePoint(
                commonDirectory,
                Path.GetDirectoryName(refPath)!))
        {
            return null;
        }

        return refPath + ".lock";
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        string current = Path.GetFullPath(path);
        string boundary = Path.GetFullPath(root);
        while (true)
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (string.Equals(current, boundary, PathComparison))
            {
                return false;
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, PathComparison))
            {
                return true;
            }

            current = parent;
        }
    }

    private string GetCommonDirectory(string gitDirectory)
    {
        string commonDirectoryPath = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirectoryPath))
        {
            return gitDirectory;
        }

        string commonDirectory = _readAllText(commonDirectoryPath).Trim();
        if (!Path.IsPathFullyQualified(commonDirectory))
        {
            commonDirectory = Path.Combine(gitDirectory, commonDirectory);
        }

        return Path.GetFullPath(commonDirectory);
    }

    private string GetGitDirectory(RepositoryInfo repository)
    {
        string dotGitPath = Path.Combine(repository.RepoRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return Path.GetFullPath(dotGitPath);
        }

        if (File.Exists(dotGitPath))
        {
            const string prefix = "gitdir:";
            string contents = _readAllText(dotGitPath).Trim();
            if (contents.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string gitDirectory = contents[prefix.Length..].Trim();
                if (!Path.IsPathFullyQualified(gitDirectory))
                {
                    gitDirectory = Path.Combine(repository.RepoRoot, gitDirectory);
                }

                return Path.GetFullPath(gitDirectory);
            }
        }

        return Path.GetFullPath(dotGitPath);
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
