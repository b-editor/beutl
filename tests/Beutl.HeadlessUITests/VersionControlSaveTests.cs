using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlSaveTests
{
    [AvaloniaTest]
    public async Task Explicit_save_creates_one_snapshot_and_a_second_clean_save_creates_none()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnSave = true;
            config.AutoCommitOnClose = true;
            config.UseLfsWhenAvailable = false;

            string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "version-control-save");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tracked",
                location))!;
            HeadlessTestHelpers.Settle();

            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                async service =>
                {
                    await service.SetLocalIdentityAsync(
                        new GitIdentity("Beutl Headless Test", "headless@example.invalid"),
                        CancellationToken.None);
                    return true;
                });
            Assert.That(initialized, Is.True);

            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            Assert.That(await CountCommitsAsync(gitPath, projectRoot), Is.EqualTo(1));

            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.That(
                editor.GetService(typeof(IProjectVersionControlService)),
                Is.SameAs(TestShell.VersionControl.CurrentService));

            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            adder.AddElement(new ElementDescription(
                Start: TimeSpan.Zero,
                Length: TimeSpan.FromSeconds(1),
                Layer: 0,
                EngineObjectFactory: () => new RectShape()));
            HeadlessTestHelpers.Settle();

            await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            int afterFirstSave = await CountCommitsAsync(gitPath, projectRoot);
            int saveSnapshotsAfterFirstSave = await CountSaveSnapshotsAsync(gitPath, projectRoot);

            await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            int afterSecondSave = await CountCommitsAsync(gitPath, projectRoot);
            int saveSnapshotsAfterSecondSave = await CountSaveSnapshotsAsync(gitPath, projectRoot);

            Assert.Multiple(() =>
            {
                Assert.That(afterFirstSave, Is.EqualTo(2));
                Assert.That(saveSnapshotsAfterFirstSave, Is.EqualTo(1));
                Assert.That(afterSecondSave, Is.EqualTo(2));
                Assert.That(saveSnapshotsAfterSecondSave, Is.EqualTo(1));
            });

            await File.WriteAllTextAsync(Path.Combine(projectRoot, "close-marker.txt"), "close\n");
            await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();
            int afterClose = await CountCommitsAsync(gitPath, projectRoot);
            int closeSnapshots = await CountCloseSnapshotsAsync(gitPath, projectRoot);

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(afterClose, Is.EqualTo(3));
                Assert.That(closeSnapshots, Is.EqualTo(1));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Identity_view_model_prefills_the_os_user_and_writes_the_repository_identity()
    {
        var service = new RecordingVersionControlService();
        var viewModel = new GitIdentityDialogViewModel(service);

        Assert.That(viewModel.Name.Value, Is.EqualTo(Environment.UserName));
        viewModel.Email.Value = "local@example.invalid";
        await viewModel.SaveAsync();

        Assert.That(
            service.Identity,
            Is.EqualTo(new GitIdentity(Environment.UserName, "local@example.invalid")));
    }

    private static int CountSaveSnapshots(string log)
    {
        const string trailer = "Beutl-Snapshot: save";
        int count = 0;
        int index = 0;
        while ((index = log.IndexOf(trailer, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += trailer.Length;
        }

        return count;
    }

    private static async Task<int> CountCommitsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "rev-list", "--count", "HEAD");
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSaveSnapshotsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "log", "--format=%B");
        return CountSaveSnapshots(output);
    }

    private static async Task<int> CountCloseSnapshotsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "log", "--format=%B");
        return CountOccurrences(output, "Beutl-Snapshot: close");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static async Task<string> RunGitAsync(
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

        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var process = Process.Start(startInfo)!;
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, stderr);
        return stdout;
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

            return process.StartInfo.FileName == "git"
                ? FindGitOnPath()
                : process.StartInfo.FileName;
        }
        catch (Win32Exception)
        {
            Assert.Ignore("git is not available on this machine.");
            return "git";
        }
    }

    private static string FindGitOnPath()
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

    private sealed class IsolatedGitEnvironment : IDisposable
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

    private sealed class RecordingVersionControlService : IProjectVersionControlService
    {
        public RepositoryInfo? Repository => null;

        public GitIdentity? Identity { get; private set; }

        public event EventHandler<WorkspaceStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
            => Task.FromResult(GitAvailability.NotInstalled);

        public Task InitializeAsync(InitOptions options, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => Task.FromResult<CommitResult>(new CommitResult.NoChanges());

        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceStatus(null, 0, 0, [], false));

        public Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken)
            => Task.FromResult(Identity);

        public Task SetLocalIdentityAsync(
            GitIdentity identity,
            CancellationToken cancellationToken)
        {
            Identity = identity;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
