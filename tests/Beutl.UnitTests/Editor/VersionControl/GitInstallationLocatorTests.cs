using System.Diagnostics;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class GitInstallationLocatorTests
{
    [Test]
    public async Task Override_path_is_authoritative_and_lfs_is_probed()
    {
        string overridePath = Path.GetFullPath(Path.Combine("custom", "git"));
        var config = new VersionControlConfig { GitExecutablePath = overridePath };
        var probe = new FakeProbe();
        probe.Results[(overridePath, "--version")] = new GitProbeResult(0, "git version 2.43.1\n", "");
        probe.Results[(overridePath, "lfs version")] = new GitProbeResult(0, "git-lfs/3.5.1", "");
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);

        GitAvailability result = await locator.LocateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(result.GitPath, Is.EqualTo(overridePath));
            Assert.That(result.Version, Is.EqualTo(new Version(2, 43, 1)));
            Assert.That(result.LfsInstalled, Is.True);
            Assert.That(probe.FindOnPathCalls, Is.Zero);
        });
    }

    [Test]
    public async Task Version_below_2_23_is_reported_as_too_old()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe { Paths = ["/usr/local/bin/git"] };
        probe.Results[("/usr/local/bin/git", "--version")]
            = new GitProbeResult(0, "git version 2.22.9", "");
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);

        GitAvailability result = await locator.LocateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.VersionTooOld));
            Assert.That(result.Version, Is.EqualTo(new Version(2, 22, 9)));
            Assert.That(result.LfsInstalled, Is.False);
        });
    }

    [Test]
    public async Task Mac_system_stub_is_skipped_without_command_line_tools()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe
        {
            Paths = ["/usr/bin/git"],
            MacCommandLineToolsInstalled = false,
            ExistingFiles = ["/usr/bin/git", "/opt/homebrew/bin/git"],
        };
        probe.Results[("/opt/homebrew/bin/git", "--version")]
            = new GitProbeResult(0, "git version 2.44.0", "");
        probe.Results[("/opt/homebrew/bin/git", "lfs version")]
            = new GitProbeResult(1, "", "git: 'lfs' is not a git command");
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.MacOS);

        GitAvailability result = await locator.LocateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(result.GitPath, Is.EqualTo("/opt/homebrew/bin/git"));
            Assert.That(probe.RunCalls.Select(x => x.Executable), Does.Not.Contain("/usr/bin/git"));
        });
    }

    [Test]
    public async Task Timed_out_version_probe_is_skipped_for_the_next_candidate()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe { Paths = ["/hanging/git", "/working/git"] };
        probe.Results[("/hanging/git", "--version")] = new GitProbeResult(-1, "", "");
        probe.Results[("/working/git", "--version")] = new GitProbeResult(0, "git version 2.50.0", "");
        probe.Results[("/working/git", "lfs version")] = new GitProbeResult(0, "git-lfs/3.7.0", "");
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);

        GitAvailability result = await locator.LocateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(result.GitPath, Is.EqualTo("/working/git"));
            Assert.That(result.LfsInstalled, Is.True);
        });
    }

    [Test]
    public async Task Timed_out_override_probe_is_reported_as_not_installed()
    {
        string overridePath = Path.GetFullPath(Path.Combine("hanging", "git"));
        var config = new VersionControlConfig { GitExecutablePath = overridePath };
        var probe = new FakeProbe();
        probe.Results[(overridePath, "--version")] = new GitProbeResult(-1, "", "");
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);

        GitAvailability result = await locator.LocateAsync();

        Assert.That(result, Is.EqualTo(GitAvailability.NotInstalled));
    }

    [Test]
    public async Task Timed_out_lfs_probe_does_not_hide_the_git_installation()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe { Paths = ["/working/git"] };
        probe.Results[("/working/git", "--version")] = new GitProbeResult(0, "git version 2.50.0", "");
        probe.Results[("/working/git", "lfs version")] = new GitProbeResult(-1, "", "");
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);

        GitAvailability result = await locator.LocateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(result.GitPath, Is.EqualTo("/working/git"));
            Assert.That(result.LfsInstalled, Is.False);
        });
    }

    [Test]
    public async Task Discovery_budget_stops_probing_additional_candidates()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe
        {
            Paths = ["/first/git", "/second/git", "/third/git"],
            RunDelay = TimeSpan.FromMilliseconds(100),
        };
        var locator = new GitInstallationLocator(
            config,
            probe,
            GitHostPlatform.Linux,
            TimeSpan.FromMilliseconds(150));

        GitAvailability result = await locator.LocateAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(GitAvailability.NotInstalled));
            Assert.That(probe.RunCalls.Select(x => x.Executable), Does.Not.Contain("/third/git"));
        });
    }

    [Test]
    public async Task Discovery_budget_preserves_git_found_before_lfs_timeout()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe { Paths = ["/working/git"] };
        probe.Results[("/working/git", "--version")] = new GitProbeResult(0, "git version 2.50.0", "");
        probe.Results[("/working/git", "lfs version")] = new GitProbeResult(0, "git-lfs/3.7.0", "");
        probe.RunDelays[("/working/git", "lfs version")] = TimeSpan.FromSeconds(1);
        var locator = new GitInstallationLocator(
            config,
            probe,
            GitHostPlatform.Linux,
            TimeSpan.FromMilliseconds(100));

        GitAvailability result = await locator.LocateAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(result.GitPath, Is.EqualTo("/working/git"));
            Assert.That(result.Version, Is.EqualTo(new Version(2, 50, 0)));
            Assert.That(result.LfsInstalled, Is.False);
        });
    }

    [Test]
    public async Task Discovery_budget_preserves_version_too_old_result()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe { Paths = ["/old/git", "/hanging/git"] };
        probe.Results[("/old/git", "--version")] = new GitProbeResult(0, "git version 2.22.9", "");
        probe.RunDelays[("/hanging/git", "--version")] = TimeSpan.FromSeconds(1);
        var locator = new GitInstallationLocator(
            config,
            probe,
            GitHostPlatform.Linux,
            TimeSpan.FromMilliseconds(100));

        GitAvailability result = await locator.LocateAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(GitAvailabilityState.VersionTooOld));
            Assert.That(result.GitPath, Is.EqualTo("/old/git"));
            Assert.That(result.Version, Is.EqualTo(new Version(2, 22, 9)));
            Assert.That(result.LfsInstalled, Is.False);
        });
    }

    [Test]
    public async Task Discovery_budget_covers_path_lookup()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe
        {
            Paths = ["/working/git"],
            FindOnPathDelay = TimeSpan.FromSeconds(1),
        };
        var locator = new GitInstallationLocator(
            config,
            probe,
            GitHostPlatform.Linux,
            TimeSpan.FromMilliseconds(100));

        GitAvailability result = await locator.LocateAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(GitAvailability.NotInstalled));
            Assert.That(probe.FindOnPathCalls, Is.EqualTo(1));
            Assert.That(probe.RunCalls, Is.Empty);
        });
    }

    [Test]
    public void Discovery_budget_preserves_external_cancellation()
    {
        var config = new VersionControlConfig();
        var probe = new FakeProbe
        {
            Paths = ["/hanging/git"],
            RunDelay = TimeSpan.FromSeconds(1),
        };
        var locator = new GitInstallationLocator(
            config,
            probe,
            GitHostPlatform.Linux,
            TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        OperationCanceledException? exception = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await locator.LocateAsync(cancellation.Token));

        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [TestCase("git version 2.39.5 (Apple Git-154)", 2, 39, 5)]
    [TestCase("git version 2.50.1.windows.1", 2, 50, 1)]
    public void Version_parser_accepts_platform_suffixes(
        string output,
        int major,
        int minor,
        int patch)
    {
        bool parsed = GitInstallationLocator.TryParseVersion(output, out Version? version);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(version, Is.EqualTo(new Version(major, minor, patch)));
        });
    }

    [Test]
    public void Cancellation_cleanup_ignores_a_process_without_an_active_association()
    {
        using var process = new Process();

        Assert.DoesNotThrow(() => ProcessGitInstallationProbe.TryKillProcessTree(process));
    }

    [Test]
    public void Cancellation_cleanup_ignores_partial_process_tree_kill_failures()
    {
        using Process process = Process.GetCurrentProcess();

        Assert.DoesNotThrow(() => ProcessGitInstallationProbe.TryKillProcessTree(
            process,
            static _ => throw new AggregateException("A descendant could not be terminated.")));
    }

    [Test]
    public async Task Process_probe_times_out_internally()
    {
        var probe = new ProcessGitInstallationProbe(TimeSpan.FromMilliseconds(250));
        (string executable, IReadOnlyList<string> arguments) = CreateShellCommand(
            "printf 'started'; printf 'error' >&2; sleep 60",
            "echo started & echo error 1>&2 & ping 127.0.0.1 -n 60 >nul");
        using var safetyCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        GitProbeResult result = await probe.RunAsync(
            executable,
            arguments,
            safetyCancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(-1));
            Assert.That(safetyCancellation.IsCancellationRequested, Is.False);
        });
    }

    [Test]
    public async Task Process_probe_returns_the_completed_process_result()
    {
        var probe = new ProcessGitInstallationProbe(TimeSpan.FromSeconds(5));
        (string executable, IReadOnlyList<string> arguments) = CreateShellCommand(
            "printf 'standard output'; printf 'standard error' >&2; exit 7",
            "echo standard output & echo standard error 1>&2 & exit /b 7");

        GitProbeResult result = await probe.RunAsync(executable, arguments, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(7));
            Assert.That(result.Stdout, Does.Contain("standard output"));
            Assert.That(result.Stderr, Does.Contain("standard error"));
        });
    }

    [Test]
    public void Process_probe_preserves_external_cancellation()
    {
        var probe = new ProcessGitInstallationProbe(TimeSpan.FromSeconds(5));
        (string executable, IReadOnlyList<string> arguments) = CreateShellCommand(
            "sleep 60",
            "ping 127.0.0.1 -n 60 >nul");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        OperationCanceledException? exception = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await probe.RunAsync(executable, arguments, cancellation.Token));

        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task Process_probe_timeout_terminates_the_process_tree_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The portable Windows shell does not expose the spawned child process ID.");
        }

        string childPidPath = Path.Combine(Path.GetTempPath(), $"beutl-git-probe-{Guid.NewGuid():N}.pid");
        int childPid = 0;
        try
        {
            var probe = new ProcessGitInstallationProbe(TimeSpan.FromSeconds(1));
            string command = $"sleep 60 >/dev/null 2>&1 & child=$!; printf '%s' \"$child\" > {QuoteForPosixShell(childPidPath)}; wait";

            GitProbeResult result = await probe.RunAsync(
                "/bin/sh",
                ["-c", command],
                CancellationToken.None);

            Assert.That(result.ExitCode, Is.EqualTo(-1));
            Assert.That(File.Exists(childPidPath), Is.True);
            childPid = int.Parse(
                await File.ReadAllTextAsync(childPidPath),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(
                await WaitForProcessExitAsync(childPid, TimeSpan.FromSeconds(5)),
                Is.True,
                "The probe's child process was left running after the timeout.");
        }
        finally
        {
            TryKillProcess(childPid);
            File.Delete(childPidPath);
        }
    }

    [Test]
    public async Task Process_probe_timeout_bounds_readers_held_by_an_exited_wrapper()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The portable Windows shell does not expose the spawned child process ID.");
        }

        string childPidPath = Path.Combine(Path.GetTempPath(), $"beutl-git-probe-{Guid.NewGuid():N}.pid");
        string childSucceededPath = Path.Combine(Path.GetTempPath(), $"beutl-git-probe-{Guid.NewGuid():N}.success");
        int childPid = 0;
        Task<GitProbeResult>? probeTask = null;
        try
        {
            var probe = new ProcessGitInstallationProbe(TimeSpan.FromMilliseconds(250));
            string command = $"(sleep 5; /bin/echo child-output && printf success > {QuoteForPosixShell(childSucceededPath)}) & child=$!; printf '%s' \"$child\" > {QuoteForPosixShell(childPidPath)}; exit 0";
            var stopwatch = Stopwatch.StartNew();

            probeTask = probe.RunAsync("/bin/sh", ["-c", command], CancellationToken.None);
            GitProbeResult result = await probeTask.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(-1));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
            });
            Assert.That(File.Exists(childPidPath), Is.True);
            childPid = int.Parse(
                await File.ReadAllTextAsync(childPidPath),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(
                await WaitForProcessExitAsync(childPid, TimeSpan.FromSeconds(10)),
                Is.True,
                "The descendant did not exit after its inherited pipe was closed.");
            Assert.That(
                File.Exists(childSucceededPath),
                Is.False,
                "The descendant retained a writable inherited pipe after the probe returned.");
        }
        finally
        {
            if (File.Exists(childPidPath))
            {
                childPid = int.Parse(
                    await File.ReadAllTextAsync(childPidPath),
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            TryKillProcess(childPid);
            File.Delete(childPidPath);
            File.Delete(childSucceededPath);
            if (probeTask is not null)
            {
                await probeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static (string Executable, IReadOnlyList<string> Arguments) CreateShellCommand(
        string unixCommand,
        string windowsCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/s", "/c", windowsCommand]);
        }

        return ("/bin/sh", ["-c", unixCommand]);
    }

    private static string QuoteForPosixShell(string value)
        => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            try
            {
                await process.WaitForExitAsync().WaitAsync(timeout);
                return true;
            }
            catch (TimeoutException)
            {
                return process.HasExited;
            }
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void TryKillProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            ProcessGitInstallationProbe.TryKillProcessTree(process);
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed class FakeProbe : IGitInstallationProbe
    {
        public IReadOnlyList<string> Paths { get; init; } = [];

        public TimeSpan FindOnPathDelay { get; init; }

        public TimeSpan RunDelay { get; init; }

        public bool MacCommandLineToolsInstalled { get; init; }

        public HashSet<string> ExistingFiles { get; init; } = [];

        public Dictionary<(string Executable, string Arguments), GitProbeResult> Results { get; } = [];

        public Dictionary<(string Executable, string Arguments), TimeSpan> RunDelays { get; } = [];

        public List<(string Executable, string Arguments)> RunCalls { get; } = [];

        public int FindOnPathCalls { get; private set; }

        public async Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            FindOnPathCalls++;
            if (FindOnPathDelay > TimeSpan.Zero)
            {
                await Task.Delay(FindOnPathDelay, cancellationToken);
            }

            return Paths;
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
            => Task.FromResult(MacCommandLineToolsInstalled);

        public async Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            string joined = string.Join(' ', arguments);
            RunCalls.Add((executablePath, joined));
            TimeSpan delay = RunDelays.GetValueOrDefault((executablePath, joined), RunDelay);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return Results.GetValueOrDefault(
                (executablePath, joined),
                new GitProbeResult(1, "", "not found"));
        }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public string? GetEnvironmentVariable(string name) => null;
    }
}
