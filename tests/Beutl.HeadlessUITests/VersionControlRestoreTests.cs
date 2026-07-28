using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlRestoreTests
{
    private const string RestoreStateKey = "version-control-restore-state";

    [AvaloniaTest]
    public async Task Manual_commit_requests_repository_identity_and_skips_a_clean_second_commit()
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

            (Project project, _) = await CreateTrackedProjectAsync("version-control-manual");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            await RunGitAsync(gitPath, projectRoot, "config", "--unset", "user.name");
            await RunGitAsync(gitPath, projectRoot, "config", "--unset", "user.email");
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "manual-marker.txt"),
                "manual version\n");

            int identityRequests = 0;
            TestShell.VersionControl.RequestIdentityAsync = async service =>
            {
                identityRequests++;
                await service.SetLocalIdentityAsync(
                    new GitIdentity("Manual Commit Test", "manual@example.invalid"),
                    CancellationToken.None);
                return true;
            };

            CommitResult first = await TestShell.VersionControl.CommitManualAsync("rough cut");
            CommitResult second = await TestShell.VersionControl.CommitManualAsync("clean retry");
            CommitInfo manual = (await TestShell.VersionControl.CurrentService!.GetHistoryAsync(
                    0,
                    1,
                    CancellationToken.None))
                .Single();

            Assert.Multiple(() =>
            {
                Assert.That(identityRequests, Is.EqualTo(1));
                Assert.That(first, Is.TypeOf<CommitResult.Committed>());
                Assert.That(second, Is.TypeOf<CommitResult.NoChanges>());
                Assert.That(manual.Subject, Is.EqualTo("rough cut"));
                Assert.That(manual.Kind, Is.EqualTo(SnapshotKind.Manual));
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
    public async Task Branch_cycle_saves_dirty_state_reopens_the_selected_branch_and_recovers_from_failure()
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

            (Project project, _) = await CreateTrackedProjectAsync("version-control-branch");
            project.Variables[RestoreStateKey] = "before-switch";
            CoreSerializer.StoreToUri(project, project.Uri!);
            Assert.That(
                (await TestShell.VersionControl.CurrentService!.GetStatusAsync(
                    CancellationToken.None)).IsClean,
                Is.False);

            int confirmations = 0;
            TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            Assert.That(
                await TestShell.VersionControl.CreateBranchAsync("experiment"),
                Is.True);
            HeadlessTestHelpers.Settle();

            Project experimentProject = TestShell.Project.CurrentProject.Value!;
            WorkspaceStatus experimentStatus =
                await TestShell.VersionControl.CurrentService!.GetStatusAsync(
                    CancellationToken.None);
            IReadOnlyList<CommitInfo> experimentHistory =
                await TestShell.VersionControl.CurrentService.GetHistoryAsync(
                    0,
                    20,
                    CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(experimentProject, Is.Not.SameAs(project));
                Assert.That(experimentStatus.Branch, Is.EqualTo("experiment"));
                Assert.That(
                    experimentProject.Variables[RestoreStateKey],
                    Is.EqualTo("before-switch"));
                Assert.That(
                    experimentHistory.Any(commit =>
                        commit.Kind == SnapshotKind.Safety
                        && commit.Subject == "beutl: safety snapshot before switch"),
                    Is.True);
            });

            experimentProject.Variables[RestoreStateKey] = "experiment-only";
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            Assert.That(
                await TestShell.VersionControl.SwitchBranchAsync("main"),
                Is.True);
            HeadlessTestHelpers.Settle();

            Project mainProject = TestShell.Project.CurrentProject.Value!;
            WorkspaceStatus mainStatus =
                await TestShell.VersionControl.CurrentService!.GetStatusAsync(
                    CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(mainStatus.Branch, Is.EqualTo("main"));
                Assert.That(
                    mainProject.Variables[RestoreStateKey],
                    Is.EqualTo("before-switch"));
                Assert.That(confirmations, Is.EqualTo(2));
            });

            Assert.That(
                await TestShell.VersionControl.SwitchBranchAsync("missing-branch"),
                Is.False);
            HeadlessTestHelpers.Settle();
            WorkspaceStatus recoveredStatus =
                await TestShell.VersionControl.CurrentService!.GetStatusAsync(
                    CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(recoveredStatus.Branch, Is.EqualTo("main"));
                Assert.That(
                    TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                    Is.EqualTo("before-switch"));
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
    public async Task Pull_cycle_records_dirty_safety_snapshot_and_reopens_fast_forwarded_state()
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

            (Project project, _) = await CreateTrackedProjectAsync("version-control-pull");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            string remoteRoot = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-pull-remote.git");
            await RunGitAsync(
                gitPath,
                projectRoot,
                "init",
                "--bare",
                "-b",
                "main",
                remoteRoot);
            await TestShell.VersionControl.SetRemoteAsync(remoteRoot);
            Assert.That(
                await TestShell.VersionControl.PushAsync(progress: null),
                Is.TypeOf<RemoteOpResult.Success>());

            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "local-marker.txt"),
                "local safety state\n");
            TestShell.VersionControl.ConfirmPullAsync = _ => Task.FromResult(true);
            Assert.That(
                await TestShell.VersionControl.PullAsync(),
                Is.TypeOf<RemoteOpResult.Success>());
            HeadlessTestHelpers.Settle();

            IReadOnlyList<CommitInfo> safetyHistory =
                await TestShell.VersionControl.CurrentService!.GetHistoryAsync(
                    0,
                    20,
                    CancellationToken.None);
            Assert.That(
                safetyHistory.Any(commit =>
                    commit.Kind == SnapshotKind.Safety
                    && commit.Subject == "beutl: safety snapshot before pull"),
                Is.True);
            Assert.That(
                await TestShell.VersionControl.PushAsync(progress: null),
                Is.TypeOf<RemoteOpResult.Success>());

            string peerRoot = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-pull-peer");
            await RunGitAsync(
                gitPath,
                projectRoot,
                "clone",
                "--branch",
                "main",
                remoteRoot,
                peerRoot);
            await RunGitAsync(
                gitPath,
                peerRoot,
                "config",
                "user.name",
                "Beutl Headless Peer");
            await RunGitAsync(
                gitPath,
                peerRoot,
                "config",
                "user.email",
                "headless-peer@example.invalid");
            await File.WriteAllTextAsync(
                Path.Combine(peerRoot, "remote-marker.txt"),
                "remote state\n");
            await RunGitAsync(gitPath, peerRoot, "add", "--", "remote-marker.txt");
            await RunGitAsync(gitPath, peerRoot, "commit", "-m", "remote update");
            await RunGitAsync(gitPath, peerRoot, "push");

            Project beforePull = TestShell.Project.CurrentProject.Value!;
            Assert.That(
                await TestShell.VersionControl.PullAsync(),
                Is.TypeOf<RemoteOpResult.Success>());
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.SameAs(beforePull));
                Assert.That(
                    File.ReadAllText(Path.Combine(projectRoot, "remote-marker.txt")),
                    Is.EqualTo("remote state\n"));
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
    public async Task Restore_reopens_exact_state_preserves_safety_snapshot_and_supports_a_new_branch()
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

            (Project project, EditViewModel editor) = await CreateTrackedProjectAsync();
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            IProjectVersionControlService service = TestShell.VersionControl.CurrentService!;

            project.Variables[RestoreStateKey] = "version-one";
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            CommitInfo target = (await service.GetHistoryAsync(0, 10, CancellationToken.None))
                .First(commit => commit.Kind == SnapshotKind.Save);

            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            AddRectangle(adder, layer: 0);
            project.Variables[RestoreStateKey] = "version-two";
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

            AddRectangle(adder, layer: 1);
            project.Variables[RestoreStateKey] = "pre-restore";
            CoreSerializer.StoreToUri(project, project.Uri!);
            Scene preRestoreScene = project.Items.OfType<Scene>().Single();
            CoreSerializer.StoreToUri(preRestoreScene, preRestoreScene.Uri!);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(editor.HistoryManager.CanUndo, Is.True);
                Assert.That(preRestoreScene.Children, Has.Count.EqualTo(2));
            });
            Assert.That((await service.GetStatusAsync(CancellationToken.None)).IsClean, Is.False);

            int confirmationCount = 0;
            TestShell.VersionControl.ConfirmRestoreAsync = _ =>
            {
                confirmationCount++;
                return Task.FromResult(true);
            };

            TestShell.Editor.NotifyOutputStarted();
            try
            {
                Assert.That(
                    await TestShell.VersionControl.RestoreAsync(target.Sha),
                    Is.False,
                    "A restore must not start while output is reading project files.");
                Assert.That(confirmationCount, Is.Zero);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            }
            finally
            {
                TestShell.Editor.NotifyOutputFinished();
            }

            Assert.That(await TestShell.VersionControl.RestoreAsync(target.Sha), Is.True);
            HeadlessTestHelpers.Settle();

            Project restoredProject = TestShell.Project.CurrentProject.Value!;
            Scene restoredScene = restoredProject.Items.OfType<Scene>().Single();
            var restoredEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.Multiple(() =>
            {
                Assert.That(confirmationCount, Is.EqualTo(1));
                Assert.That(restoredProject, Is.Not.SameAs(project));
                Assert.That(restoredProject.Variables[RestoreStateKey], Is.EqualTo("version-one"));
                Assert.That(restoredScene.Children, Is.Empty);
                Assert.That(restoredEditor.HistoryManager.CanUndo, Is.False);
            });

            service = TestShell.VersionControl.CurrentService!;
            IReadOnlyList<CommitInfo> history =
                await service.GetHistoryAsync(0, 20, CancellationToken.None);
            CommitInfo safety = history.Single(commit => commit.Kind == SnapshotKind.Safety);
            IReadOnlyList<FileChange> safetyFiles =
                await service.GetCommitFilesAsync(safety.Sha, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(history.Any(commit => commit.Kind == SnapshotKind.Restore), Is.True);
                Assert.That(history.Any(commit => commit.Sha == target.Sha), Is.True);
                Assert.That(
                    safety.Subject,
                    Is.EqualTo("beutl: safety snapshot before restore"));
                Assert.That(
                    history.Single(commit => commit.Kind == SnapshotKind.Restore).Subject,
                    Is.EqualTo($"beutl: restore project state from {target.ShortSha}"));
                Assert.That(
                    safetyFiles.Count(file => file.Path.EndsWith(".belm", StringComparison.Ordinal)),
                    Is.EqualTo(1));
            });

            Assert.That(await TestShell.VersionControl.RestoreAsync(safety.Sha), Is.True);
            HeadlessTestHelpers.Settle();

            Project recoveredProject = TestShell.Project.CurrentProject.Value!;
            Scene recoveredScene = recoveredProject.Items.OfType<Scene>().Single();
            string[] restoredElementFiles =
                Directory.GetFiles(projectRoot, "*.belm", SearchOption.AllDirectories);
            Assert.Multiple(() =>
            {
                Assert.That(
                    recoveredProject.Variables[RestoreStateKey],
                    Is.EqualTo("pre-restore"),
                    "The safety snapshot must remain reachable after a restore.");
                Assert.That(restoredElementFiles, Has.Length.EqualTo(2));
                Assert.That(
                    recoveredScene.Children,
                    Has.Count.EqualTo(2),
                    string.Join(Environment.NewLine, restoredElementFiles));
            });

            Assert.That(
                await TestShell.VersionControl.RestoreAsync("0000000000000000000000000000000000000000"),
                Is.False);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(
                    TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                    Is.EqualTo("pre-restore"),
                    "A failed restore must reopen the original project state.");
            });

            const string branchName = "restored-version";
            Assert.That(
                await TestShell.VersionControl.RestoreToNewBranchAsync(
                    target.Sha,
                    branchName),
                Is.True);
            HeadlessTestHelpers.Settle();

            Project branchedProject = TestShell.Project.CurrentProject.Value!;
            WorkspaceStatus branchStatus =
                await TestShell.VersionControl.CurrentService!.GetStatusAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(branchStatus.Branch, Is.EqualTo(branchName));
                Assert.That(branchedProject.Variables[RestoreStateKey], Is.EqualTo("version-one"));
                Assert.That(branchedProject.Items.OfType<Scene>().Single().Children, Is.Empty);
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

    private static async Task<(Project Project, EditViewModel Editor)> CreateTrackedProjectAsync(
        string directoryName = "version-control-restore")
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, directoryName);
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

        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        return (project, editor);
    }

    private static void AddRectangle(IElementAdder adder, int layer)
    {
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: layer,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();
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

    private sealed class IsolatedGitEnvironment : IDisposable
    {
        private readonly string? _oldGlobal =
            Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        private readonly string? _oldNoSystem =
            Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");

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
}
