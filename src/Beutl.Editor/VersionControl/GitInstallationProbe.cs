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
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        GitProcessEnvironment.ConfigureToolSearchPath(startInfo);

        try
        {
            (int exitCode, string stdout, string stderr) = await GitProcess.RunAsync(
                startInfo,
                standardInput: null,
                static (reader, token) => reader.ReadToEndAsync(token),
                static (reader, token) => reader.ReadToEndAsync(token),
                _timeout,
                cancellationToken).ConfigureAwait(false);
            return new GitProbeResult(exitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException)
        {
            return new GitProbeResult(-1, string.Empty, string.Empty);
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
