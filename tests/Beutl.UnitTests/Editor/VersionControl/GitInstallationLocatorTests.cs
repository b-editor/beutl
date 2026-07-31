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

    private sealed class FakeProbe : IGitInstallationProbe
    {
        public IReadOnlyList<string> Paths { get; init; } = [];

        public bool MacCommandLineToolsInstalled { get; init; }

        public HashSet<string> ExistingFiles { get; init; } = [];

        public Dictionary<(string Executable, string Arguments), GitProbeResult> Results { get; } = [];

        public List<(string Executable, string Arguments)> RunCalls { get; } = [];

        public int FindOnPathCalls { get; private set; }

        public Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            FindOnPathCalls++;
            return Task.FromResult(Paths);
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
            => Task.FromResult(MacCommandLineToolsInstalled);

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            string joined = string.Join(' ', arguments);
            RunCalls.Add((executablePath, joined));
            return Task.FromResult(Results.GetValueOrDefault(
                (executablePath, joined),
                new GitProbeResult(1, "", "not found")));
        }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public string? GetEnvironmentVariable(string name) => null;
    }
}
