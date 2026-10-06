using System.Diagnostics;

namespace Beutl.Editor.VersionControl;

internal sealed record GitProbeResult(int ExitCode, string Stdout, string Stderr);

internal interface IGitInstallationProbe
{
    Task<IReadOnlyList<string>> FindOnPathAsync(string executableName, CancellationToken cancellationToken);

    Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken);

    Task<GitProbeResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);

    bool FileExists(string path);

    string? GetEnvironmentVariable(string name);
}

internal sealed class ProcessGitInstallationProbe : IGitInstallationProbe
{
    private static readonly TimeSpan s_cleanupGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _timeout;

    public static ProcessGitInstallationProbe Instance { get; } = new(s_defaultTimeout);

    internal ProcessGitInstallationProbe(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
    }

    public Task<IReadOnlyList<string>> FindOnPathAsync(
        string executableName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FindOnPath(
            executableName,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT")));
    }

    // Searched in-process rather than through which/where: a minimal Linux image can carry git
    // without those utilities, and reporting no candidates there disables version control for a
    // Git that works.
    internal static IReadOnlyList<string> FindOnPath(
        string executableName,
        string? pathValue,
        string? pathExtensionsValue)
        => FindOnPath(
            executableName,
            pathValue,
            pathExtensionsValue,
            OperatingSystem.IsWindows() ? GitHostPlatform.Windows : GitHostPlatform.Linux,
            Environment.CurrentDirectory);

    internal static IReadOnlyList<string> FindOnPath(
        string executableName,
        string? pathValue,
        string? pathExtensionsValue,
        GitHostPlatform platform,
        string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        if (pathValue is null)
        {
            return [];
        }

        char pathSeparator = platform == GitHostPlatform.Windows ? ';' : ':';
        StringComparer candidateComparer = platform == GitHostPlatform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string[] extensions = platform == GitHostPlatform.Windows
            ? [
                string.Empty,
                .. (pathExtensionsValue ?? string.Empty)
                    .Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(candidateComparer)
            ]
            : [string.Empty];
        string[] directories = platform == GitHostPlatform.Windows
            ? pathValue.Split(
                pathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : pathValue.Split(pathSeparator);
        string resolvedCurrentDirectory = Path.GetFullPath(currentDirectory);
        var candidates = new List<string>();
        foreach (string pathEntry in directories)
        {
            string directory;
            try
            {
                // POSIX specifies an empty PATH component as the current directory. Every relative
                // component has the same captured-CWD dependency, so resolve both forms now before
                // a later process-wide working-directory change can redirect the executable.
                directory = pathEntry.Length == 0
                    ? resolvedCurrentDirectory
                    : Path.IsPathFullyQualified(pathEntry)
                        ? pathEntry
                        : Path.GetFullPath(pathEntry, resolvedCurrentDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or IOException
                                       or NotSupportedException)
            {
                continue;
            }

            foreach (string extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, executableName + extension);
                }
                catch (ArgumentException)
                {
                    // A PATH entry with invalid path characters is not a directory to search.
                    break;
                }

                if (File.Exists(candidate) && !candidates.Contains(candidate, candidateComparer))
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    public async Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        GitProbeResult result = await RunAsync(
            "/usr/bin/xcode-select",
            ["-p"],
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout);
    }

    public async Task<GitProbeResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        var process = new Process { StartInfo = startInfo };
        bool disposeProcess = true;
        try
        {
            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new GitProbeResult(-1, string.Empty, string.Empty);
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(linkedCts.Token);
            Task processExit = process.WaitForExitAsync(linkedCts.Token);
            Task completion = Task.WhenAll(processExit, stdout, stderr);
            try
            {
                await completion.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                Task cleanup = CreateCleanupTask(process, completion, processExit, stdout, stderr);
                await WaitForCleanupGracePeriodAsync(cleanup).ConfigureAwait(false);
                disposeProcess = false;
                _ = DisposeAfterCleanupAsync(process, cleanup);
                cancellationToken.ThrowIfCancellationRequested();
                return new GitProbeResult(-1, string.Empty, string.Empty);
            }

            return new GitProbeResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
        }
        finally
        {
            if (disposeProcess)
            {
                process.Dispose();
            }
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    internal static void TryKillProcessTree(
        Process process,
        Action<Process>? killProcessTree = null)
    {
        try
        {
            if (!process.HasExited)
            {
                killProcessTree ??= static target => target.Kill(entireProcessTree: true);
                killProcessTree(process);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or NotSupportedException
                                   or AggregateException)
        {
        }
    }

    private static Task CreateCleanupTask(
        Process process,
        Task completion,
        Task processExit,
        Task stdout,
        Task stderr)
    {
        Task finalExit;
        try
        {
            finalExit = process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            finalExit = Task.CompletedTask;
        }

        return Task.WhenAll(
            ObserveCleanupTaskAsync(completion),
            ObserveCleanupTaskAsync(processExit),
            ObserveCleanupTaskAsync(stdout),
            ObserveCleanupTaskAsync(stderr),
            ObserveCleanupTaskAsync(finalExit));
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

    private static async Task DisposeAfterCleanupAsync(Process process, Task cleanup)
    {
        await Task.Yield();
        TryCloseRedirectedStreams(process);
        try
        {
            process.Dispose();
        }
        catch (Exception)
        {
        }

        await cleanup.ConfigureAwait(false);
    }

    private static void TryCloseRedirectedStreams(Process process)
    {
        try
        {
            process.StandardOutput.BaseStream.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            process.StandardError.BaseStream.Dispose();
        }
        catch (Exception)
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
}
