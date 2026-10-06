using System.Text.RegularExpressions;
using Beutl.Configuration;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitInstallationLocator
{
    private static readonly TimeSpan s_defaultDiscoveryTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan DiscoveryCacheLifetime = TimeSpan.FromSeconds(5);

    public static readonly Version MinimumVersion = new(2, 36);

    private readonly VersionControlConfig _config;
    private readonly TimeSpan _discoveryTimeout;
    private readonly IGitInstallationProbe _probe;
    private readonly GitHostPlatform _platform;
    private readonly TimeProvider _timeProvider;
    private readonly object _cacheGate = new();
    private readonly Dictionary<DiscoveryKey, DiscoveryFlight> _discoveries = [];
    private DiscoveryKey? _cachedKey;
    private GitAvailability? _cachedAvailability;
    private long _cachedAt;

    private sealed record DiscoveryKey(
        string? Executable, string? Path, string? PathExtensions, string? ProgramFiles,
        string? DeveloperDirectory, string? Toolchains, string WorkingDirectory);

    private sealed class DiscoveryFlight
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<GitAvailability> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters { get; set; }
    }

    public GitInstallationLocator(VersionControlConfig config)
        : this(config, ProcessGitInstallationProbe.Instance, GetCurrentPlatform())
    {
    }

    internal GitInstallationLocator(
        VersionControlConfig config,
        IGitInstallationProbe probe,
        GitHostPlatform platform)
        : this(config, probe, platform, s_defaultDiscoveryTimeout)
    {
    }

    internal GitInstallationLocator(
        VersionControlConfig config,
        IGitInstallationProbe probe,
        GitHostPlatform platform,
        TimeSpan discoveryTimeout,
        TimeProvider? timeProvider = null)
    {
        if (discoveryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryTimeout));
        }

        _config = config ?? throw new ArgumentNullException(nameof(config));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _platform = platform;
        _discoveryTimeout = discoveryTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal VersionControlConfig Config => _config;

    public async Task<GitAvailability> LocateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiscoveryKey key = GetDiscoveryKey();
        DiscoveryFlight flight;
        bool startDiscovery = false;
        lock (_cacheGate)
        {
            if (_cachedKey == key && _cachedAvailability is { } cached
                && _timeProvider.GetElapsedTime(_cachedAt) < DiscoveryCacheLifetime)
            {
                return cached;
            }

            if (!_discoveries.TryGetValue(key, out flight!))
            {
                flight = new DiscoveryFlight();
                _discoveries.Add(key, flight);
                startDiscovery = true;
            }
            flight.Waiters++;
        }

        if (startDiscovery)
        {
            _ = CompleteDiscoveryAsync(key, flight);
        }
        try
        {
            return await flight.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        finally
        {
            bool cancel = false;
            lock (_cacheGate)
            {
                flight.Waiters--;
                if (flight.Waiters == 0 && !flight.Completion.Task.IsCompleted)
                {
                    RemoveFlightLocked(key, flight);
                    cancel = true;
                }
            }
            if (cancel)
            {
                // A caller owns only its wait. Cancel the probe once nobody needs its result.
                try { flight.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                // The last owner also joins probe cleanup so coordinator disposal cannot
                // finish while its discovery is still shutting down.
                try { await flight.Completion.Task.ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
    }

    private async Task CompleteDiscoveryAsync(DiscoveryKey key, DiscoveryFlight flight)
    {
        try
        {
            (GitAvailability availability, bool cacheable) =
                await DiscoverAsync(key, flight.Cancellation.Token).ConfigureAwait(false);
            lock (_cacheGate)
            {
                if (cacheable && flight.Waiters > 0 && !flight.Cancellation.IsCancellationRequested
                    && _discoveries.GetValueOrDefault(key) == flight && key == GetDiscoveryKey())
                {
                    _cachedKey = key;
                    _cachedAvailability = availability;
                    _cachedAt = _timeProvider.GetTimestamp();
                }
                RemoveFlightLocked(key, flight);
                flight.Completion.TrySetResult(availability);
            }
        }
        catch (OperationCanceledException ex)
        {
            lock (_cacheGate)
            {
                RemoveFlightLocked(key, flight);
                flight.Completion.TrySetCanceled(ex.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            lock (_cacheGate)
            {
                RemoveFlightLocked(key, flight);
                flight.Completion.TrySetException(ex);
                // All waiters may already have canceled.
                _ = flight.Completion.Task.Exception;
            }
        }
        finally
        {
            flight.Cancellation.Dispose();
        }
    }

    // Forgets the flight only while it is still the one registered for its key. Callers hold _cacheGate.
    private void RemoveFlightLocked(DiscoveryKey key, DiscoveryFlight flight)
    {
        if (_discoveries.GetValueOrDefault(key) == flight) _discoveries.Remove(key);
    }

    private async Task<(GitAvailability Availability, bool Cacheable)> DiscoverAsync(
        DiscoveryKey key, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_discoveryTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        CancellationToken discoveryToken = linkedCts.Token;
        GitAvailability timeoutFallback = GitAvailability.NotInstalled;
        try
        {
            IReadOnlyList<string> candidates = await GetCandidatesAsync(key.Executable, discoveryToken).ConfigureAwait(false);
            discoveryToken.ThrowIfCancellationRequested();
            GitAvailability? oldestSupportedFailure = null;

            StringComparer candidateComparer = _platform == GitHostPlatform.Windows
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            foreach (string discoveredCandidate in candidates.Distinct(candidateComparer))
            {
                discoveryToken.ThrowIfCancellationRequested();
                string candidate = discoveredCandidate;
                if (_platform == GitHostPlatform.MacOS && key.Executable is null && IsMacSystemGit(candidate))
                {
                    candidate = await ResolveMacSystemGitAsync(candidate, discoveryToken).ConfigureAwait(false);
                }
                GitProbeResult result = await _probe.RunAsync(
                    candidate,
                    ["--version"],
                    discoveryToken).ConfigureAwait(false);
                discoveryToken.ThrowIfCancellationRequested();
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
                    timeoutFallback = oldestSupportedFailure;
                    continue;
                }

                var installedWithoutLfs = new GitAvailability(
                    GitAvailabilityState.Installed,
                    candidate,
                    version,
                    LfsInstalled: false);
                timeoutFallback = installedWithoutLfs;
                GitProbeResult lfs = await _probe.RunAsync(
                    candidate,
                    ["lfs", "version"],
                    discoveryToken).ConfigureAwait(false);
                discoveryToken.ThrowIfCancellationRequested();
                GitAvailability availability = installedWithoutLfs with { LfsInstalled = lfs.ExitCode == 0 };
                // Only a confirmed LFS install or absence is reusable.
                return (availability, lfs.ExitCode is 0 or 1);
            }

            discoveryToken.ThrowIfCancellationRequested();
            return (oldestSupportedFailure ?? GitAvailability.NotInstalled, false);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (timeoutFallback, false);
        }
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

    private DiscoveryKey GetDiscoveryKey()
    {
        string? configured = _config.GitExecutablePath;
        return new DiscoveryKey(
            string.IsNullOrWhiteSpace(configured) ? null : Path.GetFullPath(configured),
            _probe.GetEnvironmentVariable("PATH"),
            _probe.GetEnvironmentVariable("PATHEXT"),
            _probe.GetEnvironmentVariable("ProgramFiles"),
            _probe.GetEnvironmentVariable("DEVELOPER_DIR"),
            _probe.GetEnvironmentVariable("TOOLCHAINS"),
            Environment.CurrentDirectory);
    }

    private async Task<string> ResolveMacSystemGitAsync(string fallback, CancellationToken cancellationToken)
    {
        // GetCandidatesAsync only includes the system shim after checking that developer tools
        // are installed. Resolve its selected implementation once instead of launching the shim
        // for every repository command. An explicit executable override remains authoritative.
        GitProbeResult result = await _probe.RunAsync(
            "/usr/bin/xcrun", ["--find", "git"], cancellationToken).ConfigureAwait(false);
        string path = result.Stdout.Trim();
        return result.ExitCode == 0 && Path.IsPathFullyQualified(path) && !path.Any(char.IsControl)
               && _probe.FileExists(path)
            ? path : fallback;
    }

    private async Task<IReadOnlyList<string>> GetCandidatesAsync(string? configuredPath, CancellationToken cancellationToken)
    {
        if (configuredPath is not null)
        {
            return [configuredPath];
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
        // Candidate paths are already absolute/normalized. Do not normalize a macOS path with
        // the host's path rules when the discovery platform is supplied by a test.
        => string.Equals(path, "/usr/bin/git", StringComparison.Ordinal);

    private static GitHostPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS()) return GitHostPlatform.MacOS;
        if (OperatingSystem.IsWindows()) return GitHostPlatform.Windows;
        return GitHostPlatform.Linux;
    }

    [GeneratedRegex(@"git version (?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex GitVersionRegex();
}

internal enum GitHostPlatform
{
    Windows,
    MacOS,
    Linux,
}
