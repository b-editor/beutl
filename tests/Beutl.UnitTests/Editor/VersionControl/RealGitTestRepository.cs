using System.ComponentModel;
using System.Diagnostics;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

public abstract class RealGitTestRepository
{
    private readonly List<string> _additionalTemporaryDirectories = [];

    protected static readonly IReadOnlyDictionary<string, string> IsolatedGitEnvironment
        = new Dictionary<string, string>
        {
            ["GIT_CONFIG_GLOBAL"] = "/dev/null",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_AUTHOR_DATE"] = "2026-01-02T03:04:05Z",
            ["GIT_COMMITTER_DATE"] = "2026-01-02T03:04:05Z",
        };

    protected string Root { get; private set; } = null!;

    protected string GitPath { get; private set; } = "git";

    protected RepositoryInfo Repository { get; private set; } = null!;

    private protected GitCliRunner Runner { get; private set; } = null!;

    [SetUp]
    public async Task SetUpRealGitRepository()
    {
        GitPath = ProbeGitOrIgnore();
        Root = Path.Combine(Path.GetTempPath(), $"beutl-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        Repository = new RepositoryInfo(Root, Root);
        Runner = CreateRunner();
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("config", "user.name", "Beutl Test");
        await RunGitAsync("config", "user.email", "beutl-test@example.invalid");
        await RunGitAsync("config", "commit.gpgsign", "false");
    }

    [TearDown]
    public void TearDownRealGitRepository()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        foreach (string directory in _additionalTemporaryDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        _additionalTemporaryDirectories.Clear();
    }

    private protected GitCliRunner CreateRunner(TimeSpan? timeout = null)
        => new(GitPath, timeout ?? TimeSpan.FromSeconds(10), IsolatedGitEnvironment);

    protected string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-extra-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _additionalTemporaryDirectories.Add(directory);
        return directory;
    }

    private protected Task<GitCommandResult> RunGitAsync(params string[] arguments)
        => Runner.RunAsync(Repository, arguments, networkOperation: false, CancellationToken.None);

    protected async Task CommitFileAsync(string relativePath, string contents, string message)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
        await RunGitAsync("add", "--", relativePath.Replace('\\', '/'));
        await RunGitAsync("commit", "-m", message);
    }

    protected GitInstallationLocator CreateInstalledLocator(
        bool lfsInstalled = false,
        Beutl.Configuration.VersionControlConfig? config = null)
    {
        return new GitInstallationLocator(
            config ?? new Beutl.Configuration.VersionControlConfig(),
            new InstalledGitProbe(GitPath, lfsInstalled),
            GitHostPlatform.Linux);
    }

    private static string ProbeGitOrIgnore()
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--version");
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Assert.Ignore("git is not available on this machine.");
                return "git";
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Assert.Ignore("git is not available on this machine.");
            }

            return "git";
        }
        catch (Win32Exception)
        {
            Assert.Ignore("git is not available on this machine.");
            return "git";
        }
    }

    private sealed class InstalledGitProbe(string gitPath, bool lfsInstalled) : IGitInstallationProbe
    {
        public Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([gitPath]);

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            string joined = string.Join(' ', arguments);
            return Task.FromResult(joined switch
            {
                "--version" => new GitProbeResult(0, "git version 2.50.0", ""),
                "lfs version" when lfsInstalled => new GitProbeResult(0, "git-lfs/3.7.0", ""),
                _ => new GitProbeResult(1, "", "git-lfs unavailable"),
            });
        }

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
    }
}
