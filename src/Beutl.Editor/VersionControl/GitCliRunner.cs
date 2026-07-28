using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

public sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);

public sealed class GitRepositoryLockEventArgs(
    RepositoryInfo repository,
    GitOperationException exception) : EventArgs
{
    public RepositoryInfo Repository { get; } = repository;

    public GitOperationException Exception { get; } = exception;
}

internal interface IGitCliRunner
{
    Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        bool networkOperation,
        CancellationToken cancellationToken);
}

public sealed class GitCliRunner : IGitCliRunner
{
    private static readonly TimeSpan s_defaultLocalTimeout = TimeSpan.FromSeconds(30);
    private readonly string _gitPath;
    private readonly TimeSpan _localTimeout;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverrides;
    private int _activeProcesses;

    public GitCliRunner(string gitPath)
        : this(gitPath, s_defaultLocalTimeout, environmentOverrides: null)
    {
    }

    internal GitCliRunner(
        string gitPath,
        TimeSpan localTimeout,
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitPath);
        if (localTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(localTimeout));
        }

        _gitPath = gitPath;
        _localTimeout = localTimeout;
        _environmentOverrides = environmentOverrides;
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
}
