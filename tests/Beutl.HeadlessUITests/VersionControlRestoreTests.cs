using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
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
        var oldRequestIdentityAsync = TestShell.VersionControl.RequestIdentityAsync;

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
            TestShell.VersionControl.RequestIdentityAsync = _ =>
            {
                identityRequests++;
                return Task.FromResult<GitIdentity?>(
                    new GitIdentity("Manual Commit Test", "manual@example.invalid"));
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
            TestShell.VersionControl.RequestIdentityAsync = oldRequestIdentityAsync;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Disposal_during_a_lifecycle_operation_cleans_up_the_service_after_completion()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.GitExecutablePath = gitPath;
            config.UseLfsWhenAvailable = false;
            await CreateTrackedProjectAsync("version-control-coordinator-disposal");

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(TestShell.Project, editorService);
            await WaitUntilAsync(() => coordinator.CurrentService?.Repository is not null);
            coordinator.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmationStarted.TrySetResult();
                return releaseConfirmation.Task;
            };

            Task<bool> operation = coordinator.CreateBranchAsync("blocked-branch");
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.Dispose();
            releaseConfirmation.TrySetResult(false);

            bool result = await operation;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Closing_waits_for_activation_and_retires_the_tracked_backend_with_a_final_snapshot()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = true;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-close-during-activation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, tip);
            var tracked = new PullCycleTestBackend(repository, repository, tip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
            };

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task closing = coordinator.NotifyClosingAsync();
            Assert.That(closing.IsCompleted, Is.False);
            releaseHygiene.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));

            ProjectVersionControlFinalSnapshot? finalSnapshot =
                tracked.RetirementSnapshots.Single();
            Assert.Multiple(() =>
            {
                Assert.That(tracked.RetirementCalls, Is.EqualTo(1));
                Assert.That(finalSnapshot, Is.Not.Null);
                Assert.That(finalSnapshot!.Message, Is.EqualTo("beutl: snapshot on close"));
                Assert.That(finalSnapshot.Kind, Is.EqualTo(SnapshotKind.Close));
                Assert.That(discovery.RetirementSnapshots, Has.Count.EqualTo(1));
                Assert.That(discovery.RetirementSnapshots.Single(), Is.Null);
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Closing_passes_a_final_snapshot_to_an_untracked_owned_backend()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = true;
            await CreateProjectForFakeVersionControlAsync(
                "version-control-close-untracked-backend");
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip);

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            await coordinator.NotifyClosingAsync();

            ProjectVersionControlFinalSnapshot? finalSnapshot =
                backend.RetirementSnapshots.Single();
            Assert.Multiple(() =>
            {
                Assert.That(backend.Repository, Is.Null);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(finalSnapshot, Is.Not.Null);
                Assert.That(finalSnapshot!.Message, Is.EqualTo("beutl: snapshot on close"));
                Assert.That(finalSnapshot.Kind, Is.EqualTo(SnapshotKind.Close));
            });
        }
        finally
        {
            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_propagates_cancellation_to_the_identity_request()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            await CreateProjectForFakeVersionControlAsync(
                "version-control-initialize-identity-cancellation");
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip)
            {
                RequireIdentityForInitialization = true,
            };

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            using var cancellation = new CancellationTokenSource();
            CancellationToken observedToken = default;
            int identityRequests = 0;

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                token =>
                {
                    identityRequests++;
                    observedToken = token;
                    cancellation.Cancel();
                    return Task.FromCanceled<GitIdentity?>(token);
                },
                cancellation.Token);

            Assert.That(
                async () => await initialization,
                Throws.InstanceOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(identityRequests, Is.EqualTo(1));
                Assert.That(observedToken, Is.EqualTo(cancellation.Token));
                Assert.That(backend.InitializeCalls, Is.EqualTo(1));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.Repository, Is.Null);
            });
        }
        finally
        {
            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Disposal_from_a_service_publication_callback_finishes_with_a_hidden_service()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.GitExecutablePath = gitPath;
            config.UseLfsWhenAvailable = false;
            await CreateTrackedProjectAsync("version-control-reentrant-disposal");

            var editorService = new EditorService(new ExtensionProvider());
            var disposed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editorService.ProjectVersionControlService.Subscribe(
                service =>
                {
                    if (service?.Repository is not null && coordinator is not null)
                    {
                        coordinator.Dispose();
                        disposed.TrySetResult();
                    }
                });

            coordinator = new VersionControlCoordinator(TestShell.Project, editorService);
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Throwing_service_publication_subscriber_does_not_stop_later_state_publication()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.GitExecutablePath = gitPath;
            config.UseLfsWhenAvailable = false;
            await CreateTrackedProjectAsync("version-control-publication-exception");

            var editorService = new EditorService(new ExtensionProvider());
            var throwingPublication = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editorService.ProjectVersionControlService.Subscribe(
                service =>
                {
                    if (service?.Repository is not null)
                    {
                        throwingPublication.TrySetResult();
                        throw new InvalidOperationException("subscriber failed");
                    }
                });

            coordinator = new VersionControlCoordinator(TestShell.Project, editorService);
            await throwingPublication.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => editorService.ProjectVersionControlService.Value is not null);

            coordinator.Dispose();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
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
        var oldConfirmSwitchBranchAsync =
            TestShell.VersionControl.ConfirmSwitchBranchAsync;

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
            TestShell.VersionControl.ConfirmSwitchBranchAsync =
                oldConfirmSwitchBranchAsync;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Branch_cycle_publishes_hidden_and_reopened_service_with_matching_tracked_state()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        var oldConfirmSwitchBranchAsync = TestShell.VersionControl.ConfirmSwitchBranchAsync;

        try
        {
            config.GitExecutablePath = gitPath;
            config.UseLfsWhenAvailable = false;
            await CreateTrackedProjectAsync("version-control-publication");
            IProjectVersionControlService original = TestShell.VersionControl.CurrentService!;
            var publications = new List<(IProjectVersionControlService? Service, bool IsTracked)>();
            using IDisposable subscription = TestShell.Editor.ProjectVersionControlService.Subscribe(
                service => publications.Add((service, TestShell.VersionControl.IsTracked.Value)));
            TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);

            Assert.That(
                await TestShell.VersionControl.CreateBranchAsync("publication-branch"),
                Is.True);
            HeadlessTestHelpers.Settle();

            int hidden = publications.FindIndex(item => item.Service is null);
            int republished = hidden < 0
                ? -1
                : publications.FindIndex(
                    hidden + 1,
                    item => ReferenceEquals(item.Service, original));
            Assert.Multiple(() =>
            {
                Assert.That(hidden, Is.GreaterThanOrEqualTo(0));
                Assert.That(republished, Is.GreaterThan(hidden));
                Assert.That(
                    publications.All(item =>
                        item.IsTracked == (item.Service?.Repository is not null)),
                    Is.True);
                Assert.That(TestShell.VersionControl.CurrentService, Is.SameAs(original));
                Assert.That(TestShell.Editor.ProjectVersionControlService.Value, Is.SameAs(original));
            });
        }
        finally
        {
            TestShell.VersionControl.ConfirmSwitchBranchAsync = oldConfirmSwitchBranchAsync;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task External_close_waits_for_branch_cycle_then_retires_the_reopened_service()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        var oldConfirmation = TestShell.VersionControl.ConfirmSwitchBranchAsync;
        var confirmationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnClose = true;
            config.UseLfsWhenAvailable = false;
            await CreateTrackedProjectAsync("version-control-close-during-branch");
            IProjectVersionControlService staleService =
                TestShell.VersionControl.CurrentService!;
            TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmationEntered.TrySetResult();
                return releaseConfirmation.Task;
            };

            Task<bool> branch = TestShell.VersionControl.CreateBranchAsync("close-race");
            await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task close = TestShell.Project.CloseProject();

            Assert.Multiple(() =>
            {
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            });

            releaseConfirmation.TrySetResult(true);
            Assert.That(await branch.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(TestShell.VersionControl.CurrentService, Is.Null);
                Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await staleService.GetStatusAsync(CancellationToken.None));
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
            TestShell.VersionControl.ConfirmSwitchBranchAsync = oldConfirmation;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Pull_cycle_preserves_dirty_state_while_fast_forwarding_a_remote_ahead_branch()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        var oldConfirmPullAsync = TestShell.VersionControl.ConfirmPullAsync;

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
            string remoteCommit = (await RunGitAsync(
                gitPath,
                peerRoot,
                "rev-parse",
                "HEAD")).Trim();

            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "local-marker.txt"),
                "local safety state\n");
            Project beforePull = TestShell.Project.CurrentProject.Value!;
            TestShell.VersionControl.ConfirmPullAsync = _ => Task.FromResult(true);
            Assert.That(
                await TestShell.VersionControl.PullAsync(),
                Is.TypeOf<RemoteOpResult.Success>());
            HeadlessTestHelpers.Settle();

            IReadOnlyList<CommitInfo> history =
                await TestShell.VersionControl.CurrentService!.GetHistoryAsync(
                    0,
                    20,
                    CancellationToken.None);
            CommitInfo safety = history.First(commit =>
                commit.Kind == SnapshotKind.Safety
                && commit.Subject == "beutl: safety snapshot before pull");
            string safetyParent = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                $"{safety.Sha}^")).Trim();
            string checkpointRefs = await RunGitAsync(
                gitPath,
                projectRoot,
                "for-each-ref",
                "--format=%(refname)",
                "refs/beutl/safety");
            WorkspaceStatus status = await TestShell.VersionControl.CurrentService.GetStatusAsync(
                CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.SameAs(beforePull));
                Assert.That(safetyParent, Is.EqualTo(remoteCommit));
                Assert.That(
                    File.ReadAllText(Path.Combine(projectRoot, "remote-marker.txt")),
                    Is.EqualTo("remote state\n"));
                Assert.That(
                    File.ReadAllText(Path.Combine(projectRoot, "local-marker.txt")),
                    Is.EqualTo("local safety state\n"));
                Assert.That(checkpointRefs, Is.Empty);
                Assert.That(status.IsClean, Is.True);
            });
        }
        finally
        {
            TestShell.VersionControl.ConfirmPullAsync = oldConfirmPullAsync;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Pull_reopen_failure_rolls_back_exact_tip_and_restores_dirty_checkpoint()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        var oldConfirmPullAsync = TestShell.VersionControl.ConfirmPullAsync;

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnSave = true;
            config.AutoCommitOnClose = true;
            config.UseLfsWhenAvailable = false;

            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-pull-recovery");
            project.Variables[RestoreStateKey] = "valid-local-state";
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            IProjectVersionControlService service = TestShell.VersionControl.CurrentService!;
            string originalTip = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();

            string remoteRoot = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-pull-recovery-remote.git");
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

            string peerRoot = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-pull-recovery-peer");
            await RunGitAsync(
                gitPath,
                projectRoot,
                "clone",
                "--branch",
                "main",
                remoteRoot,
                peerRoot);
            await RunGitAsync(gitPath, peerRoot, "config", "user.name", "Beutl Headless Peer");
            await RunGitAsync(
                gitPath,
                peerRoot,
                "config",
                "user.email",
                "headless-peer@example.invalid");
            string peerProjectFile = Path.Combine(peerRoot, Path.GetFileName(projectFile));
            await File.WriteAllTextAsync(peerProjectFile, "{ invalid remote project json");
            await RunGitAsync(
                gitPath,
                peerRoot,
                "add",
                "--",
                Path.GetFileName(projectFile));
            await RunGitAsync(gitPath, peerRoot, "commit", "-m", "invalid remote project");
            await RunGitAsync(gitPath, peerRoot, "push");

            string localMarker = Path.Combine(projectRoot, "local-recovery-marker.txt");
            await File.WriteAllTextAsync(localMarker, "keep local state\n");
            TestShell.VersionControl.ConfirmPullAsync = _ => Task.FromResult(true);

            RemoteOpResult result = await TestShell.VersionControl.PullAsync();
            HeadlessTestHelpers.Settle();

            service = TestShell.VersionControl.CurrentService!;
            string recoveredTip = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();
            WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
            string checkpointRefs = await RunGitAsync(
                gitPath,
                projectRoot,
                "for-each-ref",
                "--format=%(refname)",
                "refs/beutl/safety");
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
                Assert.That(recoveredTip, Is.EqualTo(originalTip));
                Assert.That(
                    TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                    Is.EqualTo("valid-local-state"));
                Assert.That(File.ReadAllText(localMarker), Is.EqualTo("keep local state\n"));
                Assert.That(status.IsClean, Is.False);
                Assert.That(checkpointRefs, Is.Empty);
            });
        }
        finally
        {
            TestShell.VersionControl.ConfirmPullAsync = oldConfirmPullAsync;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public Task Pull_ownership_loss_keeps_the_project_closed_and_checkpoint_retained()
    {
        return AssertUncertainPullKeepsProjectClosedAsync(PullTransitionState.OwnershipLost);
    }

    [AvaloniaTest]
    public Task Pull_recovery_failure_keeps_the_project_closed_and_checkpoint_retained()
    {
        return AssertUncertainPullKeepsProjectClosedAsync(PullTransitionState.RecoveryFailed);
    }

    [AvaloniaTest]
    public Task Pull_success_with_ownership_loss_keeps_the_project_closed_and_checkpoint_retained()
    {
        return AssertUncertainPullKeepsProjectClosedAsync(
            PullTransitionState.OwnershipLost,
            reportSuccess: true);
    }

    [AvaloniaTest]
    public Task Pull_success_with_recovery_failure_keeps_the_project_closed_and_checkpoint_retained()
    {
        return AssertUncertainPullKeepsProjectClosedAsync(
            PullTransitionState.RecoveryFailed,
            reportSuccess: true);
    }

    [AvaloniaTest]
    public async Task Pull_recovery_final_tip_mismatch_keeps_the_project_closed_and_checkpoint_retained()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        Func<string, Task>? openingObserver = null;

        try
        {
            config.AutoCommitOnSave = false;
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pull-final-tip-mismatch");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var pulledTip = new CheckedOutBranchTip(
                originalTip.RefName,
                "2222222222222222222222222222222222222222");
            var unexpectedFinalTip = new CheckedOutBranchTip(
                originalTip.RefName,
                "3333333333333333333333333333333333333333");
            var discovery = new PullCycleTestBackend(repository: null, repository, originalTip);
            var backend = new PullCycleTestBackend(repository, repository, originalTip)
            {
                PullResult = new FastForwardPullResult(
                    new RemoteOpResult.Failed("pull failed"),
                    pulledTip,
                    PullTransitionState.Applied),
                RollbackResult = new BranchTipRollbackResult.RolledBack(),
            };
            backend.EnqueueObservedTip(originalTip);
            backend.EnqueueObservedTip(pulledTip);
            backend.EnqueueObservedTip(unexpectedFinalTip);

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            int reopenAttempts = 0;
            openingObserver = _ =>
            {
                reopenAttempts++;
                return Task.CompletedTask;
            };
            TestShell.Project.Opening += openingObserver;

            RemoteOpResult result = await coordinator.PullAsync();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.CheckpointCreateCalls, Is.EqualTo(1));
                Assert.That(backend.RollbackCalls, Is.EqualTo(1));
                Assert.That(backend.RestoreCheckpointCalls, Is.EqualTo(1));
                Assert.That(backend.DeleteCheckpointCalls, Is.Zero);
                Assert.That(backend.IsCheckpointRetained, Is.True);
                Assert.That(reopenAttempts, Is.Zero);
            });
        }
        finally
        {
            if (openingObserver is not null)
            {
                TestShell.Project.Opening -= openingObserver;
            }

            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Restore_reopen_failure_appends_a_recovery_commit_and_reopens_original_state()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;
        var oldConfirmRestoreAsync = TestShell.VersionControl.ConfirmRestoreAsync;

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnSave = true;
            config.AutoCommitOnClose = true;
            config.UseLfsWhenAvailable = false;

            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-restore-recovery");
            project.Variables[RestoreStateKey] = "original-state";
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            string validProject = await File.ReadAllTextAsync(projectFile);
            await File.WriteAllTextAsync(projectFile, "{ invalid project json");
            string projectPathspec = Path.GetRelativePath(projectRoot, projectFile)
                .Replace('\\', '/');
            await RunGitAsync(gitPath, projectRoot, "add", "--", projectPathspec);
            await RunGitAsync(gitPath, projectRoot, "commit", "-m", "invalid restore target");
            string invalidTarget = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();

            await File.WriteAllTextAsync(projectFile, validProject);
            TestShell.VersionControl.ConfirmRestoreAsync = _ => Task.FromResult(true);

            Assert.That(
                await TestShell.VersionControl.RestoreAsync(invalidTarget),
                Is.False);
            HeadlessTestHelpers.Settle();

            Project reopened = TestShell.Project.CurrentProject.Value!;
            IProjectVersionControlService service = TestShell.VersionControl.CurrentService!;
            IReadOnlyList<CommitInfo> history = await service.GetHistoryAsync(
                0,
                10,
                CancellationToken.None);
            WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(reopened, Is.Not.Null);
                Assert.That(reopened.Variables[RestoreStateKey], Is.EqualTo("original-state"));
                Assert.That(history[0].Kind, Is.EqualTo(SnapshotKind.Recovery));
                Assert.That(
                    history[0].Subject,
                    Is.EqualTo("beutl: recover original project state after failed restore"));
                Assert.That(history[1].Kind, Is.EqualTo(SnapshotKind.Restore));
                Assert.That(history.Any(commit => commit.Sha == invalidTarget), Is.True);
                Assert.That(status.IsClean, Is.True);
            });
        }
        finally
        {
            TestShell.VersionControl.ConfirmRestoreAsync = oldConfirmRestoreAsync;
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
        var oldConfirmRestoreAsync =
            TestShell.VersionControl.ConfirmRestoreAsync;

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

            using (IDisposable outputOperation = TestShell.Editor.TryBeginOutputOperation()!)
            {
                Assert.That(
                    await TestShell.VersionControl.RestoreAsync(target.Sha),
                    Is.False,
                    "A restore must not start while output is reading project files.");
                Assert.That(confirmationCount, Is.Zero);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            }

            var confirmationStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseConfirmation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TestShell.VersionControl.ConfirmRestoreAsync = _ =>
            {
                confirmationCount++;
                confirmationStarted.TrySetResult();
                return releaseConfirmation.Task;
            };
            Task<bool> pendingRestore = TestShell.VersionControl.RestoreAsync(target.Sha);
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            IDisposable? blockedOutput = TestShell.Editor.TryBeginOutputOperation();
            try
            {
                Assert.That(
                    blockedOutput,
                    Is.Null,
                    "The worktree reservation must reject an export while confirmation is pending.");
            }
            finally
            {
                blockedOutput?.Dispose();
            }
            releaseConfirmation.SetResult(false);
            Assert.That(await pendingRestore, Is.False);

            TestShell.VersionControl.ConfirmRestoreAsync = _ =>
            {
                confirmationCount++;
                return Task.FromResult(true);
            };

            Assert.That(await TestShell.VersionControl.RestoreAsync(target.Sha), Is.True);
            HeadlessTestHelpers.Settle();

            Project restoredProject = TestShell.Project.CurrentProject.Value!;
            Scene restoredScene = restoredProject.Items.OfType<Scene>().Single();
            var restoredEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.Multiple(() =>
            {
                Assert.That(confirmationCount, Is.EqualTo(2));
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
            TestShell.VersionControl.ConfirmRestoreAsync = oldConfirmRestoreAsync;
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    private static async Task AssertUncertainPullKeepsProjectClosedAsync(
        PullTransitionState transitionState,
        bool reportSuccess = false)
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        Func<string, Task>? openingObserver = null;

        try
        {
            config.AutoCommitOnSave = false;
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                $"version-control-pull-{transitionState.ToString().ToLowerInvariant()}");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var pulledTip = new CheckedOutBranchTip(
                originalTip.RefName,
                "2222222222222222222222222222222222222222");
            var discovery = new PullCycleTestBackend(repository: null, repository, originalTip);
            var backend = new PullCycleTestBackend(repository, repository, originalTip)
            {
                PullResult = new FastForwardPullResult(
                    reportSuccess
                        ? new RemoteOpResult.Success()
                        : new RemoteOpResult.Failed("pull failed"),
                    pulledTip,
                    transitionState),
            };

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            int reopenAttempts = 0;
            openingObserver = _ =>
            {
                reopenAttempts++;
                return Task.CompletedTask;
            };
            TestShell.Project.Opening += openingObserver;

            RemoteOpResult result = await coordinator.PullAsync();
            HeadlessTestHelpers.Settle();

            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            var failure = (RemoteOpResult.Failed)result;
            Assert.Multiple(() =>
            {
                Assert.That(
                    failure.Stderr,
                    Is.EqualTo(Strings.VersionControl_PullTransitionUncertain));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.CheckpointCreateCalls, Is.EqualTo(1));
                Assert.That(backend.PullCalls, Is.EqualTo(1));
                Assert.That(backend.RollbackCalls, Is.Zero);
                Assert.That(backend.RestoreCheckpointCalls, Is.Zero);
                Assert.That(backend.DeleteCheckpointCalls, Is.Zero);
                Assert.That(backend.IsCheckpointRetained, Is.True);
                Assert.That(reopenAttempts, Is.Zero);
            });
        }
        finally
        {
            if (openingObserver is not null)
            {
                TestShell.Project.Opening -= openingObserver;
            }

            coordinator?.Dispose();
            await TestReset.ResetShellAsync();
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    private static async Task<Project> CreateProjectForFakeVersionControlAsync(string directoryName)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, directoryName);
        Directory.CreateDirectory(location);
        Project? project = await TestShell.Project.CreateProject(
            640,
            480,
            30,
            44100,
            "tracked",
            location);
        HeadlessTestHelpers.Settle();
        Assert.That(project, Is.Not.Null);
        return project!;
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
            _ => Task.FromResult<GitIdentity?>(
                new GitIdentity("Beutl Headless Test", "headless@example.invalid")));
        Assert.That(initialized, Is.True);

        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        return (project, editor);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(25);
        }

        Assert.That(condition(), Is.True, "The expected state was not reached.");
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

    private sealed class PullCycleTestBackend :
        IProjectVersionControlBackend,
        IProjectVersionControlTransaction
    {
        private static readonly WorkspaceStatus DirtyStatus = new(
            "main",
            0,
            0,
            [new FileChange("dirty.txt", FileChangeStatus.Modified)],
            HasConflicts: false);

        private readonly RepositoryInfo? _discoveredRepository;
        private readonly Queue<CheckedOutBranchTip> _observedTips = new();
        private readonly CheckedOutBranchTip _originalTip;
        private bool _hasIdentity;
        private ProjectCheckpoint? _checkpoint;

        public PullCycleTestBackend(
            RepositoryInfo? repository,
            RepositoryInfo? discoveredRepository,
            CheckedOutBranchTip originalTip)
        {
            Repository = repository;
            _discoveredRepository = discoveredRepository;
            _originalTip = originalTip;
            PullResult = new FastForwardPullResult(
                new RemoteOpResult.Failed("pull failed"),
                originalTip);
        }

        public RepositoryInfo? Repository { get; private set; }

        public RepositoryLockInfo? RecoverableLock => null;

        public FastForwardPullResult PullResult { get; init; }

        public BranchTipRollbackResult RollbackResult { get; init; } =
            new BranchTipRollbackResult.RolledBack();

        public TaskCompletionSource? EnsureHygieneStarted { get; init; }

        public Task? EnsureHygieneRelease { get; init; }

        public TaskCompletionSource? InitializeStarted { get; init; }

        public Task? InitializeRelease { get; init; }

        public bool RequireIdentityForInitialization { get; init; }

        public int InitializeCalls { get; private set; }

        public int SetLocalIdentityCalls { get; private set; }

        public int CheckpointCreateCalls { get; private set; }

        public int PullCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public int RestoreCheckpointCalls { get; private set; }

        public int DeleteCheckpointCalls { get; private set; }

        public int RetirementCalls { get; private set; }

        public List<ProjectVersionControlFinalSnapshot?> RetirementSnapshots { get; } = [];

        public bool IsCheckpointRetained => _checkpoint is not null;

        public event EventHandler<WorkspaceStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<RepositoryLockInfo>? RecoverableLockAvailable
        {
            add { }
            remove { }
        }

        public void EnqueueObservedTip(CheckedOutBranchTip tip)
        {
            _observedTips.Enqueue(tip);
        }

        public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new GitAvailability(
                GitAvailabilityState.Installed,
                "git",
                new Version(2, 40),
                LfsInstalled: false));
        }

        public Task<RepositoryInfo?> DiscoverRepositoryAsync(
            string projectRoot,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<RepositoryInfo?>(_discoveredRepository);
        }

        public async Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken)
        {
            EnsureHygieneStarted?.TrySetResult();
            if (EnsureHygieneRelease is not null)
            {
                await EnsureHygieneRelease.WaitAsync(cancellationToken);
            }
        }

        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(DirtyStatus);
        }

        public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _observedTips.TryDequeue(out CheckedOutBranchTip? tip)
                    ? tip
                    : _originalTip);
        }

        public Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
            string message,
            CancellationToken cancellationToken)
        {
            CheckpointCreateCalls++;
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                _originalTip);
            _checkpoint = checkpoint;
            return Task.FromResult(checkpoint);
        }

        public Task<FastForwardPullResult> PullFastForwardAsync(
            CheckedOutBranchTip expectedCurrent,
            ProjectCheckpoint? checkpoint,
            CancellationToken cancellationToken)
        {
            PullCalls++;
            return Task.FromResult(PullResult);
        }

        public Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
            CheckedOutBranchTip expectedCurrent,
            CheckedOutBranchTip target,
            CancellationToken cancellationToken)
        {
            RollbackCalls++;
            return Task.FromResult(RollbackResult);
        }

        public Task RestoreProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            RestoreCheckpointCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            DeleteCheckpointCalls++;
            _checkpoint = null;
            return Task.FromResult(true);
        }

        public Task<TResult> ExecuteExclusiveAsync<TResult>(
            Func<IProjectVersionControlTransaction, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            return operation(this);
        }

        public async Task InitializeAsync(
            InitOptions options,
            CancellationToken cancellationToken)
        {
            InitializeCalls++;
            if (RequireIdentityForInitialization && !_hasIdentity)
            {
                throw new GitIdentityRequiredException();
            }

            InitializeStarted?.TrySetResult();
            if (InitializeRelease is not null)
            {
                await InitializeRelease.WaitAsync(cancellationToken);
            }

            Repository = options.TargetRepository;
        }

        public Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CommitResult>(new CommitResult.NoChanges());
        }

        public Task<CommitResult> CommitProjectTreeAsync(
            CheckedOutBranchTip expectedCurrent,
            string sourceCommit,
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CommitResult>(new CommitResult.NoChanges());
        }

        public Task SetRemoteAsync(string url, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<RemoteOpResult> PushAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<RemoteOpResult>(new RemoteOpResult.Success());
        }

        public Task SetLocalIdentityAsync(
            GitIdentity identity,
            CancellationToken cancellationToken)
        {
            SetLocalIdentityCalls++;
            _hasIdentity = true;
            return Task.CompletedTask;
        }

        public Task RetireAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
        {
            RetirementCalls++;
            RetirementSnapshots.Add(finalSnapshot);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveRecoverableLockAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CommitInfo>>([]);
        }

        public Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(
            string sha,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<FileChange>>([]);
        }

        public Task<string> GetDiffAsync(
            string sha,
            string? path,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BranchInfo>>([]);
        }

        public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RemoteInfo>>([]);
        }

        public Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<GitIdentity?>(null);
        }

        public Task CreateBranchAsync(
            string name,
            string startPoint,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SwitchBranchAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
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
