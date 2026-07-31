using System.Diagnostics;
using System.Text.RegularExpressions;
using Beutl.Configuration;

namespace Beutl.Editor.VersionControl;

public sealed partial class GitInstallationLocator
{
    public static readonly Version MinimumVersion = new(2, 23);

    private readonly VersionControlConfig _config;
    private readonly IGitInstallationProbe _probe;
    private readonly GitHostPlatform _platform;

    public GitInstallationLocator(VersionControlConfig config)
        : this(config, ProcessGitInstallationProbe.Instance, GetCurrentPlatform())
    {
    }

    internal GitInstallationLocator(
        VersionControlConfig config,
        IGitInstallationProbe probe,
        GitHostPlatform platform)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _platform = platform;
    }

    internal VersionControlConfig Config => _config;

    public async Task<GitAvailability> LocateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> candidates = await GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
        GitAvailability? oldestSupportedFailure = null;

        foreach (string candidate in candidates.Distinct(PathComparer))
        {
            GitProbeResult result = await _probe.RunAsync(
                candidate,
                ["--version"],
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || !TryParseVersion(result.Stdout, out Version? version))
            {
                continue;
            }

            if (version < MinimumVersion)
            {
                oldestSupportedFailure ??= new GitAvailability(
                    GitAvailabilityState.VersionTooOld,
                    candidate,
                    version,
                    LfsInstalled: false);
                continue;
            }

            GitProbeResult lfs = await _probe.RunAsync(
                candidate,
                ["lfs", "version"],
                cancellationToken).ConfigureAwait(false);
            return new GitAvailability(
                GitAvailabilityState.Installed,
                candidate,
                version,
                LfsInstalled: lfs.ExitCode == 0);
        }

        return oldestSupportedFailure ?? GitAvailability.NotInstalled;
    }

    internal static bool TryParseVersion(string output, out Version? version)
    {
        Match match = GitVersionRegex().Match(output);
        if (!match.Success)
        {
            version = null;
            return false;
        }

        version = new Version(
            int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    private async Task<IReadOnlyList<string>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_config.GitExecutablePath))
        {
            return [Path.GetFullPath(_config.GitExecutablePath)];
        }

        var candidates = new List<string>();
        switch (_platform)
        {
            case GitHostPlatform.MacOS:
                {
                    bool commandLineToolsInstalled
                        = await _probe.HasMacCommandLineToolsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (string path in await _probe.FindOnPathAsync("git", cancellationToken).ConfigureAwait(false))
                    {
                        if (!IsMacSystemGit(path) || commandLineToolsInstalled)
                        {
                            candidates.Add(path);
                        }
                    }

                    if (commandLineToolsInstalled && _probe.FileExists("/usr/bin/git"))
                    {
                        candidates.Add("/usr/bin/git");
                    }

                    AddIfExists(candidates, "/opt/homebrew/bin/git");
                    AddIfExists(candidates, "/usr/local/bin/git");
                    break;
                }

            case GitHostPlatform.Windows:
                candidates.AddRange(await _probe.FindOnPathAsync("git", cancellationToken).ConfigureAwait(false));
                string? programFiles = _probe.GetEnvironmentVariable("ProgramFiles");
                if (!string.IsNullOrWhiteSpace(programFiles))
                {
                    AddIfExists(candidates, Path.Combine(programFiles, "Git", "cmd", "git.exe"));
                }
                break;

            default:
                candidates.AddRange(await _probe.FindOnPathAsync("git", cancellationToken).ConfigureAwait(false));
                break;
        }

        return candidates;
    }

    private void AddIfExists(List<string> candidates, string path)
    {
        if (_probe.FileExists(path))
        {
            candidates.Add(path);
        }
    }

    private static bool IsMacSystemGit(string path)
        => string.Equals(Path.GetFullPath(path), "/usr/bin/git", StringComparison.Ordinal);

    private static GitHostPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS()) return GitHostPlatform.MacOS;
        if (OperatingSystem.IsWindows()) return GitHostPlatform.Windows;
        return GitHostPlatform.Linux;
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [GeneratedRegex(@"git version (?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex GitVersionRegex();
}

internal enum GitHostPlatform
{
    Windows,
    MacOS,
    Linux,
}

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
    public static ProcessGitInstallationProbe Instance { get; } = new();

    public async Task<IReadOnlyList<string>> FindOnPathAsync(
        string executableName,
        CancellationToken cancellationToken)
    {
        string locator = OperatingSystem.IsWindows() ? "where.exe" : "which";
        GitProbeResult result = await RunAsync(locator, [executableName], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(File.Exists)
            .ToArray();
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

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                throw;
            }

            return new GitProbeResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new GitProbeResult(-1, string.Empty, string.Empty);
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    internal static void TryKillProcessTree(Process process)
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
