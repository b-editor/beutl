using System.Diagnostics;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

internal static class GitTestSupport
{
    // The git on PATH; the test is ignored when there is none.
    public static string FindGitOrIgnore()
    {
        string executable = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("git");
        using var process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0
            || output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is not { } path)
        {
            Assert.Ignore("git is not available on this machine.");
            return "git";
        }

        return path;
    }

    public static async Task<string> RunGitAsync(
        string gitPath,
        string repositoryRoot,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, stderr);
        return stdout;
    }

    // Initializes version control for the shell's open project with requestIdentityAsync as its identity
    // prompt, and puts the shell's own prompt back once that ends.
    public static async Task<bool> InitializeCurrentProjectAsync(
        Project project,
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync)
    {
        Func<CancellationToken, Task<GitIdentity?>> shellPrompt = TestShell.VersionControl.RequestIdentityAsync;
        TestShell.VersionControl.RequestIdentityAsync = requestIdentityAsync;
        try
        {
            return await TestShell.VersionControl.InitializeCurrentProjectAsync(project);
        }
        finally
        {
            TestShell.VersionControl.RequestIdentityAsync = shellPrompt;
        }
    }
}

internal sealed class IsolatedGitEnvironment : IDisposable
{
    private readonly string? _oldGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
    private readonly string? _oldNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");

    public IsolatedGitEnvironment()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _oldGlobal);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", _oldNoSystem);
    }
}

// Points the shell's version control at the git on PATH, away from the user's git settings. Dispose
// resets the shell first and then puts the settings and the shell's prompts back.
internal sealed class RealGitScope : IAsyncDisposable
{
    private readonly IsolatedGitEnvironment _environment;
    private readonly VersionControlConfig _config = GlobalConfiguration.Instance.VersionControlConfig;
    private readonly string? _oldGitPath;
    private readonly bool _oldAutoCommitOnSave;
    private readonly bool _oldAutoCommitOnClose;
    private readonly bool _oldUseLfs;
    private readonly Func<CancellationToken, Task<bool>> _oldConfirmRestoreAsync;
    private readonly Func<string, CancellationToken, Task<bool>> _oldConfirmSwitchBranchAsync;
    private readonly Func<CancellationToken, Task<bool>> _oldConfirmPullAsync;
    private readonly Func<CancellationToken, Task<GitIdentity?>> _oldRequestIdentityAsync;

    private RealGitScope(IsolatedGitEnvironment environment, string gitPath)
    {
        _environment = environment;
        GitPath = gitPath;
        _oldGitPath = _config.GitExecutablePath;
        _oldAutoCommitOnSave = _config.AutoCommitOnSave;
        _oldAutoCommitOnClose = _config.AutoCommitOnClose;
        _oldUseLfs = _config.UseLfsWhenAvailable;
        _oldConfirmRestoreAsync = TestShell.VersionControl.ConfirmRestoreAsync;
        _oldConfirmSwitchBranchAsync = TestShell.VersionControl.ConfirmSwitchBranchAsync;
        _oldConfirmPullAsync = TestShell.VersionControl.ConfirmPullAsync;
        _oldRequestIdentityAsync = TestShell.VersionControl.RequestIdentityAsync;
    }

    public string GitPath { get; }

    // A setting left null keeps its current value.
    public static async Task<RealGitScope> StartAsync(
        bool? autoCommitOnSave = null,
        bool? autoCommitOnClose = null,
        bool? useLfsWhenAvailable = null)
    {
        await TestReset.ResetShellAsync();
        var environment = new IsolatedGitEnvironment();
        RealGitScope scope;
        try
        {
            scope = new RealGitScope(environment, GitTestSupport.FindGitOrIgnore());
        }
        catch
        {
            environment.Dispose();
            throw;
        }

        VersionControlConfig config = scope._config;
        config.GitExecutablePath = scope.GitPath;
        if (autoCommitOnSave is { } save)
        {
            config.AutoCommitOnSave = save;
        }

        if (autoCommitOnClose is { } close)
        {
            config.AutoCommitOnClose = close;
        }

        if (useLfsWhenAvailable is { } useLfs)
        {
            config.UseLfsWhenAvailable = useLfs;
        }

        return scope;
    }

    // Creates a project under BEUTL_HOME in the shell and, unless initialize is false, starts tracking
    // it with the headless test identity.
    public async Task<Project> CreateProjectAsync(
        string directoryName,
        string projectName,
        bool initialize = true,
        int width = 640,
        int height = 480)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, directoryName);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(
            width,
            height,
            30,
            44100,
            projectName,
            location))!;
        HeadlessTestHelpers.Settle();
        if (initialize)
        {
            bool initialized = await GitTestSupport.InitializeCurrentProjectAsync(
                project,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid")));
            Assert.That(initialized, Is.True);
        }

        return project;
    }

    public async ValueTask DisposeAsync()
    {
        TestShell.VersionControl.ConfirmRestoreAsync = _oldConfirmRestoreAsync;
        TestShell.VersionControl.ConfirmSwitchBranchAsync = _oldConfirmSwitchBranchAsync;
        TestShell.VersionControl.ConfirmPullAsync = _oldConfirmPullAsync;
        TestShell.VersionControl.RequestIdentityAsync = _oldRequestIdentityAsync;
        try
        {
            await TestReset.ResetShellAsync();
        }
        finally
        {
            _config.GitExecutablePath = _oldGitPath;
            _config.AutoCommitOnSave = _oldAutoCommitOnSave;
            _config.AutoCommitOnClose = _oldAutoCommitOnClose;
            _config.UseLfsWhenAvailable = _oldUseLfs;
            _environment.Dispose();
        }
    }
}
