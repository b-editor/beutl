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
            await WaitUntilAsync(() =>
                coordinator.CurrentService is null
                && editorService.ProjectVersionControlService.Value is null);

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
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Save_snapshot_is_skipped_while_an_output_operation_is_active()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-save-during-output");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig
            {
                AutoCommitOnSave = true,
                AutoCommitOnClose = false,
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            using (IDisposable outputOperation = editorService.TryBeginOutputOperation()!)
            {
                await coordinator.NotifySavedAsync();
                Assert.That(
                    backend.CommitAllCalls,
                    Is.Zero,
                    "A save must not snapshot files while output may still be writing them.");
            }

            await coordinator.NotifySavedAsync();
            Assert.That(
                backend.CommitAllCalls,
                Is.EqualTo(1),
                "The next save after output completes must recover the skipped snapshot.");
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Save_snapshot_reservation_blocks_output_until_commit_completes()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? snapshot = null;
        var commitStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-output-during-save");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                CommitAllStarted = commitStarted,
                CommitAllRelease = releaseCommit.Task,
            };
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig
            {
                AutoCommitOnSave = true,
                AutoCommitOnClose = false,
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            snapshot = coordinator.NotifySavedAsync();
            await commitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            IDisposable? blockedOutput = editorService.TryBeginOutputOperation();
            try
            {
                Assert.That(
                    blockedOutput,
                    Is.Null,
                    "Output must not start after snapshot staging has begun.");
            }
            finally
            {
                blockedOutput?.Dispose();
            }

            releaseCommit.TrySetResult();
            await snapshot.WaitAsync(TimeSpan.FromSeconds(5));
            snapshot = null;

            using IDisposable? outputAfterSnapshot = editorService.TryBeginOutputOperation();
            Assert.That(outputAfterSnapshot, Is.Not.Null);
        }
        finally
        {
            releaseCommit.TrySetResult();
            if (snapshot is not null)
            {
                await snapshot.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Close_snapshot_is_skipped_while_an_output_operation_is_active()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-close-during-output");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig
            {
                AutoCommitOnClose = true,
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            using (IDisposable outputOperation = editorService.TryBeginOutputOperation()!)
            {
                await TestShell.Project.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(
                    backend.RetirementSnapshots,
                    Is.EqualTo(new ProjectVersionControlFinalSnapshot?[] { null }),
                    "Closing may continue, but retirement must not snapshot a partial output file.");
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Project_close_waits_for_activation_and_retires_the_tracked_backend_with_a_final_snapshot()
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

            IProjectVersionControlService? coordinatorServiceAtDiscoveryRetirement = discovery;
            IProjectVersionControlService? editorServiceAtDiscoveryRetirement = discovery;
            bool trackedAtDiscoveryRetirement = false;
            discovery.RetirementStarting = () =>
            {
                coordinatorServiceAtDiscoveryRetirement = coordinator.CurrentService;
                editorServiceAtDiscoveryRetirement =
                    editorService.ProjectVersionControlService.Value;
                trackedAtDiscoveryRetirement = coordinator.IsTracked.Value;
            };

            Task closing = TestShell.Project.CloseProject();
            Assert.That(closing.IsCompleted, Is.False);
            releaseHygiene.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));

            ProjectVersionControlFinalSnapshot? finalSnapshot =
                tracked.RetirementSnapshots.Single();
            Assert.Multiple(() =>
            {
                Assert.That(tracked.RetirementCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(finalSnapshot, Is.Not.Null);
                Assert.That(finalSnapshot!.Message, Is.EqualTo("beutl: snapshot on close"));
                Assert.That(finalSnapshot.Kind, Is.EqualTo(SnapshotKind.Close));
                Assert.That(discovery.RetirementSnapshots, Has.Count.EqualTo(1));
                Assert.That(discovery.RetirementSnapshots.Single(), Is.Null);
                Assert.That(coordinatorServiceAtDiscoveryRetirement, Is.Not.SameAs(discovery));
                Assert.That(
                    editorServiceAtDiscoveryRetirement,
                    Is.SameAs(coordinatorServiceAtDiscoveryRetirement));
                Assert.That(
                    trackedAtDiscoveryRetirement,
                    Is.EqualTo(coordinatorServiceAtDiscoveryRetirement?.Repository is not null));
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
    public async Task Project_close_passes_a_final_snapshot_to_an_untracked_owned_backend()
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

            await TestShell.Project.CloseProject();

            ProjectVersionControlFinalSnapshot? finalSnapshot =
                backend.RetirementSnapshots.Single();
            Assert.Multiple(() =>
            {
                Assert.That(backend.Repository, Is.Null);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
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
    public async Task Caller_cancellation_after_the_close_commit_point_does_not_skip_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            cancelAfterCommitPoint = null;
        using var closeCancellation = new CancellationTokenSource();

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-close-post-commit-cancellation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var config = new VersionControlConfig
            {
                AutoCommitOnClose = true,
            };
            var editorService = new EditorService(new ExtensionProvider());

            cancelAfterCommitPoint = (_, finalizerCancellation) =>
            {
                Assert.That(finalizerCancellation, Is.EqualTo(CancellationToken.None));
                closeCancellation.Cancel();
                return Task.CompletedTask;
            };
            TestShell.Project.ClosingFinalizing += cancelAfterCommitPoint;
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            await TestShell.Project.CloseProject(closeCancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                backend.RetirementCalls == 1
                && backend.DisposeCalls == 1);

            ProjectVersionControlFinalSnapshot? finalSnapshot =
                backend.RetirementSnapshots.Single();
            Assert.Multiple(() =>
            {
                Assert.That(closeCancellation.IsCancellationRequested, Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(finalSnapshot, Is.Not.Null);
                Assert.That(finalSnapshot!.Message, Is.EqualTo("beutl: snapshot on close"));
                Assert.That(finalSnapshot.Kind, Is.EqualTo(SnapshotKind.Close));
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });
        }
        finally
        {
            if (cancelAfterCommitPoint is not null)
            {
                TestShell.Project.ClosingFinalizing -= cancelAfterCommitPoint;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
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
                TestShell.Project.CurrentProject.Value!,
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
                Assert.That(observedToken.CanBeCanceled, Is.True);
                Assert.That(observedToken.IsCancellationRequested, Is.True);
                Assert.That(cancellation.Token.IsCancellationRequested, Is.True);
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
    public async Task InitializeCurrentProject_passes_the_requested_identity_to_the_retry()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            await CreateProjectForFakeVersionControlAsync(
                "version-control-initialize-identity-retry");
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
            var identity = new GitIdentity("Identity Retry", "identity-retry@example.invalid");
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool initialized = await coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(identity));

            Assert.Multiple(() =>
            {
                Assert.That(initialized, Is.True);
                Assert.That(backend.InitializeCalls, Is.EqualTo(2));
                Assert.That(backend.InitializationOptions[0].Identity, Is.Null);
                Assert.That(backend.InitializationOptions[1].Identity, Is.EqualTo(identity));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.Repository, Is.Not.Null);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_rejects_a_project_that_is_no_longer_open()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            Project staleProject = await CreateProjectForFakeVersionControlAsync(
                "version-control-stale-initialization-target");
            Project currentProject = await CreateProjectForFakeVersionControlAsync(
                "version-control-current-initialization-target");
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
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await coordinator.InitializeCurrentProjectAsync(
                    staleProject,
                    _ => Task.FromResult<GitIdentity?>(null)));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("no longer the open project"));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(currentProject));
                Assert.That(backend.InitializeCalls, Is.Zero);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_cancels_and_joins_an_initialization_identity_request()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var identityStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseIdentity = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await CreateProjectForFakeVersionControlAsync(
                "version-control-initialize-disposal");
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

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                async cancellationToken =>
                {
                    identityStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }

                    await releaseIdentity.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return new GitIdentity(
                        "Disposal Test",
                        "disposal@example.invalid");
                });
            await identityStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = coordinator.DisposeAsync().AsTask();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(backend.RetirementCalls, Is.Zero);
            });

            releaseIdentity.TrySetResult();
            OperationCanceledException? initializationCancellation = null;
            try
            {
                await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException ex)
            {
                initializationCancellation = ex;
            }

            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(initializationCancellation, Is.Not.Null);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseIdentity.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_continues_when_activation_cancellation_callback_throws()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? disposal = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activationTokenObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration throwingRegistration = default;
        int activationCancellationCallbackCalls = 0;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-throwing-activation-cancellation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(
                repository: null,
                repository,
                tip);
            var tracked = new PullCycleTestBackend(repository, repository, tip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
                EnsureHygieneTokenObserved = cancellationToken =>
                {
                    throwingRegistration = cancellationToken.Register(() =>
                    {
                        Interlocked.Increment(ref activationCancellationCallbackCalls);
                        throw new InvalidOperationException(
                            "Activation cancellation callback failure.");
                    });
                    activationTokenObserved.TrySetResult();
                },
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await activationTokenObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, discovery)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    discovery));

            Exception? synchronousDisposalFailure = null;
            try
            {
                disposal = coordinator.DisposeAsync().AsTask();
            }
            catch (Exception ex)
            {
                synchronousDisposalFailure = ex;
            }

            disposal ??= coordinator.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(synchronousDisposalFailure, Is.Null);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(
                    Volatile.Read(ref activationCancellationCallbackCalls),
                    Is.EqualTo(1));
                Assert.That(discovery.RetirementCalls, Is.EqualTo(1));
                Assert.That(discovery.DisposeCalls, Is.EqualTo(1));
                Assert.That(tracked.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            throwingRegistration.Dispose();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_waits_for_activation_cancellation_callbacks_before_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? disposal = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activationTokenObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCallbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellationCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var trackedRetirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTrackedRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var trackedDisposed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hygieneCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration blockingRegistration = default;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-blocking-activation-cancellation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(
                repository: null,
                repository,
                tip)
            {
                RetirementStarted = retirementStarted,
            };
            var tracked = new PullCycleTestBackend(repository, repository, tip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
                EnsureHygieneCompleted = hygieneCompleted,
                RetirementStarted = trackedRetirementStarted,
                RetirementRelease = releaseTrackedRetirement.Task,
                DisposeCompleted = trackedDisposed,
                EnsureHygieneTokenObserved = cancellationToken =>
                {
                    blockingRegistration = cancellationToken.Register(() =>
                    {
                        cancellationCallbackStarted.TrySetResult();
                        releaseCancellationCallback.Task.GetAwaiter().GetResult();
                    });
                    activationTokenObserved.TrySetResult();
                },
            };
            var probe = new MutableGitInstallationProbe();
            var config = new VersionControlConfig();
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await activationTokenObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, discovery)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    discovery));

            probe.IsInstalled = true;
            Task.Run(() => coordinator.GetAvailabilityAsync())
                .GetAwaiter()
                .GetResult();

            disposal = Task.Run(() => coordinator.DisposeAsync().AsTask());
            await cancellationCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await hygieneCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();
            await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(retirementStarted.Task.IsCompleted, Is.False);
                Assert.That(discovery.RetirementCalls, Is.Zero);
                Assert.That(discovery.DisposeCalls, Is.Zero);
                Assert.That(trackedDisposed.Task.IsCompleted, Is.False);
                Assert.That(tracked.DisposeCalls, Is.Zero);
                Assert.That(disposal.IsCompleted, Is.False);
            });

            releaseCancellationCallback.TrySetResult();
            await trackedRetirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(discovery.RetirementCalls, Is.Zero);
                Assert.That(discovery.DisposeCalls, Is.Zero);
                Assert.That(tracked.DisposeCalls, Is.Zero);
                Assert.That(disposal.IsCompleted, Is.False);
            });

            releaseTrackedRetirement.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(discovery.RetirementCalls, Is.EqualTo(1));
                Assert.That(discovery.DisposeCalls, Is.EqualTo(1));
                Assert.That(tracked.RetirementCalls, Is.EqualTo(1));
                Assert.That(tracked.DisposeCalls, Is.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseCancellationCallback.TrySetResult();
            releaseHygiene.TrySetResult();
            releaseTrackedRetirement.TrySetResult();
            blockingRegistration.Dispose();
            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_waits_for_handoff_activation_callbacks_before_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? disposal = null;
        IDisposable? serviceSubscription = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activationTokenObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCallbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellationCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var trackedRetirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration blockingRegistration = default;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-handoff-activation-cancellation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(
                repository: null,
                repository,
                tip);
            var tracked = new PullCycleTestBackend(repository, repository, tip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
                RetirementStarted = trackedRetirementStarted,
                EnsureHygieneTokenObserved = cancellationToken =>
                {
                    blockingRegistration = cancellationToken.Register(() =>
                    {
                        cancellationCallbackStarted.TrySetResult();
                        releaseCancellationCallback.Task.GetAwaiter().GetResult();
                    });
                    activationTokenObserved.TrySetResult();
                },
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await activationTokenObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, discovery)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    discovery));

            serviceSubscription = editorService.ProjectVersionControlService.Subscribe(service =>
            {
                if (ReferenceEquals(service, tracked)
                    && disposal is null
                    && coordinator is not null)
                {
                    disposal = Task.Run(() => coordinator.DisposeAsync().AsTask());
                    cancellationCallbackStarted.Task
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .GetAwaiter()
                        .GetResult();
                }
            });

            releaseHygiene.TrySetResult();
            await WaitUntilAsync(() => cancellationCallbackStarted.Task.IsCompleted);
            HeadlessTestHelpers.Settle();
            await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(disposal, Is.Not.Null);
                Assert.That(trackedRetirementStarted.Task.IsCompleted, Is.False);
                Assert.That(tracked.RetirementCalls, Is.Zero);
                Assert.That(tracked.DisposeCalls, Is.Zero);
                Assert.That(disposal!.IsCompleted, Is.False);
            });

            releaseCancellationCallback.TrySetResult();
            await disposal!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(discovery.RetirementCalls, Is.EqualTo(1));
                Assert.That(discovery.DisposeCalls, Is.EqualTo(1));
                Assert.That(tracked.RetirementCalls, Is.EqualTo(1));
                Assert.That(tracked.DisposeCalls, Is.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseCancellationCallback.TrySetResult();
            releaseHygiene.TrySetResult();
            blockingRegistration.Dispose();
            serviceSubscription?.Dispose();
            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Shared_backend_handoff_waits_for_the_previous_activation_before_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? secondActivationSetup = null;
        Task? disposal = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCallbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellationCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstActivationUnwinding = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstActivation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration blockingRegistration = default;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-shared-backend-handoff");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(
                repository: null,
                repository,
                tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
                EnsureHygieneOverride = async cancellationToken =>
                {
                    if (hygieneStarted.Task.IsCompleted)
                    {
                        return;
                    }

                    blockingRegistration = cancellationToken.Register(() =>
                    {
                        cancellationCallbackStarted.TrySetResult();
                        releaseCancellationCallback.Task.GetAwaiter().GetResult();
                    });
                    hygieneStarted.TrySetResult();
                    try
                    {
                        await releaseHygiene.Task.WaitAsync(cancellationToken);
                    }
                    finally
                    {
                        firstActivationUnwinding.TrySetResult();
                        await releaseFirstActivation.Task;
                    }
                },
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            secondActivationSetup = Task.Run(() => coordinator.OnProjectChanged(project));
            await cancellationCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(coordinator.CurrentService, Is.Null);
            await WaitUntilAsync(() => editorService.ProjectVersionControlService.Value is null);

            releaseCancellationCallback.TrySetResult();
            await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            await firstActivationUnwinding.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposal = Task.Run(() => coordinator.DisposeAsync().AsTask());
            HeadlessTestHelpers.Settle();
            await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(retirementStarted.Task.IsCompleted, Is.False);
                Assert.That(backend.RetirementCalls, Is.Zero);
                Assert.That(backend.DisposeCalls, Is.Zero);
                Assert.That(disposal.IsCompleted, Is.False);
            });

            releaseFirstActivation.TrySetResult();
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.Zero);
                Assert.That(disposal.IsCompleted, Is.False);
            });

            releaseRetirement.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseCancellationCallback.TrySetResult();
            releaseHygiene.TrySetResult();
            releaseFirstActivation.TrySetResult();
            releaseRetirement.TrySetResult();
            blockingRegistration.Dispose();
            if (secondActivationSetup is not null)
            {
                await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Tracked_candidate_shared_with_the_next_activation_is_not_discarded()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? secondActivationSetup = null;
        Task? disposal = null;
        var firstHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHygieneCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstActivationUnwinding = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstActivation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHygieneCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;
        int hygieneCalls = 0;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-shared-tracked-candidate");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var initial = new PullCycleTestBackend(
                repository: null,
                repository,
                tip);
            var shared = new PullCycleTestBackend(repository, repository, tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
                EnsureHygieneOverride = async cancellationToken =>
                {
                    if (Interlocked.Increment(ref hygieneCalls) == 1)
                    {
                        firstHygieneStarted.TrySetResult();
                        try
                        {
                            await releaseFirstHygiene.Task.WaitAsync(cancellationToken);
                        }
                        finally
                        {
                            firstHygieneCompleted.TrySetResult();
                            firstActivationUnwinding.TrySetResult();
                            await releaseFirstActivation.Task;
                        }
                    }
                    else
                    {
                        secondHygieneStarted.TrySetResult();
                        try
                        {
                            await releaseSecondHygiene.Task.WaitAsync(cancellationToken);
                        }
                        finally
                        {
                            secondHygieneCompleted.TrySetResult();
                        }
                    }
                },
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => Interlocked.Increment(ref factoryCalls) == 1
                    ? initial
                    : shared);
            await firstHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            secondActivationSetup = Task.Run(() => coordinator.OnProjectChanged(project));
            await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            await firstActivationUnwinding.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => editorService.ProjectVersionControlService.Value is null);

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await coordinator.CommitManualAsync("blocked alias"));
            Assert.That(shared.CommitAllCalls, Is.Zero);

            releaseFirstActivation.TrySetResult();
            await firstHygieneCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(shared.RetirementCalls, Is.Zero);
                Assert.That(shared.DisposeCalls, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await coordinator.CommitManualAsync("blocked during hygiene"));
            Assert.That(shared.CommitAllCalls, Is.Zero);

            releaseSecondHygiene.TrySetResult();
            await secondHygieneCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, shared)
                && ReferenceEquals(editorService.ProjectVersionControlService.Value, shared));
            disposal = Task.Run(() => coordinator.DisposeAsync().AsTask());
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(shared.RetirementCalls, Is.EqualTo(1));
                Assert.That(shared.DisposeCalls, Is.Zero);
                Assert.That(disposal.IsCompleted, Is.False);
            });

            releaseRetirement.TrySetResult();
            await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(shared.RetirementCalls, Is.EqualTo(1));
                Assert.That(shared.DisposeCalls, Is.EqualTo(1));
                Assert.That(initial.RetirementCalls, Is.EqualTo(1));
                Assert.That(initial.DisposeCalls, Is.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseFirstHygiene.TrySetResult();
            releaseFirstActivation.TrySetResult();
            releaseSecondHygiene.TrySetResult();
            releaseRetirement.TrySetResult();
            if (secondActivationSetup is not null)
            {
                await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Shared_tracked_candidate_is_retired_when_second_hygiene_fails()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? secondActivationSetup = null;
        var firstHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstActivationUnwinding = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstActivation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;
        int hygieneCalls = 0;
        bool sharedWasPublished = false;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-failed-shared-tracked-candidate");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var initial = new PullCycleTestBackend(
                repository: null,
                repository,
                tip);
            var shared = new PullCycleTestBackend(repository, repository, tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
                DisposeCompleted = disposeCompleted,
                EnsureHygieneOverride = async cancellationToken =>
                {
                    if (Interlocked.Increment(ref hygieneCalls) == 1)
                    {
                        firstHygieneStarted.TrySetResult();
                        try
                        {
                            await releaseFirstHygiene.Task.WaitAsync(cancellationToken);
                        }
                        finally
                        {
                            firstActivationUnwinding.TrySetResult();
                            await releaseFirstActivation.Task;
                        }

                        return;
                    }

                    secondHygieneStarted.TrySetResult();
                    throw new InvalidOperationException("Expected second hygiene failure.");
                },
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => Interlocked.Increment(ref factoryCalls) == 1
                    ? initial
                    : shared);
            using IDisposable publication = editorService.ProjectVersionControlService.Subscribe(
                service => sharedWasPublished |= ReferenceEquals(service, shared));
            await firstHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            secondActivationSetup = Task.Run(() => coordinator.OnProjectChanged(project));
            await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            await firstActivationUnwinding.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => editorService.ProjectVersionControlService.Value is null);

            releaseFirstActivation.TrySetResult();
            await secondHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(sharedWasPublished, Is.False);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(shared.RetirementCalls, Is.EqualTo(1));
                Assert.That(shared.DisposeCalls, Is.Zero);
            });

            releaseRetirement.TrySetResult();
            await disposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(sharedWasPublished, Is.False);
                Assert.That(shared.RetirementCalls, Is.EqualTo(1));
                Assert.That(shared.DisposeCalls, Is.EqualTo(1));
                Assert.That(initial.RetirementCalls, Is.EqualTo(1));
                Assert.That(initial.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseFirstHygiene.TrySetResult();
            releaseFirstActivation.TrySetResult();
            releaseRetirement.TrySetResult();
            if (secondActivationSetup is not null)
            {
                await secondActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Stale_factory_completion_cannot_overwrite_a_newer_activation()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? construction = null;
        Task? newerActivationSetup = null;
        var firstFactoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-stale-factory-completion");
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var stale = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip);
            var current = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip);
            var editorService = new EditorService(new ExtensionProvider());
            int factoryCalls = 0;

            construction = Task.Run(() => coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ =>
                {
                    if (Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        firstFactoryStarted.TrySetResult();
                        releaseFirstFactory.Task.GetAwaiter().GetResult();
                        return stale;
                    }

                    return current;
                }));
            await firstFactoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var activeCoordinator = (VersionControlCoordinator)
                editorService.ProjectVersionControlCoordinator!;
            newerActivationSetup = Task.Run(() => activeCoordinator.OnProjectChanged(project));
            await newerActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => ReferenceEquals(activeCoordinator.CurrentService, current));

            releaseFirstFactory.TrySetResult();
            await construction.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => stale.DisposeCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(activeCoordinator.CurrentService, Is.SameAs(current));
                Assert.That(editorService.ProjectVersionControlService.Value, Is.SameAs(current));
                Assert.That(stale.RetirementCalls, Is.EqualTo(1));
                Assert.That(stale.DisposeCalls, Is.EqualTo(1));
                Assert.That(current.RetirementCalls, Is.Zero);
                Assert.That(current.DisposeCalls, Is.Zero);
            });
        }
        finally
        {
            releaseFirstFactory.TrySetResult();
            if (newerActivationSetup is not null)
            {
                await newerActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (construction is not null)
            {
                await construction.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Stale_factory_failure_cannot_clear_a_newer_activation()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? construction = null;
        Task? newerActivationSetup = null;
        var firstFactoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-stale-factory-failure");
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var current = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip);
            var editorService = new EditorService(new ExtensionProvider());
            int factoryCalls = 0;

            construction = Task.Run(() => coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ =>
                {
                    if (Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        firstFactoryStarted.TrySetResult();
                        releaseFirstFactory.Task.GetAwaiter().GetResult();
                        throw new InvalidOperationException("stale factory failure");
                    }

                    return current;
                }));
            await firstFactoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var activeCoordinator = (VersionControlCoordinator)
                editorService.ProjectVersionControlCoordinator!;
            newerActivationSetup = Task.Run(() => activeCoordinator.OnProjectChanged(project));
            await newerActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => ReferenceEquals(activeCoordinator.CurrentService, current));

            releaseFirstFactory.TrySetResult();
            await construction.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(activeCoordinator.CurrentService, Is.SameAs(current));
                Assert.That(editorService.ProjectVersionControlService.Value, Is.SameAs(current));
                Assert.That(current.RetirementCalls, Is.Zero);
                Assert.That(current.DisposeCalls, Is.Zero);
            });
        }
        finally
        {
            releaseFirstFactory.TrySetResult();
            if (newerActivationSetup is not null)
            {
                await newerActivationSetup.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (construction is not null)
            {
                await construction.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_waits_for_rejected_activation_setup_and_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task<Project?>? projectCreation = null;
        Task? disposal = null;
        var factoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
                DisposeStartsRetirement = true,
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ =>
                {
                    factoryStarted.TrySetResult();
                    releaseFactory.Task.GetAwaiter().GetResult();
                    return backend;
                });

            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-rejected-activation");
            Directory.CreateDirectory(location);
            projectCreation = Task.Run(() => TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tracked",
                location));
            await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposal = coordinator.DisposeAsync().AsTask();
            await Task.Delay(100);
            Assert.That(disposal.IsCompleted, Is.False);

            releaseFactory.TrySetResult();
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.Zero);
            });

            releaseRetirement.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseFactory.TrySetResult();
            releaseRetirement.TrySetResult();
            if (projectCreation is not null)
            {
                await projectCreation.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Concurrent_rejected_setups_cleanup_a_shared_backend_once()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task<VersionControlCoordinator>? construction = null;
        Task<Project?>? projectCreation = null;
        Task? disposal = null;
        var firstFactoryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var twoFactoriesStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactories = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;

        try
        {
            await CreateProjectForFakeVersionControlAsync(
                "version-control-concurrent-rejected-initial");
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
                IdempotentRetirement = true,
            };
            var config = new VersionControlConfig();
            var probe = new MutableGitInstallationProbe();
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var editorService = new EditorService(new ExtensionProvider());
            construction = Task.Run(() => new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: _ =>
                {
                    int call = Interlocked.Increment(ref factoryCalls);
                    firstFactoryStarted.TrySetResult();
                    if (call == 2)
                    {
                        twoFactoriesStarted.TrySetResult();
                    }

                    releaseFactories.Task.GetAwaiter().GetResult();
                    return backend;
                }));
            await firstFactoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-concurrent-rejected-next");
            Directory.CreateDirectory(location);
            projectCreation = Task.Run(() => TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tracked",
                location));
            await twoFactoriesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator = (VersionControlCoordinator?)editorService.ProjectVersionControlCoordinator;
            Assert.That(coordinator, Is.Not.Null);
            disposal = coordinator!.DisposeAsync().AsTask();
            await Task.Delay(100);
            Assert.That(disposal.IsCompleted, Is.False);

            releaseFactories.TrySetResult();
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseRetirement.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(factoryCalls, Is.EqualTo(2));
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseFactories.TrySetResult();
            releaseRetirement.TrySetResult();
            if (construction is not null)
            {
                await construction.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (projectCreation is not null)
            {
                await projectCreation.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_cancels_and_joins_an_initialization_before_retiring_the_backend()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? initialization = null;
        Task? closing = null;
        var identityStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseIdentity = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await CreateProjectForFakeVersionControlAsync(
                "version-control-initialize-close");
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

            initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                async cancellationToken =>
                {
                    identityStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }

                    await releaseIdentity.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return new GitIdentity(
                        "Close Barrier Test",
                        "close-barrier@example.invalid");
                });
            await identityStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            closing = TestShell.Project.CloseProject();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Exception? rejectedOperation = null;
            try
            {
                await coordinator.NotifySavedAsync();
            }
            catch (Exception ex)
            {
                rejectedOperation = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(rejectedOperation, Is.TypeOf<InvalidOperationException>());
                Assert.That(backend.RetirementCalls, Is.Zero);
            });

            releaseIdentity.TrySetResult();
            OperationCanceledException? initializationCancellation = null;
            try
            {
                await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException ex)
            {
                initializationCancellation = ex;
            }

            await closing.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(initializationCancellation, Is.Not.Null);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.RetirementCalls, Is.GreaterThanOrEqualTo(1));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });
        }
        finally
        {
            releaseIdentity.TrySetResult();
            if (initialization is not null)
            {
                try
                {
                    await initialization.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine(ex);
                }
            }

            if (closing is not null)
            {
                try
                {
                    await closing.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine(ex);
                }
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_rejects_operations_until_the_project_change_is_committed()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            subsequentClosingFinalizer = null;
        Task? closing = null;
        var subsequentHandlerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubsequentHandler = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-close-commit-gap");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            subsequentClosingFinalizer = async (_, _) =>
            {
                subsequentHandlerStarted.TrySetResult();
                await releaseSubsequentHandler.Task.WaitAsync(TimeSpan.FromSeconds(5));
            };
            TestShell.Project.ClosingFinalizing += subsequentClosingFinalizer;

            closing = TestShell.Project.CloseProject();
            await subsequentHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            IProjectVersionControlService? coordinatorServiceDuringClose =
                coordinator.CurrentService;
            IProjectVersionControlService? editorServiceDuringClose =
                editorService.ProjectVersionControlService.Value;
            if (coordinatorServiceDuringClose is not null)
            {
                await coordinatorServiceDuringClose.GetStatusAsync(CancellationToken.None);
            }

            if (editorServiceDuringClose is not null
                && !ReferenceEquals(editorServiceDuringClose, coordinatorServiceDuringClose))
            {
                await editorServiceDuringClose.GetStatusAsync(CancellationToken.None);
            }

            Exception? rejectedOperation = null;
            try
            {
                await coordinator.SetLocalIdentityAsync(
                    new GitIdentity("Rejected Close Test", "rejected-close@example.invalid"));
            }
            catch (Exception ex)
            {
                rejectedOperation = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(coordinatorServiceDuringClose, Is.Null);
                Assert.That(editorServiceDuringClose, Is.Null);
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(rejectedOperation, Is.TypeOf<InvalidOperationException>());
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });

            releaseSubsequentHandler.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseSubsequentHandler.TrySetResult();
            if (closing is not null)
            {
                await closing.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (subsequentClosingFinalizer is not null)
            {
                TestShell.Project.ClosingFinalizing -= subsequentClosingFinalizer;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_from_a_state_publication_hides_the_service_before_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? closing = null;
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-reentrant-close-publication");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                RetirementRelease = releaseRetirement.Task,
            };
            var probe = new MutableGitInstallationProbe();
            var config = new VersionControlConfig();
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            bool retirementObserved = false;
            IProjectVersionControlService? coordinatorServiceAtRetirement = backend;
            IProjectVersionControlService? editorServiceAtRetirement = backend;
            bool trackedAtRetirement = true;
            backend.RetirementStarting = () =>
            {
                retirementObserved = true;
                coordinatorServiceAtRetirement = coordinator.CurrentService;
                editorServiceAtRetirement =
                    editorService.ProjectVersionControlService.Value;
                trackedAtRetirement = coordinator.IsTracked.Value;
            };

            using IDisposable subscription = coordinator.IsGitAvailable.Subscribe(
                isAvailable =>
                {
                    if (isAvailable && closing is null)
                    {
                        closing = TestShell.Project.CloseProject();
                    }
                });

            probe.IsInstalled = true;
            GitAvailability availability = await coordinator.GetAvailabilityAsync();
            await WaitUntilAsync(() => retirementObserved);

            Assert.Multiple(() =>
            {
                Assert.That(availability.State, Is.EqualTo(GitAvailabilityState.Installed));
                Assert.That(coordinatorServiceAtRetirement, Is.Null);
                Assert.That(editorServiceAtRetirement, Is.Null);
                Assert.That(trackedAtRetirement, Is.False);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });

            releaseRetirement.TrySetResult();
            await closing!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseRetirement.TrySetResult();
            if (closing is not null)
            {
                await closing.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Close_aborted_before_retirement_keeps_the_backend_visible()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? closing = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>? blockingClosing = null;
        var closingStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var closeCancellation = new CancellationTokenSource();

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pre-retirement-close-abort");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var probe = new MutableGitInstallationProbe();
            var config = new VersionControlConfig();
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            blockingClosing = async (_, cancellationToken) =>
            {
                closingStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            };
            TestShell.Project.Closing += blockingClosing;
            closing = TestShell.Project.CloseProject(closeCancellation.Token);
            await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closeCancellation.Cancel();

            OperationCanceledException? closeFailure = null;
            try
            {
                await closing!.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException ex)
            {
                closeFailure = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(closeFailure, Is.Not.Null);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(coordinator.CurrentService, Is.SameAs(backend));
                Assert.That(
                    editorService.ProjectVersionControlService.Value,
                    Is.SameAs(backend));
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(backend.RetirementCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });

            TestShell.Project.Closing -= blockingClosing;
            blockingClosing = null;
            await TestShell.Project.CloseProject();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.RetirementCalls, Is.GreaterThanOrEqualTo(1));
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });
        }
        finally
        {
            if (blockingClosing is not null)
            {
                TestShell.Project.Closing -= blockingClosing;
            }

            if (closing is not null)
            {
                try
                {
                    await closing.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_finalizer_failure_does_not_abort_the_committed_close()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            throwingClosingFinalizer = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            observingClosingFinalizer = null;
        bool observingFinalizerCalled = false;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-aborted-close-lifecycle");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            throwingClosingFinalizer = (_, _) =>
                throw new InvalidOperationException("Later close finalizer failed.");
            observingClosingFinalizer = (_, cancellationToken) =>
            {
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                observingFinalizerCalled = true;
                return Task.CompletedTask;
            };
            TestShell.Project.ClosingFinalizing += throwingClosingFinalizer;
            TestShell.Project.ClosingFinalizing += observingClosingFinalizer;

            await TestShell.Project.CloseProject();

            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(observingFinalizerCalled, Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });
        }
        finally
        {
            if (throwingClosingFinalizer is not null)
            {
                TestShell.Project.ClosingFinalizing -= throwingClosingFinalizer;
            }

            if (observingClosingFinalizer is not null)
            {
                TestShell.Project.ClosingFinalizing -= observingClosingFinalizer;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Backend_retirement_failure_does_not_abort_committed_close_or_hold_operation_barrier()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-failed-close-retirement");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                RetirementStarting = () =>
                    throw new InvalidOperationException("Expected retirement failure."),
            };
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig
            {
                AutoCommitOnClose = true,
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            await TestShell.Project.CloseProject();
            await coordinator.NotifySavedAsync();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
                Assert.That(backend.RetirementSnapshots, Has.Count.EqualTo(1));
                Assert.That(backend.RetirementSnapshots[0]?.Message,
                    Is.EqualTo("beutl: snapshot on close"));
                Assert.That(backend.RetirementSnapshots[0]?.Kind, Is.EqualTo(SnapshotKind.Close));
            });

            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_continues_when_an_operation_cancellation_callback_throws()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task<bool>? initialization = null;
        var identityStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseIdentity = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await CreateProjectForFakeVersionControlAsync(
                "version-control-throwing-cancellation-disposal");
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

            initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                async cancellationToken =>
                {
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        static () => throw new InvalidOperationException(
                            "Cancellation callback failure."));
                    identityStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }

                    await releaseIdentity.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return null;
                });
            await identityStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Exception? synchronousDisposalFailure = null;
            Task? disposal = null;
            try
            {
                disposal = coordinator.DisposeAsync().AsTask();
            }
            catch (Exception ex)
            {
                synchronousDisposalFailure = ex;
            }

            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(synchronousDisposalFailure, Is.Null);
                Assert.That(disposal, Is.Not.Null);
                Assert.That(disposal?.IsCompleted, Is.False);
            });

            releaseIdentity.TrySetResult();
            Assert.That(await initialization.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
            await disposal!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseIdentity.TrySetResult();
            if (initialization is not null)
            {
                await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_joins_an_active_conflict_marker_warning()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? opening = null;
        var warningStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWarning = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-opening-warning-disposal");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "blocked-warning.scene"),
                "<<<<<<< ours\n{}\n=======\n{}\n>>>>>>> theirs\n");
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
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            coordinator.WarnConflictMarkersAsync = async _ =>
            {
                warningStarted.TrySetResult();
                await releaseWarning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            };

            opening = TestShell.Project.OpenProject(project.Uri.LocalPath);
            await warningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = coordinator.DisposeAsync().AsTask();
            Task completed = await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.Multiple(() =>
            {
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(completed, Is.Not.SameAs(disposal));
            });

            releaseWarning.TrySetResult();
            await opening.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseWarning.TrySetResult();
            if (opening is not null)
            {
                await opening.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_joins_an_active_close_barrier_before_clearing_state()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? closing = null;
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-close-barrier-disposal");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            closing = TestShell.Project.CloseProject();
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = coordinator.DisposeAsync().AsTask();
            Task completed = await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.Multiple(() =>
            {
                Assert.That(completed, Is.Not.SameAs(disposal));
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
            });

            releaseRetirement.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });
        }
        finally
        {
            releaseRetirement.TrySetResult();
            if (closing is not null)
            {
                await closing.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Git_executable_override_rediscoveries_only_an_unassociated_backend()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-override-reactivation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                    };
                    createdBackends.Add(backend);
                    return backend;
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value
                && !coordinator.IsTracked.Value);
            IProjectVersionControlService unavailableService = coordinator.CurrentService!;

            config.AutoCommitOnSave = !config.AutoCommitOnSave;
            HeadlessTestHelpers.Settle();
            Assert.That(coordinator.CurrentService, Is.SameAs(unavailableService));

            config.GitExecutablePath = validGitPath;
            await WaitUntilAsync(() =>
                coordinator.CurrentService?.Repository is not null
                && coordinator.IsGitAvailable.Value
                && coordinator.IsTracked.Value
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    coordinator.CurrentService));
            IProjectVersionControlService trackedService = coordinator.CurrentService!;

            int backendCountAfterTracking = createdBackends.Count;
            config.GitExecutablePath = invalidGitPath;
            await WaitUntilAsync(() => !coordinator.IsGitAvailable.Value);

            Assert.Multiple(() =>
            {
                Assert.That(trackedService.Repository, Is.Not.Null);
                Assert.That(coordinator.CurrentService, Is.SameAs(trackedService));
                Assert.That(editorService.ProjectVersionControlService.Value,
                    Is.SameAs(trackedService));
                Assert.That(coordinator.IsGitAvailable.Value, Is.False);
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(createdBackends, Has.Count.EqualTo(backendCountAfterTracking));
                Assert.That(
                    createdBackends.Single(backend => ReferenceEquals(backend, trackedService))
                        .RetirementCalls,
                    Is.Zero);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Lfs_configuration_reapplies_hygiene_to_the_open_tracked_backend()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var appliedValues = new List<bool>();

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-lfs-config-hygiene");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var config = new VersionControlConfig
            {
                UseLfsWhenAvailable = false,
            };
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                EnsureHygieneOverride = _ =>
                {
                    lock (appliedValues)
                    {
                        appliedValues.Add(config.UseLfsWhenAvailable);
                    }

                    return Task.CompletedTask;
                },
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);
            lock (appliedValues)
            {
                appliedValues.Clear();
            }

            config.UseLfsWhenAvailable = true;
            await WaitUntilAsync(() =>
            {
                lock (appliedValues)
                {
                    return appliedValues.Count >= 1;
                }
            });

            config.UseLfsWhenAvailable = false;
            await WaitUntilAsync(() =>
            {
                lock (appliedValues)
                {
                    return appliedValues.Count >= 2;
                }
            });

            bool[] snapshot;
            lock (appliedValues)
            {
                snapshot = appliedValues.ToArray();
            }

            Assert.Multiple(() =>
            {
                Assert.That(snapshot, Is.EqualTo(new[] { true, false }));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(coordinator.CurrentService, Is.SameAs(backend));
                Assert.That(editorService.ProjectVersionControlService.Value, Is.SameAs(backend));
                Assert.That(backend.RetirementCalls, Is.Zero);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Newer_git_configuration_cancels_a_stale_unassociated_rediscovery()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hygieneCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestInvalidProbeCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-stale-git-configuration");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-stale-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-stale-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            PullCycleTestBackend? staleTrackedBackend = null;
            var editorService = new EditorService(new ExtensionProvider());
            var publishedServices = new List<IProjectVersionControlService?>();
            using IDisposable subscription = editorService.ProjectVersionControlService.Subscribe(
                publishedServices.Add);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    bool blockHygiene = candidate is not null && staleTrackedBackend is null;
                    var backend = new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = async cancellationToken =>
                        {
                            GitAvailability availability = await locator.LocateAsync(
                                cancellationToken);
                            if (availability.State != GitAvailabilityState.Installed
                                && staleTrackedBackend is not null)
                            {
                                latestInvalidProbeCompleted.TrySetResult();
                            }

                            return availability;
                        },
                        EnsureHygieneStarted = blockHygiene ? hygieneStarted : null,
                        EnsureHygieneRelease = blockHygiene ? releaseHygiene.Task : null,
                        EnsureHygieneCompleted = blockHygiene ? hygieneCompleted : null,
                    };
                    if (blockHygiene)
                    {
                        staleTrackedBackend = backend;
                    }

                    return backend;
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);

            config.GitExecutablePath = validGitPath;
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            config.GitExecutablePath = invalidGitPath;
            await hygieneCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                staleTrackedBackend is { RetirementCalls: 1, DisposeCalls: 1 });
            await latestInvalidProbeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value
                && !coordinator.IsTracked.Value
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    coordinator.CurrentService));

            Assert.Multiple(() =>
            {
                Assert.That(staleTrackedBackend, Is.Not.Null);
                Assert.That(publishedServices, Does.Not.Contain(staleTrackedBackend));
                Assert.That(coordinator.CurrentService, Is.Not.SameAs(staleTrackedBackend));
                Assert.That(editorService.ProjectVersionControlService.Value,
                    Is.SameAs(coordinator.CurrentService));
                Assert.That(staleTrackedBackend!.RetirementCalls, Is.EqualTo(1));
                Assert.That(staleTrackedBackend.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Waiting_operation_does_not_interleave_between_coalesced_git_configurations()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var staleHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestActivationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLatestActivation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleInitializationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleInitialization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestInitializationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-coalesced-git-config-waiter");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-coalesced-valid-git");
            string invalidGitPath = Path.Combine(
                Path.GetTempPath(),
                "beutl-coalesced-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            int untrackedBackends = 0;
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    int untrackedIndex = candidate is null
                        ? Interlocked.Increment(ref untrackedBackends)
                        : 0;
                    return new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = untrackedIndex == 3
                            ? async cancellationToken =>
                            {
                                latestActivationStarted.TrySetResult();
                                await releaseLatestActivation.Task.WaitAsync(cancellationToken);
                                return await locator.LocateAsync(cancellationToken);
                            }
                        : locator.LocateAsync,
                        EnsureHygieneStarted = candidate is not null
                            ? staleHygieneStarted
                            : null,
                        EnsureHygieneRelease = candidate is not null
                            ? releaseStaleHygiene.Task
                            : null,
                        InitializeStarted = untrackedIndex switch
                        {
                            2 => staleInitializationStarted,
                            3 => latestInitializationStarted,
                            _ => null,
                        },
                        InitializeRelease = untrackedIndex == 2
                            ? releaseStaleInitialization.Task
                            : null,
                    };
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);

            config.GitExecutablePath = validGitPath;
            await staleHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var synchronousReadiness = new TaskCompletionSource();
            System.Reflection.FieldInfo? readinessField =
                typeof(VersionControlCoordinator).GetField(
                    "_configurationActivationQuiesced",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
            Assert.That(readinessField, Is.Not.Null);
            readinessField!.SetValue(coordinator, synchronousReadiness);

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(null));
            Assert.That(initialization.IsCompleted, Is.False);

            config.GitExecutablePath = invalidGitPath;
            Task firstActivation = await Task.WhenAny(
                    latestActivationStarted.Task,
                    staleInitializationStarted.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(firstActivation, Is.SameAs(latestActivationStarted.Task));
                Assert.That(initialization.IsCompleted, Is.False);
                Assert.That(staleInitializationStarted.Task.IsCompleted, Is.False);
            });

            releaseLatestActivation.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);
            await latestInitializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.IsGitAvailable.Value, Is.False);
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(staleInitializationStarted.Task.IsCompleted, Is.False);
            });
        }
        finally
        {
            releaseStaleHygiene.TrySetResult();
            releaseLatestActivation.TrySetResult();
            releaseStaleInitialization.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Git_configuration_change_during_disposal_does_not_start_a_new_activation()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Task? disposal = null;
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-config-disposal");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-disposal-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-disposal-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            int factoryCalls = 0;
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        RetirementStarted = retirementStarted,
                        RetirementRelease = releaseRetirement.Task,
                    };
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);
            int callsBeforeDisposal = Volatile.Read(ref factoryCalls);

            disposal = coordinator.DisposeAsync().AsTask();
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            config.GitExecutablePath = validGitPath;

            Assert.Multiple(() =>
            {
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(Volatile.Read(ref factoryCalls), Is.EqualTo(callsBeforeDisposal));
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });

            releaseRetirement.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseRetirement.TrySetResult();
            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Git_configuration_rediscovery_waits_for_inflight_initialization()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var initializeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialize = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-config-initialize");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-init-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-init-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    bool initialBackend = createdBackends.Count == 0;
                    var backend = new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        InitializeStarted = initialBackend ? initializeStarted : null,
                        InitializeRelease = initialBackend ? releaseInitialize.Task : null,
                    };
                    createdBackends.Add(backend);
                    return backend;
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);
            var initialService = (PullCycleTestBackend)coordinator.CurrentService!;

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(null));
            await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            config.GitExecutablePath = validGitPath;

            Assert.Multiple(() =>
            {
                Assert.That(initialization.IsCompleted, Is.False);
                Assert.That(createdBackends, Has.Count.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.SameAs(initialService));
            });

            releaseInitialize.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, initialService)
                && coordinator.IsGitAvailable.Value
                && coordinator.IsTracked.Value);

            Assert.Multiple(() =>
            {
                Assert.That(initialService.Repository, Is.Not.Null);
                Assert.That(createdBackends, Has.Count.EqualTo(1));
                Assert.That(initialService.RetirementCalls, Is.Zero);
            });
        }
        finally
        {
            releaseInitialize.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Initialization_started_during_git_rediscovery_waits_for_activation()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-config-wait-initialize");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-init-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-init-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        Branches =
                        [
                            new BranchInfo("main", true, null),
                            new BranchInfo("configuration-ready", false, null),
                        ],
                        EnsureHygieneStarted = candidate is not null ? hygieneStarted : null,
                        EnsureHygieneRelease = candidate is not null ? releaseHygiene.Task : null,
                    };
                    createdBackends.Add(backend);
                    return backend;
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value
                && !coordinator.IsTracked.Value);

            config.GitExecutablePath = validGitPath;
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(null));

            Assert.That(initialization.IsCompleted, Is.False);

            releaseHygiene.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);
            var trackedBackend = (PullCycleTestBackend)coordinator.CurrentService!;

            Assert.Multiple(() =>
            {
                Assert.That(trackedBackend.Repository, Is.EqualTo(repository));
                Assert.That(trackedBackend.InitializeCalls, Is.EqualTo(1));
                Assert.That(coordinator.IsGitAvailable.Value, Is.True);
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(createdBackends, Does.Contain(trackedBackend));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Branch_started_during_git_rediscovery_waits_for_activation()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var branchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-config-wait-branch");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-branch-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-branch-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        Branches =
                        [
                            new BranchInfo("main", true, null),
                            new BranchInfo("configuration-ready", false, null),
                        ],
                        EnsureHygieneStarted = candidate is not null ? hygieneStarted : null,
                        EnsureHygieneRelease = candidate is not null ? releaseHygiene.Task : null,
                        SwitchBranchStarted = candidate is not null ? branchStarted : null,
                    };
                    createdBackends.Add(backend);
                    return backend;
                });
            coordinator.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value
                && !coordinator.IsTracked.Value);

            config.GitExecutablePath = validGitPath;
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> branch = coordinator.SwitchBranchAsync("configuration-ready");

            Assert.Multiple(() =>
            {
                Assert.That(branch.IsCompleted, Is.False);
                Assert.That(branchStarted.Task.IsCompleted, Is.False);
            });

            releaseHygiene.TrySetResult();
            Assert.That(await branch.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            await branchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var trackedBackend = (PullCycleTestBackend)coordinator.CurrentService!;

            Assert.Multiple(() =>
            {
                Assert.That(trackedBackend.Repository, Is.EqualTo(repository));
                Assert.That(trackedBackend.RetirementCalls, Is.Zero);
                Assert.That(coordinator.IsGitAvailable.Value, Is.True);
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(createdBackends, Does.Contain(trackedBackend));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Git_configuration_change_during_branch_cycle_preserves_backend_identity()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var branchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBranch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-config-branch");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-branch-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-branch-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = validGitPath,
                UseLfsWhenAvailable = false,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            int hygieneCalls = 0;
            bool appliedUseLfsWhenAvailable = false;
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                AvailabilityOverride = locator.LocateAsync,
                Branches =
                [
                    new BranchInfo("main", true, null),
                    new BranchInfo("config-change", false, null),
                ],
                SwitchBranchStarted = branchStarted,
                SwitchBranchRelease = releaseBranch.Task,
                EnsureHygieneOverride = _ =>
                {
                    Volatile.Write(
                        ref appliedUseLfsWhenAvailable,
                        config.UseLfsWhenAvailable);
                    Interlocked.Increment(ref hygieneCalls);
                    return Task.CompletedTask;
                },
            };
            int factoryCalls = 0;
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: _ =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return backend;
                });
            coordinator.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsGitAvailable.Value
                && coordinator.IsTracked.Value);
            int callsBeforeBranch = Volatile.Read(ref factoryCalls);
            int hygieneCallsBeforeBranch = Volatile.Read(ref hygieneCalls);

            Task<bool> branch = coordinator.SwitchBranchAsync("config-change");
            await branchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);

            config.GitExecutablePath = invalidGitPath;
            config.UseLfsWhenAvailable = true;

            Assert.Multiple(() =>
            {
                Assert.That(branch.IsCompleted, Is.False);
                Assert.That(Volatile.Read(ref factoryCalls), Is.EqualTo(callsBeforeBranch));
                Assert.That(
                    Volatile.Read(ref hygieneCalls),
                    Is.EqualTo(hygieneCallsBeforeBranch));
                Assert.That(backend.RetirementCalls, Is.Zero);
            });

            releaseBranch.TrySetResult();
            Assert.That(await branch.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && !coordinator.IsGitAvailable.Value
                && coordinator.IsTracked.Value);
            await WaitUntilAsync(() =>
                Volatile.Read(ref hygieneCalls) == hygieneCallsBeforeBranch + 1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    TestShell.Project.CurrentProject.Value?.Uri?.LocalPath,
                    Is.EqualTo(project.Uri!.LocalPath));
                Assert.That(editorService.ProjectVersionControlService.Value, Is.SameAs(backend));
                Assert.That(Volatile.Read(ref factoryCalls), Is.EqualTo(callsBeforeBranch));
                Assert.That(
                    Volatile.Read(ref hygieneCalls),
                    Is.EqualTo(hygieneCallsBeforeBranch + 1));
                Assert.That(Volatile.Read(ref appliedUseLfsWhenAvailable), Is.True);
                Assert.That(backend.RetirementCalls, Is.Zero);
            });
        }
        finally
        {
            releaseBranch.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Close_barrier_defers_git_rediscovery_and_retires_the_tracked_backend_once()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>? blockingClosing = null;
        Task? abortedClose = null;
        var closingStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var trackedHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var trackedRetirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTrackedRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var closeCancellation = new CancellationTokenSource();

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-git-config-close");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-close-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-close-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
                AutoCommitOnClose = true,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                locator,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, repository, tip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        EnsureHygieneStarted = candidate is not null
                            ? trackedHygieneStarted
                            : null,
                        RetirementStarted = candidate is not null
                            ? trackedRetirementStarted
                            : null,
                        RetirementRelease = candidate is not null
                            ? releaseTrackedRetirement.Task
                            : null,
                    };
                    createdBackends.Add(backend);
                    return backend;
                });
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);
            int callsBeforeClose = createdBackends.Count;

            blockingClosing = async (_, cancellationToken) =>
            {
                closingStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            };
            TestShell.Project.Closing += blockingClosing;
            abortedClose = TestShell.Project.CloseProject(closeCancellation.Token);
            await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            config.GitExecutablePath = validGitPath;

            Assert.Multiple(() =>
            {
                Assert.That(abortedClose.IsCompleted, Is.False);
                Assert.That(createdBackends, Has.Count.EqualTo(callsBeforeClose));
                Assert.That(trackedHygieneStarted.Task.IsCompleted, Is.False);
            });

            closeCancellation.Cancel();
            OperationCanceledException? closeFailure = null;
            try
            {
                await abortedClose.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException ex)
            {
                closeFailure = ex;
            }
            Assert.That(closeFailure, Is.Not.Null);
            await trackedHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: not null }
                && coordinator.IsTracked.Value);
            var trackedBackend = (PullCycleTestBackend)coordinator.CurrentService!;
            await WaitUntilAsync(() =>
                createdBackends
                    .Where(backend => !ReferenceEquals(backend, trackedBackend))
                    .All(backend => backend.RetirementCalls == 1));

            TestShell.Project.Closing -= blockingClosing;
            blockingClosing = null;
            int callsBeforeSuccessfulClose = createdBackends.Count;
            Task successfulClose = TestShell.Project.CloseProject();
            await trackedRetirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            config.GitExecutablePath = invalidGitPath;

            Assert.Multiple(() =>
            {
                Assert.That(successfulClose.IsCompleted, Is.False);
                Assert.That(createdBackends, Has.Count.EqualTo(callsBeforeSuccessfulClose));
                Assert.That(trackedBackend.RetirementCalls, Is.EqualTo(1));
                Assert.That(trackedBackend.RetirementSnapshots, Has.Count.EqualTo(1));
                Assert.That(trackedBackend.RetirementSnapshots[0]?.Kind,
                    Is.EqualTo(SnapshotKind.Close));
            });

            releaseTrackedRetirement.TrySetResult();
            await successfulClose.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(createdBackends, Has.Count.EqualTo(callsBeforeSuccessfulClose));
                Assert.That(trackedBackend.RetirementCalls, Is.EqualTo(1));
                Assert.That(trackedBackend.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseTrackedRetirement.TrySetResult();
            if (blockingClosing is not null)
            {
                TestShell.Project.Closing -= blockingClosing;
            }

            if (abortedClose is not null)
            {
                try
                {
                    await abortedClose.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Disposal_cancels_inflight_git_availability_probes()
    {
        await TestReset.ResetShellAsync();
        var probe = new BlockingGitInstallationProbe();
        var config = new VersionControlConfig();
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
        var editorService = new EditorService(new ExtensionProvider());
        int servicePublications = 0;
        using IDisposable subscription = editorService.ProjectVersionControlService.Subscribe(
            _ => servicePublications++);
        var coordinator = new VersionControlCoordinator(
            TestShell.Project,
            editorService,
            config,
            locator);

        try
        {
            Task<GitAvailability> explicitProbe = coordinator.GetAvailabilityAsync();
            await probe.TwoProbesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = coordinator.DisposeAsync().AsTask();
            await probe.TwoProbesCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(disposal.IsCompleted, Is.False);
            probe.ReleaseCancelledProbes.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            OperationCanceledException? cancellation = null;
            try
            {
                await explicitProbe;
            }
            catch (OperationCanceledException ex)
            {
                cancellation = ex;
            }

            int publicationsAfterDisposal = servicePublications;
            config.GitExecutablePath = "/git-after-disposal";
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(cancellation, Is.Not.Null);
                Assert.That(probe.CancellationCount, Is.EqualTo(2));
                Assert.That(probe.CompletionCount, Is.EqualTo(2));
                Assert.That(servicePublications, Is.EqualTo(publicationsAfterDisposal));
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            probe.ReleaseCancelledProbes.TrySetResult();
            await coordinator.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_cancels_and_joins_an_active_lock_recovery_prompt()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-lock-recovery-disposal");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            coordinator.ConfirmRemoveStaleLockAsync = async (_, cancellationToken) =>
            {
                confirmationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    confirmationCancelled.TrySetResult();
                    throw;
                }
            };

            backend.RaiseRecoverableLock(new RepositoryLockInfo(
                Path.Combine(projectRoot, ".git", "index.lock"),
                DateTimeOffset.UtcNow));
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = coordinator.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            await confirmationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backend.RemoveRecoverableLockCalls, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_waits_for_backend_retirement()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-retirement-disposal");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
            };
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                GlobalConfiguration.Instance.VersionControlConfig,
                installationLocator: null,
                serviceFactory: _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    editorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            IProjectVersionControlService? coordinatorServiceAtRetirement = backend;
            IProjectVersionControlService? editorServiceAtRetirement = backend;
            bool trackedAtRetirement = true;
            backend.RetirementStarting = () =>
            {
                coordinatorServiceAtRetirement = coordinator.CurrentService;
                editorServiceAtRetirement =
                    editorService.ProjectVersionControlService.Value;
                trackedAtRetirement = coordinator.IsTracked.Value;
            };

            Task disposal = coordinator.DisposeAsync().AsTask();
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(disposal.IsCompleted, Is.False);
            releaseRetirement.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(coordinatorServiceAtRetirement, Is.Null);
                Assert.That(editorServiceAtRetirement, Is.Null);
                Assert.That(trackedAtRetirement, Is.False);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseRetirement.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

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
    public async Task Branch_creation_rejects_invalid_existing_and_shorthand_names_before_confirmation_or_close()
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

            (Project project, EditViewModel editor) = await CreateTrackedProjectAsync(
                "version-control-create-branch-validation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            await RunGitAsync(gitPath, projectRoot, "branch", "existing");
            await RunGitAsync(gitPath, projectRoot, "switch", "existing");
            await RunGitAsync(gitPath, projectRoot, "switch", "main");
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            AddRectangle(adder, layer: 0);
            project.Variables[RestoreStateKey] = "unsaved-state";
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

            bool invalid = await TestShell.VersionControl.CreateBranchAsync("invalid..branch");
            bool existing = await TestShell.VersionControl.CreateBranchAsync("existing");
            bool shorthand = await TestShell.VersionControl.CreateBranchAsync("@{-1}");
            HeadlessTestHelpers.Settle();

            WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
                .GetStatusAsync(CancellationToken.None);
            IReadOnlyList<CommitInfo> history =
                await TestShell.VersionControl.CurrentService.GetHistoryAsync(
                    0,
                    20,
                    CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(invalid, Is.False);
                Assert.That(existing, Is.False);
                Assert.That(shorthand, Is.False);
                Assert.That(confirmations, Is.Zero);
                Assert.That(status.Branch, Is.EqualTo("main"));
                Assert.That(status.IsClean, Is.False);
                Assert.That(
                    history.Any(commit => commit.Kind == SnapshotKind.Safety),
                    Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(editor.HistoryManager.CanUndo, Is.True);
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
    public async Task Branch_switch_rejects_Git_previous_branch_shorthand_before_confirmation_or_close()
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

            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-branch-shorthand");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            await RunGitAsync(gitPath, projectRoot, "branch", "alternate");
            await RunGitAsync(gitPath, projectRoot, "switch", "alternate");
            await RunGitAsync(gitPath, projectRoot, "switch", "main");
            int confirmations = 0;
            TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            bool switched = await TestShell.VersionControl.SwitchBranchAsync("-");
            HeadlessTestHelpers.Settle();
            WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
                .GetStatusAsync(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(switched, Is.False);
                Assert.That(confirmations, Is.Zero);
                Assert.That(status.Branch, Is.EqualTo("main"));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
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
    public async Task Branch_switch_revalidates_the_local_branch_before_closing_the_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-branch-revalidation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, tip);
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                SwitchBranchStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously),
            };
            backend.EnqueueBranchSnapshot(
                [
                    new BranchInfo("main", true, null),
                    new BranchInfo("target", false, null),
                ]);
            backend.EnqueueBranchSnapshot([new BranchInfo("main", true, null)]);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig();
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            int confirmations = 0;
            coordinator.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool switched = await coordinator.SwitchBranchAsync("target");
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(switched, Is.False);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(backend.GetBranchesCalls, Is.EqualTo(2));
                Assert.That(backend.SwitchBranchStarted!.Task.IsCompleted, Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Branch_creation_revalidates_the_name_before_closing_the_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-create-branch-revalidation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, tip);
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                Status = new WorkspaceStatus(
                    "main",
                    Ahead: 0,
                    Behind: 0,
                    Changes: [],
                    HasConflicts: false),
            };
            backend.EnqueueCanCreateBranchResult(true);
            backend.EnqueueCanCreateBranchResult(false);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig();
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            int confirmations = 0;
            coordinator.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool created = await coordinator.CreateBranchAsync("target");
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.False);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(backend.CanCreateBranchCalls, Is.EqualTo(2));
                Assert.That(backend.CreateBranchCalls, Is.Zero);
                Assert.That(backend.CommitAllCalls, Is.Zero);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Branch_creation_revalidates_after_the_safety_snapshot_before_closing_the_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-create-branch-post-snapshot-revalidation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, tip);
            var backend = new PullCycleTestBackend(repository, repository, tip);
            backend.EnqueueCanCreateBranchResult(true);
            backend.EnqueueCanCreateBranchResult(true);
            backend.EnqueueCanCreateBranchResult(false);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig();
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            int confirmations = 0;
            coordinator.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool created = await coordinator.CreateBranchAsync("target");
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.False);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(backend.CanCreateBranchCalls, Is.EqualTo(3));
                Assert.That(backend.CreateBranchCalls, Is.Zero);
                Assert.That(backend.CommitAllCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
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
            RemoteOpResult pullResult = await TestShell.VersionControl.PullAsync();
            Assert.That(
                pullResult,
                Is.TypeOf<RemoteOpResult.Success>(),
                () => pullResult is RemoteOpResult.Failed failed
                    ? failed.Stderr
                    : pullResult.ToString());
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
    public async Task Pull_up_to_date_clean_project_preserves_the_open_editor_session()
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
                "version-control-pull-up-to-date");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, originalTip);
            var backend = new PullCycleTestBackend(repository, repository, originalTip)
            {
                Status = new WorkspaceStatus(
                    "main",
                    Ahead: 0,
                    Behind: 0,
                    Changes: [],
                    HasConflicts: false),
                PreflightResult = new PullPreflightResult(
                    new RemoteOpResult.Success(),
                    RequiresTransition: false),
                PullResult = new FastForwardPullResult(
                    new RemoteOpResult.Success(),
                    originalTip),
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

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(backend.CheckpointCreateCalls, Is.Zero);
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
    public async Task Normal_close_completes_while_pull_confirmation_is_pending()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pull-close-lock-order");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            coordinator.ConfirmPullAsync = async cancellationToken =>
            {
                confirmationStarted.TrySetResult();
                try
                {
                    return await releaseConfirmation.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    confirmationCancelled.TrySetResult();
                    throw;
                }
            };

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            await confirmationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            RemoteOpResult result = await pull.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
                Assert.That(tracked.PullCalls, Is.Zero);
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Project_close_during_pull_preflight_cancels_the_captured_epoch()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var preflightStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreflight = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pull-preflight-epoch");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                PreflightStarted = preflightStarted,
                PreflightRelease = releasePreflight.Task,
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            int confirmations = 0;
            coordinator.ConfirmPullAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await preflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            RemoteOpResult result = await pull.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
                Assert.That(confirmations, Is.Zero);
                Assert.That(tracked.PreflightCalls, Is.EqualTo(1));
                Assert.That(tracked.PullCalls, Is.Zero);
            });
        }
        finally
        {
            releasePreflight.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Branch_transition_cancels_a_pending_pull_confirmation()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pull-branch-epoch");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            coordinator.ConfirmPullAsync = async cancellationToken =>
            {
                confirmationStarted.TrySetResult();
                try
                {
                    return await releaseConfirmation.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    confirmationCancelled.TrySetResult();
                    throw;
                }
            };
            coordinator.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bool branchCreated = await coordinator.CreateBranchAsync(
                    "cancel-pull-confirmation",
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await confirmationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            RemoteOpResult pullResult = await pull.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(branchCreated, Is.True);
                Assert.That(pullResult, Is.TypeOf<RemoteOpResult.Failed>());
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(tracked.PullCalls, Is.Zero);
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Normal_close_completes_while_manual_pull_recovery_confirmation_is_pending()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-manual-recovery-close-lock-order");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator.ConfirmPendingPullRecoveryAsync = async (_, cancellationToken) =>
            {
                confirmationStarted.TrySetResult();
                try
                {
                    return await releaseConfirmation.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    confirmationCancelled.TrySetResult();
                    throw;
                }
            };

            Task<ProjectRecoveryResult> recovery =
                coordinator.RecoverPendingPullAsync(pending.Id);
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            await confirmationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ProjectRecoveryResult result =
                await recovery.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(result, Is.TypeOf<ProjectRecoveryResult.Unavailable>());
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Project_close_during_pending_recovery_lookup_cancels_the_captured_epoch()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var lookupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-recovery-lookup-epoch");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            tracked.PendingPullLookupStarted = lookupStarted;
            tracked.PendingPullLookupRelease = releaseLookup.Task;
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            Task<ProjectRecoveryResult> recovery =
                coordinator.RecoverPendingPullAsync(pending.Id);
            await lookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            ProjectRecoveryResult result =
                await recovery.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<ProjectRecoveryResult.Unavailable>());
                Assert.That(confirmations, Is.Zero);
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
            });
        }
        finally
        {
            releaseLookup.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Pending_pull_recovery_returns_unavailable_when_project_closes_before_lookup()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-recovery-close-before-lookup");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);

            await TestShell.Project.CloseProject();
            ProjectRecoveryResult result = await coordinator.RecoverPendingPullAsync(
                pending.Id,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<ProjectRecoveryResult.Unavailable>());
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Pending_pull_recovery_completion_failure_reports_uncertain_retention()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-recovery-completion-failure");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                CompletePendingPullOverride = _ =>
                    Task.FromException(new IOException("simulated completion failure")),
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) => Task.FromResult(true);

            ProjectRecoveryResult result = await coordinator.RecoverPendingPullAsync(
                pending.Id,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.TypeOf<ProjectRecoveryResult.FailedUncertain>());
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(tracked.CompletePendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Uncertain_pull_recovery_confirmation_holds_no_lifecycle_project_or_backend_gate()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnSave = false;
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-uncertain-prompt-gates");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var pulledTip = new CheckedOutBranchTip(
                originalTip.RefName,
                "2222222222222222222222222222222222222222");
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            var recovery = new PendingPullRecovery(
                "uncertain-prompt",
                "refs/beutl/recovery/test/uncertain-prompt",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                checkpoint,
                pulledTip,
                projectFile,
                DateTimeOffset.UtcNow);
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                PullResult = new FastForwardPullResult(
                    new RemoteOpResult.Failed("pull failed"),
                    pulledTip,
                    PullTransitionState.OwnershipLost,
                    pulledTip,
                    recovery),
            };
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
            coordinator.ConfirmPendingPullRecoveryAsync = async (_, _) =>
            {
                confirmationStarted.TrySetResult();
                return await releaseConfirmation.Task;
            };
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(TestShell.Project.CurrentTransition, Is.Null);
            await TestShell.Project.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            bool backendGateEntered = await tracked.ExecuteExclusiveAsync(
                    _ => Task.FromResult(true),
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            RemoteOpResult closedProjectPull = await coordinator.PullAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            releaseConfirmation.TrySetResult(false);
            RemoteOpResult result = await pull.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backendGateEntered, Is.True);
                Assert.That(closedProjectPull, Is.TypeOf<RemoteOpResult.Failed>());
                Assert.That(result,
                    Is.EqualTo(new RemoteOpResult.Failed(
                        Strings.VersionControl_PullTransitionUncertain)));
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Project_activation_offers_and_recovers_pending_pull_without_opening_the_tab()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var offerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnSave = false;
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pending-pull-activation");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(
                repository: null,
                repository,
                originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int offers = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (recovery, _) =>
            {
                offers++;
                Assert.That(recovery.Id, Is.EqualTo(pending.Id));
                offerStarted.TrySetResult();
                return Task.FromResult(true);
            };
            releaseHygiene.TrySetResult();

            await offerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => tracked.CompletePendingPullCalls == 1);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(offers, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(
                    TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(projectFile));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Explicit_recent_open_can_accept_recovery_after_declining_the_background_offer()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundOfferDeclined = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-reoffer-on-recent-open");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                if (confirmations == 1)
                {
                    backgroundOfferDeclined.TrySetResult();
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            };
            releaseHygiene.TrySetResult();
            await backgroundOfferDeclined.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var menu = new MenuBarViewModel(TestShell.Project, editorService, coordinator);

            await menu.OpenRecentProject.ExecuteAsync(projectFile);
            await WaitUntilAsync(() => tracked.CompletePendingPullCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(confirmations, Is.EqualTo(2));
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(projectFile));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Explicit_open_confirmation_holds_no_project_or_backend_gate_and_close_cancels_it()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project target = await CreateProjectForFakeVersionControlAsync(
                "version-control-explicit-open-prompt-target");
            string projectFile = target.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            await TestShell.Project.CloseProject();
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            coordinator.ConfirmPendingPullRecoveryAsync = async (_, cancellationToken) =>
            {
                confirmationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    confirmationCancelled.TrySetResult();
                    throw;
                }
            };

            Task opening = projectService.OpenProject(projectFile);
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(projectService.CurrentTransition, Is.Null);
            bool backendGateEntered = await tracked.ExecuteExclusiveAsync(
                    _ => Task.FromResult(true),
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            await projectService.CloseProject().WaitAsync(TimeSpan.FromSeconds(5));
            await confirmationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await opening.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backendGateEntered, Is.True);
                Assert.That(projectService.CurrentTransition, Is.Null);
                Assert.That(projectService.CurrentProject.Value, Is.Null);
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Explicit_open_revalidates_the_exact_recovery_after_confirmation()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project target = await CreateProjectForFakeVersionControlAsync(
                "version-control-explicit-open-revalidation-target");
            string projectFile = target.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            Project current = await CreateProjectForFakeVersionControlAsync(
                "version-control-explicit-open-revalidation-current");
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, null, originalTip)
            {
                DiscoverRepositoryOverride = candidateRoot =>
                    RepositoryPathComparer.AreEquivalent(candidateRoot, projectRoot)
                        ? repository
                        : null,
            };
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                tracked.RemovePendingPullRecovery(pending.Id);
                return Task.FromResult(true);
            };

            await projectService.OpenProject(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.SameAs(current));
                Assert.That(projectService.CurrentTransition, Is.Null);
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Explicit_open_reuses_matching_enclosing_repository_consent_once()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project target = await CreateProjectForFakeVersionControlAsync(
                "version-control-enclosing-consent-once");
            string projectFile = target.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            string repositoryRoot = Path.GetDirectoryName(projectRoot)!;
            await TestShell.Project.CloseProject();
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(repositoryRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmUseEnclosingRepositoryAsync = (candidate, _) =>
            {
                Assert.That(candidate, Is.EqualTo(repository));
                Interlocked.Increment(ref confirmations);
                return Task.FromResult(true);
            };

            await projectService.OpenProject(projectFile);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

            Assert.Multiple(() =>
            {
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(projectService.CurrentProject.Value, Is.Not.Null);
                Assert.That(projectService.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(projectFile));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Changed_recovery_does_not_leak_enclosing_consent_to_the_next_open_attempt()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project target = await CreateProjectForFakeVersionControlAsync(
                "version-control-enclosing-consent-recovery-race");
            string projectFile = target.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            string repositoryRoot = Path.GetDirectoryName(projectRoot)!;
            await TestShell.Project.CloseProject();
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(repositoryRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int enclosingConfirmations = 0;
            int recoveryConfirmations = 0;
            coordinator.ConfirmUseEnclosingRepositoryAsync = (_, _) =>
            {
                Interlocked.Increment(ref enclosingConfirmations);
                return Task.FromResult(true);
            };
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                Interlocked.Increment(ref recoveryConfirmations);
                tracked.RemovePendingPullRecovery(pending.Id);
                return Task.FromResult(true);
            };

            await projectService.OpenProject(projectFile);
            Assert.That(projectService.CurrentProject.Value, Is.Null);

            await projectService.OpenProject(projectFile);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

            Assert.Multiple(() =>
            {
                Assert.That(enclosingConfirmations, Is.EqualTo(2));
                Assert.That(recoveryConfirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(projectService.CurrentProject.Value, Is.Not.Null);
                Assert.That(projectService.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(projectFile));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Accepted_explicit_open_recovery_failure_keeps_the_current_project_open()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project target = await CreateProjectForFakeVersionControlAsync(
                "version-control-explicit-open-failure-target");
            string projectFile = target.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            Project current = await CreateProjectForFakeVersionControlAsync(
                "version-control-explicit-open-failure-current");
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, null, originalTip)
            {
                DiscoverRepositoryOverride = candidateRoot =>
                    RepositoryPathComparer.AreEquivalent(candidateRoot, projectRoot)
                        ? repository
                        : null,
            };
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                RecoverPendingPullOverride = recovery =>
                    Task.FromException(
                        new PendingPullRecoveryPreservedException(
                            recovery.Checkpoint.RefName)),
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) => Task.FromResult(true);

            await projectService.OpenProject(projectFile);

            IReadOnlyList<PendingPullRecovery> retained =
                await tracked.GetPendingPullRecoveriesAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.SameAs(current));
                Assert.That(projectService.CurrentTransition, Is.Null);
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(retained.Select(item => item.Id), Does.Contain(pending.Id));
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Project_close_does_not_wait_for_pending_pull_recovery_confirmation()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var offerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var offerCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOffer = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-pending-pull-close-race");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(
                repository: null,
                repository,
                originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);

            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.ConfirmPendingPullRecoveryAsync = async (_, cancellationToken) =>
            {
                offerStarted.TrySetResult();
                try
                {
                    return await releaseOffer.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    offerCancelled.TrySetResult();
                    throw;
                }
            };
            releaseHygiene.TrySetResult();
            await offerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task close = TestShell.Project.CloseProject();
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await offerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);

            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            coordinator = null;

            Assert.Multiple(() =>
            {
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            releaseOffer.TrySetResult(false);
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public Task Recent_projects_recovers_a_missing_project_before_deserialization()
    {
        return AssertRecentProjectRecoveryBeforeDeserializationAsync(deleteProjectFile: true);
    }

    [AvaloniaTest]
    public Task Recent_projects_recovers_an_invalid_project_before_deserialization()
    {
        return AssertRecentProjectRecoveryBeforeDeserializationAsync(deleteProjectFile: false);
    }

    [AvaloniaTest]
    public async Task Missing_recent_project_without_recovery_keeps_the_current_project_open()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project current = await CreateProjectForFakeVersionControlAsync(
                "version-control-missing-recent-current");
            string missingProject = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "missing-recent",
                "missing.bep");
            string projectRoot = Path.GetDirectoryName(current.Uri!.LocalPath)!;
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                originalTip);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: _ => discovery);
            var menu = new MenuBarViewModel(TestShell.Project, editorService, coordinator);

            await menu.OpenRecentProject.ExecuteAsync(missingProject);

            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(current));
            Assert.That(Path.GetDirectoryName(current.Uri.LocalPath), Is.EqualTo(projectRoot));
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task External_project_symlink_with_declined_recovery_keeps_current_project_open()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        ProjectService? projectService = null;
        Func<Project, Task>? openedObserver = null;

        try
        {
            config.AutoCommitOnClose = false;
            (Project current, string linkedProject, string linkedRoot) =
                await CreateExternalProjectSymlinkForOpenTestAsync(
                    "version-control-unsafe-open-decline");
            projectService = new ProjectService();
            var repository = new RepositoryInfo(linkedRoot, linkedRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, null, originalTip)
            {
                DiscoverRepositoryOverride = projectRoot =>
                    RepositoryPathComparer.AreEquivalent(projectRoot, linkedRoot)
                        ? repository
                        : null,
            };
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                linkedProject,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(false);
            };
            int openedCalls = 0;
            openedObserver = _ =>
            {
                openedCalls++;
                return Task.CompletedTask;
            };
            projectService.Opened += openedObserver;

            await projectService.OpenProject(linkedProject);

            IReadOnlyList<PendingPullRecovery> retained =
                await tracked.GetPendingPullRecoveriesAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.SameAs(current));
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(retained.Select(item => item.Id), Does.Contain(pending.Id));
                Assert.That(openedCalls, Is.Zero);
            });
        }
        finally
        {
            if (openedObserver is not null)
            {
                projectService!.Opened -= openedObserver;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Root_alias_with_child_link_cycle_still_selects_its_pending_recovery()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project current = await CreateProjectForFakeVersionControlAsync(
                "version-control-recovery-alias-cycle-current");
            string repositoryRoot = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-recovery-alias-cycle-repository");
            string aliasContainer = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-recovery-alias-cycle-alias");
            string aliasRoot = Path.Combine(aliasContainer, "repository-link");
            Directory.CreateDirectory(repositoryRoot);
            Directory.CreateDirectory(aliasContainer);
            try
            {
                Directory.CreateSymbolicLink(aliasRoot, repositoryRoot);
                Directory.CreateSymbolicLink(
                    Path.Combine(repositoryRoot, "cycle-a"),
                    "cycle-b");
                Directory.CreateSymbolicLink(
                    Path.Combine(repositoryRoot, "cycle-b"),
                    "cycle-a");
            }
            catch (Exception ex)
                when (ex is UnauthorizedAccessException
                      or IOException
                      or PlatformNotSupportedException)
            {
                Assert.Ignore(
                    $"Symbolic links are not creatable in this environment: {ex.Message}");
            }

            string requestedProject = Path.Combine(aliasRoot, "cycle-a", "project.bep");
            string descriptorProject = Path.Combine(
                repositoryRoot,
                "cycle-a",
                "project.bep");
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(repositoryRoot, repositoryRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, null, originalTip)
            {
                DiscoverRepositoryOverride = projectRoot =>
                    Path.GetFullPath(projectRoot).StartsWith(
                        Path.GetFullPath(aliasRoot) + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal)
                        ? repository
                        : null,
            };
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                descriptorProject,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(false);
            };

            await projectService.OpenProject(requestedProject);

            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.SameAs(current));
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.Zero);
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task External_project_symlink_with_failed_recovery_keeps_current_project_open()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            (Project current, string linkedProject, string linkedRoot) =
                await CreateExternalProjectSymlinkForOpenTestAsync(
                    "version-control-unsafe-open-failed-recovery");
            var projectService = new ProjectService();
            var repository = new RepositoryInfo(linkedRoot, linkedRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, null, originalTip)
            {
                DiscoverRepositoryOverride = projectRoot =>
                    RepositoryPathComparer.AreEquivalent(projectRoot, linkedRoot)
                        ? repository
                        : null,
            };
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                RecoverPendingPullOverride = recovery =>
                    Task.FromException(
                        new PendingPullRecoveryPreservedException(
                            recovery.Checkpoint.RefName)),
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                linkedProject,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            await projectService.OpenProject(linkedProject);

            IReadOnlyList<PendingPullRecovery> retained =
                await tracked.GetPendingPullRecoveriesAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.SameAs(current));
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(retained.Select(item => item.Id), Does.Contain(pending.Id));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Failed_opened_handler_retains_prepared_pull_recovery_refs()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        Func<Project, Task>? throwingOpened = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-opened-handler-recovery");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            await TestShell.Project.CloseProject();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) => Task.FromResult(true);
            throwingOpened = _ => throw new InvalidOperationException("opened handler failed");
            TestShell.Project.Opened += throwingOpened;

            await TestShell.Project.OpenProject(projectFile);
            HeadlessTestHelpers.Settle();

            IReadOnlyList<PendingPullRecovery> retained =
                await tracked.GetPendingPullRecoveriesAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(retained.Select(item => item.Id), Does.Contain(pending.Id));
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (throwingOpened is not null)
            {
                TestShell.Project.Opened -= throwingOpened;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Already_applied_open_aborts_when_its_live_marker_is_replaced_before_apply()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        Func<Project, Task>? throwingOpened = null;
        Func<
            ProjectService.ProjectOpenAttempt,
            CancellationToken,
            Task<ProjectService.ProjectOpenPreparation?>>? replaceMarker = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-replaced-opening-marker");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            await TestShell.Project.CloseProject();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            PendingPullRecovery pending = await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            throwingOpened = _ => throw new InvalidOperationException("opened handler failed");
            TestShell.Project.Opened += throwingOpened;

            await TestShell.Project.OpenProject(projectFile);
            TestShell.Project.Opened -= throwingOpened;
            throwingOpened = null;
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);

            PendingPullRecovery replacement = pending with
            {
                Id = "replacement-marker",
                DescriptorRef = "refs/beutl/recovery/test/replacement-marker",
                DescriptorObject = "cccccccccccccccccccccccccccccccccccccccc",
            };
            replaceMarker = (_, _) =>
            {
                var field = typeof(VersionControlCoordinator).GetField(
                    "_openingPullRecoveries",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
                var markers = (System.Collections.IDictionary)field!.GetValue(coordinator)!;
                string key = markers.Keys.Cast<string>().Single();
                object current = markers[key]!;
                object replaced = Activator.CreateInstance(
                    current.GetType(),
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    args: [repository, replacement],
                    culture: null)!;
                markers[key] = replaced;
                return Task.FromResult<ProjectService.ProjectOpenPreparation?>(null);
            };
            TestShell.Project.OpeningPreflight += replaceMarker;

            await TestShell.Project.OpenProject(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.Zero);
                Assert.That(tracked.IsCheckpointRetained, Is.True);
            });
        }
        finally
        {
            if (throwingOpened is not null)
            {
                TestShell.Project.Opened -= throwingOpened;
            }

            if (replaceMarker is not null)
            {
                TestShell.Project.OpeningPreflight -= replaceMarker;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Required_recovery_miss_does_not_remove_an_unrelated_opening_marker()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        Func<Project, Task>? throwingOpened = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-required-recovery-marker");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            await TestShell.Project.CloseProject();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            throwingOpened = _ => throw new InvalidOperationException("opened handler failed");
            TestShell.Project.Opened += throwingOpened;

            await TestShell.Project.OpenProject(projectFile);
            TestShell.Project.Opened -= throwingOpened;
            throwingOpened = null;
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);

            var method = typeof(VersionControlCoordinator).GetMethod(
                "TryRecoverPendingPullBeforeOpeningAsync",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            var missingRecovery = (Task<bool>)method!.Invoke(
                coordinator,
                [projectFile, CancellationToken.None, "missing-recovery-id"])!;
            Assert.That(await missingRecovery, Is.False);

            await TestShell.Project.OpenProject(projectFile);
            await WaitUntilAsync(() => tracked.CompletePendingPullCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.EqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            if (throwingOpened is not null)
            {
                TestShell.Project.Opened -= throwingOpened;
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_waits_for_published_pull_recovery_completion()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var completionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completionFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-dispose-opening-recovery");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            await TestShell.Project.CloseProject();
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                CompletePendingPullStarted = completionStarted,
                CompletePendingPullRelease = releaseCompletion.Task,
                CompletePendingPullCompleted = completionFinished,
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                new EditorService(new ExtensionProvider()),
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) => Task.FromResult(true);

            await TestShell.Project.OpenProject(projectFile);
            await completionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disposal = coordinator.DisposeAsync().AsTask();
            await Task.Delay(100);
            Assert.That(disposal.IsCompleted, Is.False);

            releaseCompletion.TrySetResult();
            await completionFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator = null;

            Assert.That(tracked.CompletePendingPullCalls, Is.EqualTo(1));
        }
        finally
        {
            releaseCompletion.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Slow_recovery_completion_cannot_activate_an_older_published_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;
        var completionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completionFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            config.AutoCommitOnClose = false;
            Project first = await CreateProjectForFakeVersionControlAsync(
                "version-control-stale-recovery-first");
            string firstFile = first.Uri!.LocalPath;
            Project second = await CreateProjectForFakeVersionControlAsync(
                "version-control-stale-recovery-second");
            string secondFile = second.Uri!.LocalPath;
            await TestShell.Project.CloseProject();
            string firstRoot = Path.GetDirectoryName(firstFile)!;
            string secondRoot = Path.GetDirectoryName(secondFile)!;
            var firstRepository = new RepositoryInfo(firstRoot, firstRoot);
            var secondRepository = new RepositoryInfo(secondRoot, secondRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, null, originalTip)
            {
                DiscoverRepositoryOverride = projectRoot =>
                    RepositoryPathComparer.AreEquivalent(projectRoot, firstRoot)
                        ? firstRepository
                        : RepositoryPathComparer.AreEquivalent(projectRoot, secondRoot)
                            ? secondRepository
                            : null,
            };
            var firstBackend = new PullCycleTestBackend(
                firstRepository,
                firstRepository,
                originalTip)
            {
                CompletePendingPullStarted = completionStarted,
                CompletePendingPullRelease = releaseCompletion.Task,
                CompletePendingPullCompleted = completionFinished,
            };
            var secondBackend = new PullCycleTestBackend(
                secondRepository,
                secondRepository,
                originalTip);
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            await firstBackend.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                firstFile,
                CancellationToken.None);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null
                    ? discovery
                    : RepositoryPathComparer.AreEquivalent(candidate.ProjectRoot, firstRoot)
                        ? firstBackend
                        : secondBackend);
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) => Task.FromResult(true);

            await TestShell.Project.OpenProject(firstFile);
            await completionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.OpenProject(secondFile);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, secondBackend));
            releaseCompletion.TrySetResult();
            await completionFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(secondFile));
                Assert.That(coordinator.CurrentService, Is.SameAs(secondBackend));
                Assert.That(editorService.ProjectVersionControlService.Value,
                    Is.SameAs(secondBackend));
            });
        }
        finally
        {
            releaseCompletion.TrySetResult();
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
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
    public async Task Restore_to_new_branch_validates_revision_before_confirmation_or_close()
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
            config.AutoCommitOnSave = false;
            config.AutoCommitOnClose = false;
            config.UseLfsWhenAvailable = false;

            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-reject-restore-option");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            string validSha = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();
            int confirmationCalls = 0;
            TestShell.VersionControl.ConfirmRestoreAsync = _ =>
            {
                confirmationCalls++;
                return Task.FromResult(true);
            };

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await TestShell.VersionControl.RestoreToNewBranchAsync(
                    "--discard-changes",
                    "injected-option"));

            string currentBranch = await RunGitAsync(
                gitPath,
                projectRoot,
                "branch",
                "--show-current");
            string injectedBranch = await RunGitAsync(
                gitPath,
                projectRoot,
                "branch",
                "--list",
                "injected-option");
            Assert.Multiple(() =>
            {
                Assert.That(confirmationCalls, Is.Zero);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(currentBranch.Trim(), Is.EqualTo("main"));
                Assert.That(injectedBranch, Is.Empty);
            });

            Assert.That(
                await TestShell.VersionControl.RestoreToNewBranchAsync(
                    validSha,
                    "valid-revision"),
                Is.True);
            HeadlessTestHelpers.Settle();

            string validBranch = await RunGitAsync(
                gitPath,
                projectRoot,
                "branch",
                "--show-current");
            Assert.Multiple(() =>
            {
                Assert.That(confirmationCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(validBranch.Trim(), Is.EqualTo("valid-revision"));
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
    public async Task Restore_rejects_revision_without_project_file_before_confirmation_or_close()
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
            config.AutoCommitOnSave = false;
            config.AutoCommitOnClose = false;
            config.UseLfsWhenAvailable = false;

            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-reject-missing-project");
            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            string projectPathspec = Path.GetRelativePath(projectRoot, projectFile)
                .Replace('\\', '/');
            string originalTip = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();
            await RunGitAsync(gitPath, projectRoot, "rm", "--", projectPathspec);
            await RunGitAsync(gitPath, projectRoot, "commit", "-m", "remove project file");
            string missingProjectRevision = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();
            await RunGitAsync(gitPath, projectRoot, "reset", "--hard", originalTip);
            HeadlessTestHelpers.Settle();

            int confirmationCalls = 0;
            TestShell.VersionControl.ConfirmRestoreAsync = _ =>
            {
                confirmationCalls++;
                return Task.FromResult(true);
            };

            Assert.That(
                await TestShell.VersionControl.RestoreAsync(missingProjectRevision),
                Is.False);
            HeadlessTestHelpers.Settle();

            string currentBranch = await RunGitAsync(
                gitPath,
                projectRoot,
                "branch",
                "--show-current");
            Assert.Multiple(() =>
            {
                Assert.That(confirmationCalls, Is.Zero);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(currentBranch.Trim(), Is.EqualTo("main"));
                Assert.That(File.Exists(projectFile), Is.True);
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
    public async Task Restore_to_new_branch_rejects_invalid_existing_and_shorthand_names_before_confirmation_or_close()
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

            (Project project, EditViewModel editor) = await CreateTrackedProjectAsync(
                "version-control-restore-branch-validation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            string targetSha = (await RunGitAsync(
                gitPath,
                projectRoot,
                "rev-parse",
                "HEAD")).Trim();
            await RunGitAsync(gitPath, projectRoot, "branch", "existing");
            await RunGitAsync(gitPath, projectRoot, "switch", "existing");
            await RunGitAsync(gitPath, projectRoot, "switch", "main");
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            AddRectangle(adder, layer: 0);
            project.Variables[RestoreStateKey] = "unsaved-state";
            CoreSerializer.StoreToUri(project, project.Uri!);
            Assert.That(
                (await TestShell.VersionControl.CurrentService!.GetStatusAsync(
                    CancellationToken.None)).IsClean,
                Is.False);
            int confirmations = 0;
            TestShell.VersionControl.ConfirmRestoreAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            bool invalid = await TestShell.VersionControl.RestoreToNewBranchAsync(
                targetSha,
                "invalid..branch");
            bool existing = await TestShell.VersionControl.RestoreToNewBranchAsync(
                targetSha,
                "existing");
            bool shorthand = await TestShell.VersionControl.RestoreToNewBranchAsync(
                targetSha,
                "@{-1}");
            HeadlessTestHelpers.Settle();

            WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
                .GetStatusAsync(CancellationToken.None);
            IReadOnlyList<CommitInfo> history =
                await TestShell.VersionControl.CurrentService.GetHistoryAsync(
                    0,
                    20,
                    CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(invalid, Is.False);
                Assert.That(existing, Is.False);
                Assert.That(shorthand, Is.False);
                Assert.That(confirmations, Is.Zero);
                Assert.That(status.Branch, Is.EqualTo("main"));
                Assert.That(status.IsClean, Is.False);
                Assert.That(
                    history.Any(commit => commit.Kind == SnapshotKind.Safety),
                    Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(editor.HistoryManager.CanUndo, Is.True);
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
    public async Task Restore_to_new_branch_revalidates_the_name_before_closing_the_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-restore-branch-revalidation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, tip);
            var backend = new PullCycleTestBackend(repository, repository, tip)
            {
                Status = new WorkspaceStatus(
                    "main",
                    Ahead: 0,
                    Behind: 0,
                    Changes: [],
                    HasConflicts: false),
            };
            backend.EnqueueCanCreateBranchResult(true);
            backend.EnqueueCanCreateBranchResult(false);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig();
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            int confirmations = 0;
            coordinator.ConfirmRestoreAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool restored = await coordinator.RestoreToNewBranchAsync(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "target");
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(restored, Is.False);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(backend.CanCreateBranchCalls, Is.EqualTo(2));
                Assert.That(backend.CreateBranchCalls, Is.Zero);
                Assert.That(backend.CommitAllCalls, Is.Zero);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Restore_to_new_branch_revalidates_after_the_safety_snapshot_before_closing_the_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        try
        {
            Project project = await CreateProjectForFakeVersionControlAsync(
                "version-control-restore-branch-post-snapshot-revalidation");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var tip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(repository: null, repository, tip);
            var backend = new PullCycleTestBackend(repository, repository, tip);
            backend.EnqueueCanCreateBranchResult(true);
            backend.EnqueueCanCreateBranchResult(true);
            backend.EnqueueCanCreateBranchResult(false);
            var editorService = new EditorService(new ExtensionProvider());
            var config = new VersionControlConfig();
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : backend);
            int confirmations = 0;
            coordinator.ConfirmRestoreAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool restored = await coordinator.RestoreToNewBranchAsync(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "target");
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(restored, Is.False);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(backend.CanCreateBranchCalls, Is.EqualTo(3));
                Assert.That(backend.CreateBranchCalls, Is.Zero);
                Assert.That(backend.CommitAllCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
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

    private static async Task AssertRecentProjectRecoveryBeforeDeserializationAsync(
        bool deleteProjectFile)
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        VersionControlCoordinator? coordinator = null;

        try
        {
            config.AutoCommitOnClose = false;
            Project project = await CreateProjectForFakeVersionControlAsync(
                deleteProjectFile
                    ? "version-control-missing-project-recovery"
                    : "version-control-invalid-project-recovery");
            string projectFile = project.Uri!.LocalPath;
            byte[] validProject = await File.ReadAllBytesAsync(projectFile);
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            await TestShell.Project.CloseProject();
            if (deleteProjectFile)
            {
                File.Delete(projectFile);
            }
            else
            {
                await File.WriteAllTextAsync(projectFile, "not a valid project");
            }

            var repository = new RepositoryInfo(projectRoot, projectRoot);
            var originalTip = new CheckedOutBranchTip(
                "refs/heads/main",
                "1111111111111111111111111111111111111111");
            var discovery = new PullCycleTestBackend(null, repository, originalTip);
            var tracked = new PullCycleTestBackend(repository, repository, originalTip)
            {
                RecoverPendingPullOverride = _ => File.WriteAllBytesAsync(
                    projectFile,
                    validProject),
            };
            var checkpoint = new ProjectCheckpoint(
                "refs/beutl/safety/test-checkpoint",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                originalTip);
            await tracked.PersistPendingPullRecoveryAsync(
                checkpoint,
                originalTip,
                projectFile,
                CancellationToken.None);
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                TestShell.Project,
                editorService,
                config,
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmPendingPullRecoveryAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };
            var menu = new MenuBarViewModel(TestShell.Project, editorService, coordinator);

            await menu.OpenRecentProject.ExecuteAsync(projectFile);
            await WaitUntilAsync(() => tracked.CompletePendingPullCalls == 1);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(tracked.RecoverPendingPullCalls, Is.GreaterThanOrEqualTo(1));
                Assert.That(tracked.CompletePendingPullCalls, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(projectFile), Is.EqualTo(validProject));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(
                    TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(projectFile));
            });
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
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

    private static async Task<(Project Current, string LinkedProject, string LinkedRoot)>
        CreateExternalProjectSymlinkForOpenTestAsync(string directoryName)
    {
        Project current = await CreateProjectForFakeVersionControlAsync(
            $"{directoryName}-current");
        string linkedRoot = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            $"{directoryName}-repository");
        string externalRoot = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            $"{directoryName}-external");
        Directory.CreateDirectory(linkedRoot);
        Directory.CreateDirectory(externalRoot);
        string externalProject = Path.Combine(externalRoot, "external.bep");
        await File.WriteAllTextAsync(externalProject, "external project sentinel\n");
        string linkedProject = Path.Combine(linkedRoot, "linked.bep");
        try
        {
            File.CreateSymbolicLink(linkedProject, externalProject);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }

        return (current, linkedProject, linkedRoot);
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
            TestShell.Project.CurrentProject.Value!,
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

    private sealed class BlockingGitInstallationProbe : IGitInstallationProbe
    {
        private int _startCount;
        private int _cancellationCount;
        private int _completionCount;

        public TaskCompletionSource TwoProbesStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TwoProbesCancelled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCancelledProbes { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CancellationCount => Volatile.Read(ref _cancellationCount);

        public int CompletionCount => Volatile.Read(ref _completionCount);

        public async Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _startCount) == 2)
            {
                TwoProbesStarted.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref _cancellationCount) == 2)
                {
                    TwoProbesCancelled.TrySetResult();
                }

                await ReleaseCancelledProbes.Task;
                Interlocked.Increment(ref _completionCount);
                throw;
            }
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The blocking probe must not execute Git.");
        }

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
    }

    private sealed class MutableGitInstallationProbe : IGitInstallationProbe
    {
        public bool IsInstalled { get; set; }

        public Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> paths = IsInstalled ? ["/test/git"] : [];
            return Task.FromResult(paths);
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                arguments.SequenceEqual(["--version"])
                    ? new GitProbeResult(0, "git version 2.40.0", string.Empty)
                    : new GitProbeResult(1, string.Empty, string.Empty));
        }

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
    }

    private sealed class ConfigurableGitInstallationProbe(string installedExecutablePath)
        : IGitInstallationProbe
    {
        private readonly string _installedExecutablePath = Path.GetFullPath(installedExecutablePath);

        public Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            bool installed = RepositoryPathComparer.AreEquivalent(
                _installedExecutablePath,
                executablePath);
            if (installed && arguments.SequenceEqual(["--version"]))
            {
                return Task.FromResult(
                    new GitProbeResult(0, "git version 2.40.0", string.Empty));
            }

            return Task.FromResult(new GitProbeResult(1, string.Empty, string.Empty));
        }

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
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
        private readonly Queue<IReadOnlyList<BranchInfo>> _branchSnapshots = new();
        private readonly Queue<bool> _canCreateBranchResults = new();
        private readonly List<PendingPullRecovery> _pendingPullRecoveries = [];
        private readonly CheckedOutBranchTip _originalTip;
        private readonly SemaphoreSlim _exclusiveGate = new(1, 1);
        private readonly object _retirementSync = new();
        private bool _hasIdentity;
        private bool _retired;
        private int _disposeCalls;
        private int _commitAllCalls;
        private int _retirementCalls;
        private Task? _retirementTask;
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

        public RepositoryLockInfo? RecoverableLock { get; private set; }

        public FastForwardPullResult PullResult { get; init; }

        public PullPreflightResult PreflightResult { get; init; } = new(
            new RemoteOpResult.Success(),
            RequiresTransition: true);

        public TaskCompletionSource? PreflightStarted { get; init; }

        public Task? PreflightRelease { get; init; }

        public TaskCompletionSource? PendingPullLookupStarted { get; set; }

        public Task? PendingPullLookupRelease { get; set; }

        public PendingPullRecoveryOutcome PendingRecoveryOutcome { get; init; } =
            PendingPullRecoveryOutcome.RestoredOriginal;

        public WorkspaceStatus Status { get; init; } = DirtyStatus;

        public IReadOnlyList<BranchInfo> Branches { get; init; } =
            [new BranchInfo("main", true, null)];

        public BranchTipRollbackResult RollbackResult { get; init; } =
            new BranchTipRollbackResult.RolledBack();

        public TaskCompletionSource? EnsureHygieneStarted { get; init; }

        public Task? EnsureHygieneRelease { get; init; }

        public TaskCompletionSource? EnsureHygieneCompleted { get; init; }

        public Action<CancellationToken>? EnsureHygieneTokenObserved { get; init; }

        public Func<CancellationToken, Task>? EnsureHygieneOverride { get; init; }

        public Func<CancellationToken, Task<GitAvailability>>? AvailabilityOverride { get; init; }

        public Func<string, RepositoryInfo?>? DiscoverRepositoryOverride { get; init; }

        public Func<PendingPullRecovery, Task>? RecoverPendingPullOverride { get; init; }

        public Func<PendingPullRecovery, Task>? CompletePendingPullOverride { get; init; }

        public TaskCompletionSource? CompletePendingPullStarted { get; init; }

        public Task? CompletePendingPullRelease { get; init; }

        public TaskCompletionSource? CompletePendingPullCompleted { get; init; }

        public TaskCompletionSource? RetirementStarted { get; init; }

        public Task? RetirementRelease { get; init; }

        public TaskCompletionSource? DisposeCompleted { get; init; }

        public bool DisposeStartsRetirement { get; init; }

        public bool IdempotentRetirement { get; init; }

        public Action? RetirementStarting { get; set; }

        public TaskCompletionSource? InitializeStarted { get; init; }

        public Task? InitializeRelease { get; init; }

        public TaskCompletionSource? SwitchBranchStarted { get; init; }

        public Task? SwitchBranchRelease { get; init; }

        public TaskCompletionSource? CommitAllStarted { get; init; }

        public Task? CommitAllRelease { get; init; }

        public bool RequireIdentityForInitialization { get; init; }

        public int InitializeCalls { get; private set; }

        public int SetLocalIdentityCalls { get; private set; }

        public int CheckpointCreateCalls { get; private set; }

        public int PreflightCalls { get; private set; }

        public int CommitAllCalls => Volatile.Read(ref _commitAllCalls);

        public int PullCalls { get; private set; }

        public int RecoverPendingPullCalls { get; private set; }

        public int CompletePendingPullCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public int RestoreCheckpointCalls { get; private set; }

        public int DeleteCheckpointCalls { get; private set; }

        public int GetBranchesCalls { get; private set; }

        public int CanCreateBranchCalls { get; private set; }

        public int CreateBranchCalls { get; private set; }

        public int RetirementCalls => Volatile.Read(ref _retirementCalls);

        public int RemoveRecoverableLockCalls { get; private set; }

        public int CallsAfterRetirement { get; private set; }

        public void EnqueueBranchSnapshot(IReadOnlyList<BranchInfo> branches)
        {
            _branchSnapshots.Enqueue(branches);
        }

        public void EnqueueCanCreateBranchResult(bool canCreate)
        {
            _canCreateBranchResults.Enqueue(canCreate);
        }

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public List<ProjectVersionControlFinalSnapshot?> RetirementSnapshots { get; } = [];

        public List<InitOptions> InitializationOptions { get; } = [];

        public bool IsCheckpointRetained => _checkpoint is not null;

        public event EventHandler<WorkspaceStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<RepositoryLockInfo>? RecoverableLockAvailable;

        public void EnqueueObservedTip(CheckedOutBranchTip tip)
        {
            _observedTips.Enqueue(tip);
        }

        public void RemovePendingPullRecovery(string recoveryId)
        {
            _pendingPullRecoveries.RemoveAll(candidate => candidate.Id == recoveryId);
        }

        public void RaiseRecoverableLock(RepositoryLockInfo lockInfo)
        {
            RecoverableLock = lockInfo;
            RecoverableLockAvailable?.Invoke(this, lockInfo);
        }

        public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
        {
            if (AvailabilityOverride is not null)
            {
                return AvailabilityOverride(cancellationToken);
            }

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
            return Task.FromResult(
                DiscoverRepositoryOverride?.Invoke(projectRoot) ?? _discoveredRepository);
        }

        public async Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken)
        {
            if (EnsureHygieneOverride is not null)
            {
                await EnsureHygieneOverride(cancellationToken);
                return;
            }

            try
            {
                EnsureHygieneTokenObserved?.Invoke(cancellationToken);
                EnsureHygieneStarted?.TrySetResult();
                if (EnsureHygieneRelease is not null)
                {
                    await EnsureHygieneRelease.WaitAsync(cancellationToken);
                }
            }
            finally
            {
                EnsureHygieneCompleted?.TrySetResult();
            }
        }

        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            RecordBackendCall();
            return Task.FromResult(Status);
        }

        public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _observedTips.TryDequeue(out CheckedOutBranchTip? tip)
                    ? tip
                    : _originalTip);
        }

        public async Task<PullPreflightResult> PreflightPullAsync(
            CheckedOutBranchTip expectedCurrent,
            CancellationToken cancellationToken)
        {
            PreflightCalls++;
            PreflightStarted?.TrySetResult();
            if (PreflightRelease is not null)
            {
                await PreflightRelease.WaitAsync(cancellationToken);
            }

            return PreflightResult;
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
            string projectFile,
            CancellationToken cancellationToken)
        {
            PullCalls++;
            if (PullResult.Recovery is { } recovery
                && !_pendingPullRecoveries.Any(candidate => candidate.Id == recovery.Id))
            {
                _pendingPullRecoveries.Add(recovery);
            }

            return Task.FromResult(PullResult);
        }

        public Task<PendingPullRecovery> PersistPendingPullRecoveryAsync(
            ProjectCheckpoint checkpoint,
            CheckedOutBranchTip targetTip,
            string projectFile,
            CancellationToken cancellationToken)
        {
            _checkpoint = checkpoint;
            string id = Guid.NewGuid().ToString("N");
            var recovery = new PendingPullRecovery(
                id,
                $"refs/beutl/recovery/test/{id}",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                checkpoint,
                targetTip,
                projectFile,
                DateTimeOffset.UtcNow);
            _pendingPullRecoveries.Add(recovery);
            return Task.FromResult(recovery);
        }

        public async Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesAsync(
            CancellationToken cancellationToken)
        {
            PendingPullLookupStarted?.TrySetResult();
            if (PendingPullLookupRelease is not null)
            {
                await PendingPullLookupRelease.WaitAsync(cancellationToken);
            }

            return _pendingPullRecoveries.ToArray();
        }

        public async Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryAsync(
            PendingPullRecovery recovery,
            CancellationToken cancellationToken)
        {
            RecoverPendingPullCalls++;
            if (RecoverPendingPullOverride is not null)
            {
                await RecoverPendingPullOverride(recovery);
            }

            return PendingRecoveryOutcome;
        }

        public async Task CompletePendingPullRecoveryAsync(
            PendingPullRecovery recovery,
            CancellationToken cancellationToken)
        {
            CompletePendingPullCalls++;
            CompletePendingPullStarted?.TrySetResult();
            if (CompletePendingPullOverride is not null)
            {
                await CompletePendingPullOverride(recovery);
            }
            if (CompletePendingPullRelease is not null)
            {
                await CompletePendingPullRelease;
            }

            _pendingPullRecoveries.RemoveAll(candidate => candidate.Id == recovery.Id);
            _checkpoint = null;
            CompletePendingPullCompleted?.TrySetResult();
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

        public async Task<TResult> ExecuteExclusiveAsync<TResult>(
            Func<IProjectVersionControlTransaction, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            await _exclusiveGate.WaitAsync(cancellationToken);
            try
            {
                return await operation(this);
            }
            finally
            {
                _exclusiveGate.Release();
            }
        }

        public async Task InitializeAsync(
            InitOptions options,
            CancellationToken cancellationToken)
        {
            RecordBackendCall();
            InitializeCalls++;
            InitializationOptions.Add(options);
            if (RequireIdentityForInitialization && !_hasIdentity && options.Identity is null)
            {
                throw new GitIdentityRequiredException();
            }

            if (options.Identity is not null)
            {
                _hasIdentity = true;
            }

            InitializeStarted?.TrySetResult();
            if (InitializeRelease is not null)
            {
                await InitializeRelease.WaitAsync(cancellationToken);
            }

            Repository = options.TargetRepository;
        }

        public async Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
        {
            RecordBackendCall();
            Interlocked.Increment(ref _commitAllCalls);
            CommitAllStarted?.TrySetResult();
            if (CommitAllRelease is not null)
            {
                await CommitAllRelease.WaitAsync(cancellationToken);
            }

            return new CommitResult.NoChanges();
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

        public Task<bool> RevisionContainsProjectFileAsync(
            string sha,
            string projectFile,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
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
            RecordBackendCall();
            SetLocalIdentityCalls++;
            _hasIdentity = true;
            return Task.CompletedTask;
        }

        public Task RetireAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
        {
            if (!IdempotentRetirement)
            {
                return RetireCoreAsync(finalSnapshot);
            }

            lock (_retirementSync)
            {
                return _retirementTask ??= RetireCoreAsync(finalSnapshot);
            }
        }

        private async Task RetireCoreAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
        {
            await _exclusiveGate.WaitAsync();
            try
            {
                Interlocked.Increment(ref _retirementCalls);
                RetirementSnapshots.Add(finalSnapshot);
                RetirementStarting?.Invoke();
                RetirementStarted?.TrySetResult();
                if (RetirementRelease is not null)
                {
                    await RetirementRelease;
                }

                _retired = true;
            }
            finally
            {
                _exclusiveGate.Release();
            }
        }

        public Task<bool> RemoveRecoverableLockAsync(CancellationToken cancellationToken)
        {
            RemoveRecoverableLockCalls++;
            RecoverableLock = null;
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
            GetBranchesCalls++;
            return Task.FromResult(
                _branchSnapshots.TryDequeue(out IReadOnlyList<BranchInfo>? branches)
                    ? branches
                    : Branches);
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

        public Task<bool> CanCreateBranchAsync(
            string name,
            CancellationToken cancellationToken)
        {
            CanCreateBranchCalls++;
            return Task.FromResult(
                _canCreateBranchResults.TryDequeue(out bool canCreate)
                    ? canCreate
                    : true);
        }

        public Task CreateBranchAsync(
            string name,
            string startPoint,
            CancellationToken cancellationToken)
        {
            CreateBranchCalls++;
            return Task.CompletedTask;
        }

        public async Task SwitchBranchAsync(string name, CancellationToken cancellationToken)
        {
            SwitchBranchStarted?.TrySetResult();
            if (SwitchBranchRelease is not null)
            {
                await SwitchBranchRelease.WaitAsync(cancellationToken);
            }
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCalls);
            if (DisposeStartsRetirement && RetirementCalls == 0)
            {
                _ = RetireAsync(finalSnapshot: null);
            }

            DisposeCompleted?.TrySetResult();
        }

        private void RecordBackendCall()
        {
            if (_retired)
            {
                CallsAfterRetirement++;
            }
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
