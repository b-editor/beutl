using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

internal sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);

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
        bool networkOperation,
        CancellationToken cancellationToken);

    RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository);

    bool RemoveRecoverableRepositoryLock(
        RepositoryInfo repository,
        RepositoryLockInfo lockInfo);
}

internal sealed class GitCliRunner : IGitCliRunner
{
    private static readonly TimeSpan s_defaultLocalTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(10);
    private readonly string _gitPath;
    private readonly TimeSpan _localTimeout;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverrides;
    private readonly TimeProvider _timeProvider;
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
        IReadOnlyDictionary<string, string>? environmentOverrides,
        TimeProvider? timeProvider = null)
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
    }

    public event EventHandler<GitRepositoryLockEventArgs>? RepositoryLockFailed;

    public bool HasActiveProcess => Volatile.Read(ref _activeProcesses) > 0;

    public async Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        bool networkOperation = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = CreateStartInfo(repository, arguments, networkOperation);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new GitOperationException(-1, ex.Message);
        }

        Interlocked.Increment(ref _activeProcesses);
        try
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = networkOperation
                ? null
                : new CancellationTokenSource(_localTimeout);
            using var linkedCts = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
                {
                    throw new TimeoutException($"Git did not finish within {_localTimeout}.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
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

            return new GitCommandResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            Interlocked.Decrement(ref _activeProcesses);
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

        string lockPath = GetIndexLockPath(repository);
        if (!File.Exists(lockPath))
        {
            return null;
        }

        var lastWriteTime = new DateTimeOffset(
            File.GetLastWriteTimeUtc(lockPath),
            TimeSpan.Zero);
        return _timeProvider.GetUtcNow() - lastWriteTime > StaleLockAge
            ? new RepositoryLockInfo(lockPath, lastWriteTime)
            : null;
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

        File.Delete(current.LockPath);
        return true;
    }

    internal ProcessStartInfo CreateStartInfo(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        bool networkOperation)
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

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["LC_ALL"] = "C";
        if (networkOperation)
        {
            startInfo.Environment["GIT_SSH_COMMAND"] = "ssh -oBatchMode=yes";
        }

        if (_environmentOverrides is not null)
        {
            foreach ((string key, string value) in _environmentOverrides)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

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
                                   or NotSupportedException)
        {
        }
    }

    private static string GetIndexLockPath(RepositoryInfo repository)
    {
        string dotGitPath = Path.Combine(repository.RepoRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return Path.Combine(dotGitPath, "index.lock");
        }

        if (File.Exists(dotGitPath))
        {
            const string prefix = "gitdir:";
            string contents = File.ReadAllText(dotGitPath).Trim();
            if (contents.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string gitDirectory = contents[prefix.Length..].Trim();
                if (!Path.IsPathFullyQualified(gitDirectory))
                {
                    gitDirectory = Path.Combine(repository.RepoRoot, gitDirectory);
                }

                return Path.Combine(Path.GetFullPath(gitDirectory), "index.lock");
            }
        }

        return Path.Combine(dotGitPath, "index.lock");
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
