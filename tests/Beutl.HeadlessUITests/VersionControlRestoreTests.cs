using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.GitTestSupport;
using static Beutl.Testing.Headless.HeadlessTestHelpers;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlRestoreTests
{
    private const string RestoreStateKey = "version-control-restore-state";

    private static readonly CheckedOutBranchTip MainTip = new(
        "refs/heads/main",
        "1111111111111111111111111111111111111111");

    [AvaloniaTest]
    public async Task Project_open_ignores_optional_Git_discovery_for_a_control_character_path()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows paths cannot contain the control character used by this regression.");
        }

        await using RealGitScope git = await RealGitScope.StartAsync(autoCommitOnClose: false);

        string location = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "version-control-control\npath");
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(
            640,
            480,
            30,
            44100,
            "control-path",
            location))!;
        string projectFile = project.Uri!.LocalPath;
        await TestShell.Project.CloseProjectAsync();

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(
                TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                Is.EqualTo(projectFile));
            Assert.That(TestShell.VersionControl.IsTracked.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Manual_commit_requests_repository_identity_and_skips_a_clean_second_commit()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync("version-control-manual");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        await RunGitAsync(git.GitPath, projectRoot, "config", "--unset", "user.name");
        await RunGitAsync(git.GitPath, projectRoot, "config", "--unset", "user.email");
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

    [Test]
    public void Switch_branch_confirmation_discloses_a_shared_repository()
    {
        string standalone = VersionControlCoordinator.CreateSwitchBranchConfirmation(
            "feature",
            isNestedInForeignRepo: false);
        string shared = VersionControlCoordinator.CreateSwitchBranchConfirmation(
            "feature",
            isNestedInForeignRepo: true);

        Assert.Multiple(() =>
        {
            Assert.That(standalone, Does.Contain("feature"));
            Assert.That(
                standalone,
                Does.Not.Contain(Strings.VersionControl_SwitchBranchEnclosingRepositoryNotice));
            // The switch checks the whole enclosing worktree out, which the user has to know
            // before deciding rather than after files outside the project have changed.
            Assert.That(
                shared,
                Does.Contain(Strings.VersionControl_SwitchBranchEnclosingRepositoryNotice));
        });
    }

    [AvaloniaTest]
    public async Task Restore_to_a_new_branch_branches_from_the_current_tip_and_applies_the_project_tree()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-branch-scope");
        const string SelectedCommit = "2222222222222222222222222222222222222222";
        const string BranchName = "restored-version";
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnSave = false, AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : backend);
        coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        bool restored = await coordinator.RestoreToNewBranchAsync(
            SelectedCommit,
            BranchName);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True);
            // Branching at the selected commit would check its whole tree out, rolling back
            // files outside the project in an enclosing repository.
            Assert.That(backend.LastBranchStartPoint, Is.EqualTo(MainTip.Commit));
            Assert.That(
                backend.RestoreProjectTreeCalls,
                Is.EqualTo(new[] { (SelectedCommit, SnapshotKind.Restore) }));
        });
    }

    [AvaloniaTest]
    public async Task Background_restore_waits_for_editor_context_disposal_before_mutating_the_worktree()
    {
        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-editor-disposal-barrier");

        try
        {
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                Status = new WorkspaceStatus(
                    "main",
                    Ahead: 0,
                    Behind: 0,
                    Changes: [],
                    HasConflicts: false),
            };
            backend.EnqueueObservedTip(MainTip);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig
                {
                    AutoCommitOnSave = false,
                    AutoCommitOnClose = false,
                },
                candidate => candidate is null ? discovery : backend);
            coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            TestShell.Editor.TabItems.Add(new EditorTabItem(
                new BlockingDisposeEditorContext(
                    new Scene { Uri = new Uri(Path.Combine(session.Root, "blocking.scene")) },
                    disposeStarted,
                    releaseDispose.Task)));

            Task<bool> restore = Task.Run(() => coordinator.RestoreAsync(
                "2222222222222222222222222222222222222222"));
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(restore.IsCompleted, Is.False);
                Assert.That(backend.RestoreProjectTreeCalls, Is.Empty);
            });

            releaseDispose.TrySetResult();
            Assert.That(await restore.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(backend.RestoreProjectTreeCalls, Has.Count.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            });
        }
        finally
        {
            releaseDispose.TrySetResult();
        }
    }

    [AvaloniaTest]
    public Task Restore_to_new_branch_reopen_failure_warns_that_the_branch_was_retained_after_recovery()
        => AssertRestoreBranchRetentionAsync(RestoreBranchRetentionScenario.Recovered);

    [AvaloniaTest]
    public Task Restore_to_new_branch_does_not_report_manual_branch_cleanup_when_recovery_fails()
        => AssertRestoreBranchRetentionAsync(RestoreBranchRetentionScenario.RecoveryFailed);

    private static async Task AssertRestoreBranchRetentionAsync(
        RestoreBranchRetentionScenario scenario)
    {
        Func<Project, Task>? openedHandler = null;
        await using var session = await FakeVersionControlSession.CreateAsync(
            $"version-control-restore-branch-retention-{scenario}");

        try
        {
            session.Project.Variables[RestoreStateKey] = "original-state";
            var createdTip = new CheckedOutBranchTip(
                "refs/heads/restored-version",
                "3333333333333333333333333333333333333333");
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                Status = new WorkspaceStatus(
                    "main",
                    Ahead: 0,
                    Behind: 0,
                    Changes: [],
                    HasConflicts: false),
                RestoreProjectTreeResult = new CommitResult.Committed(createdTip.Commit),
            };
            backend.EnqueueObservedTip(MainTip);
            backend.EnqueueObservedTip(createdTip);

            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig
                {
                    AutoCommitOnSave = false,
                    AutoCommitOnClose = false,
                },
                candidate => candidate is null ? discovery : backend);
            coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            using var notifications = new NotificationCapture();

            int reopenAttempts = 0;
            openedHandler = _ =>
            {
                reopenAttempts++;
                if (reopenAttempts == 1
                    || scenario == RestoreBranchRetentionScenario.RecoveryFailed)
                {
                    throw new InvalidOperationException("simulated reopen failure");
                }

                return Task.CompletedTask;
            };
            TestShell.Project.Opened += openedHandler;

            bool restored = await coordinator.RestoreToNewBranchAsync(
                "2222222222222222222222222222222222222222",
                "restored-version");
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(restored, Is.False);
                Assert.That(backend.CreateBranchCalls, Is.EqualTo(1));
                Assert.That(backend.RestoreProjectTreeCalls, Has.Count.EqualTo(1));
                Assert.That(reopenAttempts, Is.EqualTo(2));
            });

            string retainedBranchMessage = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.VersionControl_RestoreBranchRetainedFormat,
                "restored-version");
            if (scenario == RestoreBranchRetentionScenario.Recovered)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                    Assert.That(
                        TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                        Is.EqualTo("original-state"));
                    Assert.That(
                        notifications.All.Count(item =>
                            item.Type == NotificationType.Warning
                            && item.Message == retainedBranchMessage),
                        Is.EqualTo(1));
                });
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                    Assert.That(
                        notifications.All.Any(item => item.Message == retainedBranchMessage),
                        Is.False);
                });
            }
        }
        finally
        {
            if (openedHandler is not null)
            {
                TestShell.Project.Opened -= openedHandler;
            }
        }
    }

    private enum RestoreBranchRetentionScenario
    {
        Recovered,
        RecoveryFailed,
    }

    [AvaloniaTest]
    public async Task Manual_commit_records_edits_made_while_the_identity_prompt_is_open()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-manual-identity-edit");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string projectFileName = Path.GetFileName(project.Uri.LocalPath);
        await RunGitAsync(git.GitPath, projectRoot, "config", "--unset", "user.name");
        await RunGitAsync(git.GitPath, projectRoot, "config", "--unset", "user.email");

        // The prompt is where the user can keep editing: the manual version has to record what
        // they see, not the state saved before the identity was asked for.
        TestShell.VersionControl.RequestIdentityAsync = _ =>
        {
            project.Variables["identity-prompt-edit"] = "typed while prompting";
            return Task.FromResult<GitIdentity?>(
                new GitIdentity("Manual Commit Test", "manual@example.invalid"));
        };

        // Something has to be pending before the commit, or the clean tree short-circuits the
        // whole attempt and the identity is never requested.
        project.Variables["pre-prompt-edit"] = "typed before prompting";

        CommitResult result = await TestShell.VersionControl.CommitManualAsync("prompted cut");
        string committedProjectFile = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "show",
            $"HEAD:{projectFileName}");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(committedProjectFile, Does.Contain("pre-prompt-edit"));
            Assert.That(committedProjectFile, Does.Contain("identity-prompt-edit"));
        });
    }

    [AvaloniaTest]
    public async Task Disposal_during_a_lifecycle_operation_cleans_up_the_service_after_completion()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        VersionControlCoordinator? coordinator = null;

        try
        {
            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-coordinator-disposal");
            await RunGitAsync(
                git.GitPath,
                Path.GetDirectoryName(project.Uri!.LocalPath)!,
                "branch",
                "blocked-branch");

            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(TestShell.Project, editorService);
            await WaitUntilAsync(() => coordinator.CurrentService?.Repository is not null);
            coordinator.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmationStarted.TrySetResult();
                return releaseConfirmation.Task;
            };

            Task<bool> operation = coordinator.SwitchBranchAsync("blocked-branch");
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
        }
    }

    [AvaloniaTest]
    public async Task Save_snapshot_is_skipped_while_an_output_operation_is_active()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-save-during-output");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        var config = new VersionControlConfig
        {
            AutoCommitOnSave = true,
            AutoCommitOnClose = false,
        };
        VersionControlCoordinator coordinator = session.Start(config, _ => backend);
        await WaitUntilAsync(() =>
            ReferenceEquals(coordinator.CurrentService, backend)
            && coordinator.IsTracked.Value);

        using (IDisposable outputOperation = session.EditorService.BeginObservedOutputOperation())
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

    [AvaloniaTest]
    public async Task Save_snapshot_failure_publishes_only_the_history_warning()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-save-snapshot-failure");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            CommitAllFailure = new GitOperationException(
                128,
                "fatal: simulated snapshot failure"),
        };
        var config = new VersionControlConfig
        {
            AutoCommitOnSave = true,
            AutoCommitOnClose = false,
        };
        VersionControlCoordinator coordinator = session.Start(config, _ => backend);
        await WaitUntilAsync(() =>
            ReferenceEquals(coordinator.CurrentService, backend)
            && coordinator.IsTracked.Value);
        using var notifications = new NotificationCapture();

        await coordinator.NotifySavedAsync();
        HeadlessTestHelpers.Settle();

        Notification warning = notifications.All.Single();
        Assert.Multiple(() =>
        {
            Assert.That(backend.CommitAllCalls, Is.EqualTo(1));
            Assert.That(warning.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(warning.Title, Is.EqualTo(Strings.VersionControl));
            Assert.That(
                warning.Message,
                Is.EqualTo(string.Format(
                    Strings.VersionControl_SaveSnapshotFailedFormat,
                    "fatal: simulated snapshot failure")),
                "The warning must say what stopped the snapshot.");
        });

        backend.CommitAllFailure = null;
        backend.CommitAllResult = new CommitResult.NoChanges();
        await coordinator.NotifySavedAsync();
        backend.CommitAllResult = new CommitResult.SkippedNoIdentity();
        await coordinator.NotifySavedAsync();
        HeadlessTestHelpers.Settle();

        Assert.That(
            notifications.All,
            Has.Count.EqualTo(1),
            "Clean and missing-identity results must not reuse the failure warning.");
    }

    // A save during a push must not hold the project files until the push ends; one snapshot after the push
    // covers every save made meanwhile.
    [AvaloniaTest]
    public async Task Save_during_a_push_frees_the_project_files_and_snapshots_after_the_push()
    {
        var pushStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePush = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-save-during-push");

        try
        {
            bool? committedOnUiThread = null;
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                PushStarted = pushStarted,
                PushRelease = releasePush.Task,
                CommitAllObserver = _ =>
                    committedOnUiThread = Avalonia.Threading.Dispatcher.UIThread.CheckAccess(),
            };
            var commands = new SaveOperation(true);
            var item = new EditorTabItem(new SavableEditorContext(session.Project, commands));
            session.EditorService.TabItems.Add(item);
            session.EditorService.SelectedTabItem.Value = item;
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig
                {
                    AutoCommitOnSave = true,
                    AutoCommitOnClose = false,
                },
                _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);
            var menu = new MenuBarViewModel(TestShell.Project, session.EditorService, coordinator);

            Task<RemoteOpResult> push = coordinator.PushAsync(progress: null);
            await pushStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await menu.Save.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await menu.Save.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(10));

            using (IProjectFileWriteLease? fileOperation = session.EditorService.TryBeginProjectFileWrite())
            {
                Assert.That(
                    fileOperation,
                    Is.Not.Null,
                    "Imports, file operations, Cut and Paste must not be refused while the push runs.");
            }

            // Auto-save waits for the same reservation.
            using (await session.EditorService.BeginProjectFileWriteAsync(CancellationToken.None)
                       .AsTask()
                       .WaitAsync(TimeSpan.FromSeconds(10)))
            {
            }

            Assert.Multiple(() =>
            {
                Assert.That(commands.SaveCalls, Is.EqualTo(2));
                Assert.That(push.IsCompleted, Is.False);
                Assert.That(backend.CommitAllCalls, Is.Zero);
            });

            // A save still writing when the push ends finishes before the snapshot reads the files.
            IProjectFileWriteLease stillWriting = await session.EditorService
                .BeginProjectFileWriteAsync(CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                releasePush.TrySetResult();
                Assert.That(
                    await push.WaitAsync(TimeSpan.FromSeconds(10)),
                    Is.TypeOf<RemoteOpResult.Success>());
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    HeadlessTestHelpers.Settle();
                    await Task.Delay(20);
                }

                Assert.That(backend.CommitAllCalls, Is.Zero, "The snapshot must wait for the save still writing.");
            }
            finally
            {
                stillWriting.Dispose();
            }

            await WaitUntilAsync(() => backend.CommitAllCalls > 0);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(backend.CommitKinds, Is.EqualTo(new[] { SnapshotKind.Save }));
                Assert.That(committedOnUiThread, Is.True, "The deferred snapshot stays on the UI thread.");
            });
        }
        finally
        {
            releasePush.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Save_with_automatic_snapshots_off_does_not_wait_for_a_push()
    {
        IProjectFileWriteLease? write = null;
        var pushStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePush = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-save-during-push-without-snapshots");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                PushStarted = pushStarted,
                PushRelease = releasePush.Task,
            };
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig
                {
                    AutoCommitOnSave = false,
                    AutoCommitOnClose = false,
                },
                _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            Task<RemoteOpResult> push = coordinator.PushAsync(progress: null);
            await pushStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            write = await session.EditorService.BeginProjectFileWriteAsync(CancellationToken.None);
            await coordinator.NotifySavedAsync(write).WaitAsync(TimeSpan.FromSeconds(10));

            releasePush.TrySetResult();
            await push.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            Assert.That(backend.CommitAllCalls, Is.Zero);
        }
        finally
        {
            releasePush.TrySetResult();
            write?.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Manual_commit_is_blocked_while_an_output_operation_is_active()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-manual-during-output");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        using IDisposable outputOperation = session.EditorService.BeginObservedOutputOperation();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.CommitManualAsync("blocked commit"));
        Assert.That(backend.CommitAllCalls, Is.Zero);
    }

    [AvaloniaTest]
    public async Task Close_snapshot_is_skipped_while_an_output_operation_is_active()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-during-output");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        var config = new VersionControlConfig
        {
            AutoCommitOnClose = true,
        };
        VersionControlCoordinator coordinator = session.Start(config, _ => backend);
        await WaitUntilAsync(() =>
            ReferenceEquals(coordinator.CurrentService, backend)
            && coordinator.IsTracked.Value);

        using (IDisposable outputOperation = session.EditorService.BeginObservedOutputOperation())
        {
            await TestShell.Project.CloseProjectAsync().WaitAsync(TimeSpan.FromSeconds(5));
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

    [AvaloniaTest]
    public async Task Close_after_a_failed_save_can_continue_without_the_close_snapshot()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-after-failed-save");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        var failedCommands = new SaveOperation(false);
        var failedContext = new SavableEditorContext(session.Project, failedCommands);
        session.EditorService.TabItems.Add(new EditorTabItem(failedContext));
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig
            {
                AutoCommitOnClose = true,
            },
            _ => backend);
        await WaitUntilAsync(() =>
            ReferenceEquals(coordinator.CurrentService, backend)
            && coordinator.IsTracked.Value);
        var answers = new Queue<bool>(new[] { false, true });
        var activitiesWhileConfirming = new List<ProjectLifecycleActivity>();
        coordinator.ConfirmCloseWithoutSnapshotAsync = _ =>
        {
            activitiesWhileConfirming.Add(session.EditorService.LifecycleActivity.Value);
            return Task.FromResult(answers.Dequeue());
        };

        bool closedAfterDecline = await TestShell.Project.TryCloseProjectAsync(
            session.Project,
            ProjectService.ProjectCloseIntent.SaveChanges);
        HeadlessTestHelpers.Settle();
        Project? openAfterDecline = TestShell.Project.CurrentProject.Value;
        // The aborted close gives the editor back instead of leaving it behind the progress view.
        ProjectLifecycleActivity activityAfterDecline = session.EditorService.LifecycleActivity.Value;
        bool editorEnabledAfterDecline = failedContext.IsEnabled.Value;
        bool closedAfterAccept = await TestShell.Project.TryCloseProjectAsync(
            session.Project,
            ProjectService.ProjectCloseIntent.SaveChanges);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(closedAfterDecline, Is.False);
            Assert.That(openAfterDecline, Is.SameAs(session.Project));
            Assert.That(activityAfterDecline, Is.EqualTo(ProjectLifecycleActivity.None));
            Assert.That(editorEnabledAfterDecline, Is.True);
            Assert.That(
                activitiesWhileConfirming,
                Is.EqualTo(new[]
                {
                    ProjectLifecycleActivity.ClosingProject,
                    ProjectLifecycleActivity.ClosingProject,
                }));
            Assert.That(
                session.EditorService.LifecycleActivity.Value,
                Is.EqualTo(ProjectLifecycleActivity.None));
            Assert.That(closedAfterAccept, Is.True);
            Assert.That(answers, Is.Empty);
            Assert.That(failedCommands.SaveCalls, Is.EqualTo(2));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            // What is on disk may be half-saved, so it must not become the version to return to.
            Assert.That(
                backend.RetirementSnapshots,
                Is.EqualTo(new ProjectVersionControlFinalSnapshot?[] { null }));
        });
    }

    [AvaloniaTest]
    public async Task Project_close_waits_for_activation_and_retires_the_tracked_backend_with_a_final_snapshot()
    {
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-during-activation");

        try
        {
            var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
            };

            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = true },
                candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            IProjectVersionControlService? coordinatorServiceAtDiscoveryRetirement = discovery;
            IProjectVersionControlService? editorServiceAtDiscoveryRetirement = discovery;
            bool trackedAtDiscoveryRetirement = false;
            discovery.RetirementStarting = () =>
            {
                coordinatorServiceAtDiscoveryRetirement = coordinator.CurrentService;
                editorServiceAtDiscoveryRetirement =
                    session.EditorService.ProjectVersionControlService.Value;
                trackedAtDiscoveryRetirement = coordinator.IsTracked.Value;
            };

            Task closing = TestShell.Project.CloseProjectAsync();
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
        }
    }

    [AvaloniaTest]
    public async Task Background_project_close_waits_for_every_editor_context_before_retiring_the_backend()
    {
        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-editor-disposal-barrier");

        try
        {
            var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = true },
                candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

            TestShell.Editor.TabItems.Add(new EditorTabItem(
                new BlockingDisposeEditorContext(
                    new Scene { Uri = new Uri(Path.Combine(session.Root, "blocking.scene")) },
                    disposeStarted,
                    releaseDispose.Task)));

            Task closing = Task.Run(() => TestShell.Project.CloseProjectAsync());
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(tracked.RetirementCalls, Is.Zero);
                Assert.That(tracked.CommitAllCalls, Is.Zero);
            });

            releaseDispose.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(tracked.RetirementCalls, Is.EqualTo(1));
                Assert.That(tracked.RetirementSnapshots.Single(), Is.Not.Null);
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
            });
        }
        finally
        {
            releaseDispose.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Close_snapshot_without_identity_warns_once_and_still_closes_the_project()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-missing-identity-warning");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            RetirementResult = new CommitResult.SkippedNoIdentity(),
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = true },
            candidate => candidate is null ? discovery : backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        using var notifications = new NotificationCapture();

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(backend.RetirementCalls, Is.EqualTo(1));
            Assert.That(backend.RetirementSnapshots.Single(), Is.Not.Null);
            Assert.That(
                notifications.All.Count(item =>
                    item.Type == NotificationType.Warning
                    && item.Message == Strings.VersionControl_MissingIdentityNotice),
                Is.EqualTo(1));
            Assert.That(
                notifications.All.Count(item => item.Type == NotificationType.Warning),
                Is.EqualTo(1),
                "A skipped close snapshot must not also report a failed one.");
        });
    }

    [AvaloniaTest]
    public async Task Close_snapshot_retirement_failure_warns_once_and_still_closes_the_project()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-retirement-failure-warning");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            RetirementStarting = () =>
                throw new InvalidOperationException("simulated retirement failure"),
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = true },
            candidate => candidate is null ? discovery : backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        using var notifications = new NotificationCapture();

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(backend.RetirementCalls, Is.EqualTo(1));
            Assert.That(
                notifications.All.Count(item =>
                    item.Type == NotificationType.Warning
                    && item.Message == string.Format(
                        Strings.VersionControl_SaveSnapshotFailedFormat,
                        "simulated retirement failure")),
                Is.EqualTo(1),
                "The close warning must say what stopped the final snapshot.");
            Assert.That(
                notifications.All.Any(item =>
                    item.Message == Strings.VersionControl_MissingIdentityNotice),
                Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Project_close_passes_a_final_snapshot_to_an_untracked_owned_backend()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-untracked-backend");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip);

        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = true },
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        await TestShell.Project.CloseProjectAsync();

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

    [AvaloniaTest]
    public async Task Caller_cancellation_after_the_close_commit_point_does_not_skip_retirement()
    {
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            cancelAfterCommitPoint = null;
        using var closeCancellation = new CancellationTokenSource();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-post-commit-cancellation");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            var config = new VersionControlConfig
            {
                AutoCommitOnClose = true,
            };

            cancelAfterCommitPoint = (_, finalizerCancellation) =>
            {
                Assert.That(finalizerCancellation, Is.EqualTo(CancellationToken.None));
                closeCancellation.Cancel();
                return Task.CompletedTask;
            };
            TestShell.Project.ClosingFinalizing += cancelAfterCommitPoint;
            VersionControlCoordinator coordinator = session.Start(config, _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            await TestShell.Project.CloseProjectAsync(closeCancellation.Token)
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
        }
    }

    [AvaloniaTest]
    public Task Manual_commit_keeps_editors_suspended_through_commit_publication()
        => AssertManualCommitEditorSuspensionAsync(requireIdentity: false);

    [AvaloniaTest]
    public Task Manual_commit_identity_retry_resuspends_through_commit_publication()
        => AssertManualCommitEditorSuspensionAsync(requireIdentity: true);

    private static async Task AssertManualCommitEditorSuspensionAsync(bool requireIdentity)
    {
        var commitStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            requireIdentity
                ? "version-control-manual-identity-suspension"
                : "version-control-manual-suspension");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                CommitAllResult = new CommitResult.NoChanges(),
                CommitAllStarted = commitStarted,
                CommitAllRelease = releaseCommit.Task,
                RequireIdentityForCommit = requireIdentity,
            };
            var commands = new SaveOperation(true);
            var context = new SavableEditorContext(session.Project, commands);
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig(),
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            bool? identityCallbackSawEditorEnabled = null;
            coordinator.RequestIdentityAsync = _ =>
            {
                identityCallbackSawEditorEnabled = context.IsEnabled.Value;
                return Task.FromResult<GitIdentity?>(
                    new GitIdentity("Manual Commit Test", "manual@example.invalid"));
            };

            Task<CommitResult> commit = coordinator.CommitManualAsync("suspended commit");
            await commitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(commit.IsCompleted, Is.False);
                Assert.That(context.IsEnabled.Value, Is.False);
                Assert.That(commands.SaveCalls, Is.EqualTo(requireIdentity ? 2 : 1));
                Assert.That(backend.CommitAllCalls, Is.EqualTo(requireIdentity ? 2 : 1));
                Assert.That(
                    identityCallbackSawEditorEnabled,
                    Is.EqualTo(requireIdentity ? true : null));
            });

            releaseCommit.TrySetResult();
            Assert.That(
                await commit.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(context.IsEnabled.Value, Is.True);
        }
        finally
        {
            releaseCommit.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_shows_its_progress_in_place_of_the_editors_while_they_are_suspended()
    {
        var initializeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialize = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-progress");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                InitializeStarted = initializeStarted,
                InitializeRelease = releaseInitialize.Task,
            };
            var commands = new SaveOperation(true);
            var context = new SavableEditorContext(session.Project, commands);
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            using var presentation = new EditorPresentationRecorder(session.EditorService, context);
            ProjectLifecycleActivity activityWhileSaving = ProjectLifecycleActivity.None;
            commands.Saving = () => activityWhileSaving = session.EditorService.LifecycleActivity.Value;
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            int identityRequests = 0;
            coordinator.RequestIdentityAsync = _ =>
            {
                identityRequests++;
                return Task.FromResult<GitIdentity?>(null);
            };
            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(session.Project);
            await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(initialization.IsCompleted, Is.False);
                Assert.That(identityRequests, Is.Zero);
                Assert.That(commands.SaveCalls, Is.EqualTo(1));
                Assert.That(backend.InitializeCalls, Is.EqualTo(1));
                Assert.That(
                    activityWhileSaving,
                    Is.EqualTo(ProjectLifecycleActivity.EnablingVersionControl));
                Assert.That(
                    session.EditorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.EnablingVersionControl));
                Assert.That(context.IsEnabled.Value, Is.False);
            });

            releaseInitialize.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(context.IsEnabled.Value, Is.True);
                Assert.That(
                    presentation.Activities,
                    Is.EqualTo(new[]
                    {
                        ProjectLifecycleActivity.None,
                        ProjectLifecycleActivity.EnablingVersionControl,
                        ProjectLifecycleActivity.None,
                    }));
                // The progress view appears before the editor is disabled, and goes only after it is enabled.
                Assert.That(
                    presentation.EditorStates,
                    Is.EqualTo(new[]
                    {
                        (false, ProjectLifecycleActivity.EnablingVersionControl),
                        (true, ProjectLifecycleActivity.EnablingVersionControl),
                    }));
            });
        }
        finally
        {
            releaseInitialize.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_hides_its_progress_while_asking_for_an_identity()
    {
        var retryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetry = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-identity-progress");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                RequireIdentityForInitialization = true,
                InitializeStarted = retryStarted,
                InitializeRelease = releaseRetry.Task,
            };
            var commands = new SaveOperation(true);
            var context = new SavableEditorContext(session.Project, commands);
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            using var presentation = new EditorPresentationRecorder(session.EditorService, context);
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            int identityRequests = 0;
            ProjectLifecycleActivity? activityWhileAsking = null;
            bool? editorEnabledWhileAsking = null;
            coordinator.RequestIdentityAsync = _ =>
            {
                identityRequests++;
                activityWhileAsking = session.EditorService.LifecycleActivity.Value;
                editorEnabledWhileAsking = context.IsEnabled.Value;
                return Task.FromResult<GitIdentity?>(
                    new GitIdentity("Identity Retry", "identity-retry@example.invalid"));
            };
            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(session.Project);
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(initialization.IsCompleted, Is.False);
                Assert.That(identityRequests, Is.EqualTo(1));
                // The user can keep editing, in a visible editor, while entering an identity.
                Assert.That(activityWhileAsking, Is.EqualTo(ProjectLifecycleActivity.None));
                Assert.That(editorEnabledWhileAsking, Is.True);
                Assert.That(
                    session.EditorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.EnablingVersionControl));
                Assert.That(context.IsEnabled.Value, Is.False);
                Assert.That(commands.SaveCalls, Is.EqualTo(2));
                Assert.That(backend.InitializeCalls, Is.EqualTo(2));
            });

            releaseRetry.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(context.IsEnabled.Value, Is.True);
                Assert.That(
                    presentation.Activities,
                    Is.EqualTo(new[]
                    {
                        ProjectLifecycleActivity.None,
                        ProjectLifecycleActivity.EnablingVersionControl,
                        ProjectLifecycleActivity.None,
                        ProjectLifecycleActivity.EnablingVersionControl,
                        ProjectLifecycleActivity.None,
                    }));
                Assert.That(
                    presentation.EditorStates,
                    Is.EqualTo(new[]
                    {
                        (false, ProjectLifecycleActivity.EnablingVersionControl),
                        (true, ProjectLifecycleActivity.EnablingVersionControl),
                        (false, ProjectLifecycleActivity.EnablingVersionControl),
                        (true, ProjectLifecycleActivity.EnablingVersionControl),
                    }));
            });
        }
        finally
        {
            releaseRetry.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_hides_its_progress_when_the_project_cannot_be_saved()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-progress-save-failure");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip);
        var commands = new SaveOperation(false);
        var context = new SavableEditorContext(session.Project, commands);
        session.EditorService.TabItems.Add(new EditorTabItem(context));
        using var presentation = new EditorPresentationRecorder(session.EditorService, context);
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        bool initialized = await coordinator.InitializeCurrentProjectAsync(session.Project);

        Assert.Multiple(() =>
        {
            Assert.That(initialized, Is.False);
            Assert.That(commands.SaveCalls, Is.EqualTo(1));
            Assert.That(backend.InitializeCalls, Is.Zero);
            Assert.That(context.IsEnabled.Value, Is.True);
            Assert.That(
                presentation.Activities,
                Is.EqualTo(new[]
                {
                    ProjectLifecycleActivity.None,
                    ProjectLifecycleActivity.EnablingVersionControl,
                    ProjectLifecycleActivity.None,
                }));
            Assert.That(
                presentation.EditorStates,
                Is.EqualTo(new[]
                {
                    (false, ProjectLifecycleActivity.EnablingVersionControl),
                    (true, ProjectLifecycleActivity.EnablingVersionControl),
                }));
        });
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_hides_its_progress_when_the_initial_commit_fails()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-progress-commit-failure");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip)
        {
            InitializeFailureAfterAttach = new InvalidOperationException("The initial commit failed."),
        };
        var context = new SavableEditorContext(session.Project, new SaveOperation(true));
        session.EditorService.TabItems.Add(new EditorTabItem(context));
        using var presentation = new EditorPresentationRecorder(session.EditorService, context);
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.InitializeCurrentProjectAsync(session.Project));

        Assert.Multiple(() =>
        {
            Assert.That(failure?.Message, Is.EqualTo("The initial commit failed."));
            Assert.That(backend.InitializeCalls, Is.EqualTo(1));
            Assert.That(context.IsEnabled.Value, Is.True);
            Assert.That(
                presentation.Activities,
                Is.EqualTo(new[]
                {
                    ProjectLifecycleActivity.None,
                    ProjectLifecycleActivity.EnablingVersionControl,
                    ProjectLifecycleActivity.None,
                }));
            Assert.That(
                presentation.EditorStates,
                Is.EqualTo(new[]
                {
                    (false, ProjectLifecycleActivity.EnablingVersionControl),
                    (true, ProjectLifecycleActivity.EnablingVersionControl),
                }));
        });
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_hides_its_progress_when_the_editors_cannot_be_suspended()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-progress-suspension-failure");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip);
        var commands = new SaveOperation(true);
        var context = new SavableEditorContext(session.Project, commands);
        session.EditorService.TabItems.Add(new EditorTabItem(context));
        using var presentation = new EditorPresentationRecorder(session.EditorService, context);
        // An editor that cannot be disabled makes the suspension itself fail.
        using IDisposable refusal = context.IsEnabled.Subscribe(enabled =>
        {
            if (!enabled)
            {
                throw new InvalidOperationException("The editor cannot be disabled.");
            }
        });
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await coordinator.InitializeCurrentProjectAsync(session.Project));

        Assert.Multiple(() =>
        {
            Assert.That(
                failure?.InnerExceptions.Select(ex => ex.Message),
                Is.EqualTo(new[] { "The editor cannot be disabled." }));
            Assert.That(commands.SaveCalls, Is.Zero);
            Assert.That(backend.InitializeCalls, Is.Zero);
            Assert.That(context.IsEnabled.Value, Is.True);
            // The progress view does not outlive the failed suspension, or it would cover the editor for good.
            Assert.That(
                presentation.Activities,
                Is.EqualTo(new[]
                {
                    ProjectLifecycleActivity.None,
                    ProjectLifecycleActivity.EnablingVersionControl,
                    ProjectLifecycleActivity.None,
                }));
        });
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_from_a_worker_thread_shows_its_progress_before_suspending_the_editors()
    {
        var initializeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialize = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-progress-worker");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                InitializeStarted = initializeStarted,
                InitializeRelease = releaseInitialize.Task,
            };
            var context = new SavableEditorContext(session.Project, new SaveOperation(true));
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            using var presentation = new EditorPresentationRecorder(session.EditorService, context);
            var editorChangesOnUiThread = new List<bool>();
            using IDisposable editorThreads = context.IsEnabled
                .Skip(1)
                .Subscribe(_ => editorChangesOnUiThread.Add(
                    Avalonia.Threading.Dispatcher.UIThread.CheckAccess()));
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            // The call moves to the UI thread before it touches the editors, so the progress and the
            // suspension still appear together.
            Task<bool> initialization = Task.Run(() => coordinator.InitializeCurrentProjectAsync(session.Project));
            await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(
                presentation.EditorStates,
                Is.EqualTo(new[] { (false, ProjectLifecycleActivity.EnablingVersionControl) }));

            releaseInitialize.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(context.IsEnabled.Value, Is.True);
                Assert.That(
                    presentation.Activities,
                    Is.EqualTo(new[]
                    {
                        ProjectLifecycleActivity.None,
                        ProjectLifecycleActivity.EnablingVersionControl,
                        ProjectLifecycleActivity.None,
                    }));
                Assert.That(
                    presentation.EditorStates,
                    Is.EqualTo(new[]
                    {
                        (false, ProjectLifecycleActivity.EnablingVersionControl),
                        (true, ProjectLifecycleActivity.EnablingVersionControl),
                    }));
                Assert.That(editorChangesOnUiThread, Is.EqualTo(new[] { true, true }));
            });
        }
        finally
        {
            releaseInitialize.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Closing_during_the_initial_commit_replaces_its_progress_with_the_close()
    {
        var initializeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // Never released: the close has to cancel the initial commit.
        var releaseInitialize = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? initialization = null;
        Task<bool>? close = null;
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-progress-close");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                InitializeStarted = initializeStarted,
                InitializeRelease = releaseInitialize.Task,
            };
            var context = new SavableEditorContext(session.Project, new SaveOperation(true));
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            using var presentation = new EditorPresentationRecorder(session.EditorService, context);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = true },
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            initialization = coordinator.InitializeCurrentProjectAsync(session.Project);
            await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            close = TestShell.Project.TryCloseProjectAsync(
                session.Project,
                ProjectService.ProjectCloseIntent.SaveChanges);
            Assert.That(await close.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            HeadlessTestHelpers.Settle();

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(
                    presentation.Activities,
                    Is.EqualTo(new[]
                    {
                        ProjectLifecycleActivity.None,
                        ProjectLifecycleActivity.EnablingVersionControl,
                        ProjectLifecycleActivity.ClosingProject,
                        ProjectLifecycleActivity.None,
                    }));
                // The editor is enabled again when the cancelled commit releases it, still behind a progress view.
                Assert.That(
                    presentation.EditorStates.Select(state => state.Enabled),
                    Is.EqualTo(new[] { false, true }));
                Assert.That(
                    presentation.EditorStates.Select(state => state.Activity),
                    Has.None.EqualTo(ProjectLifecycleActivity.None));
            });
        }
        finally
        {
            releaseInitialize.TrySetResult();
            if (initialization is not null)
            {
                try
                {
                    await initialization.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (close is not null)
            {
                await close.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [AvaloniaTest]
    public async Task InitializeCurrentProject_propagates_cancellation_to_the_identity_request()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-identity-cancellation");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip)
        {
            RequireIdentityForInitialization = true,
        };

        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        using var cancellation = new CancellationTokenSource();
        CancellationToken observedToken = default;
        int identityRequests = 0;

        coordinator.RequestIdentityAsync = token =>
        {
            identityRequests++;
            observedToken = token;
            cancellation.Cancel();
            return Task.FromCanceled<GitIdentity?>(token);
        };
        Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
            TestShell.Project.CurrentProject.Value!,
            cancellation.Token);

        await Assert.CatchAsync<OperationCanceledException>(async () => await initialization);

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

    [AvaloniaTest]
    public async Task InitializeCurrentProject_passes_the_requested_identity_to_the_retry()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-identity-retry");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip)
        {
            RequireIdentityForInitialization = true,
        };
        var identity = new GitIdentity("Identity Retry", "identity-retry@example.invalid");
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        coordinator.RequestIdentityAsync = _ => Task.FromResult<GitIdentity?>(identity);
        bool initialized = await coordinator.InitializeCurrentProjectAsync(
            TestShell.Project.CurrentProject.Value!);

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

    [AvaloniaTest]
    public async Task InitializeCurrentProject_rejects_a_project_that_is_no_longer_open()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-stale-initialization-target");
        Project currentProject = await CreateProjectForFakeVersionControlAsync(
            "version-control-current-initialization-target");
        var backend = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip);
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await coordinator.InitializeCurrentProjectAsync(session.Project));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("no longer the open project"));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(currentProject));
            Assert.That(backend.InitializeCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Async_disposal_cancels_and_joins_an_initialization_identity_request()
    {
        var identity = new BlockingPrompt<GitIdentity?>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-disposal");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                RequireIdentityForInitialization = true,
            };
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            coordinator.RequestIdentityAsync = identity.AnswerLateAsync;
            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!);
            await identity.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = coordinator.DisposeAsync().AsTask();
            await identity.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(backend.RetirementCalls, Is.Zero);
            });

            identity.Release(new GitIdentity("Disposal Test", "disposal@example.invalid"));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)));

            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            identity.Release(new GitIdentity("Disposal Test", "disposal@example.invalid"));
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_continues_when_activation_cancellation_callback_throws()
    {
        Task? disposal = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activationTokenObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration throwingRegistration = default;
        int activationCancellationCallbackCalls = 0;
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-throwing-activation-cancellation");

        try
        {
            var discovery = new PullCycleTestBackend(
                repository: null,
                session.Repository,
                MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
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
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await activationTokenObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, discovery)
                && ReferenceEquals(
                    session.EditorService.ProjectVersionControlService.Value,
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
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
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
        }
    }

    [AvaloniaTest]
    public async Task Superseded_activation_never_publishes_its_tracked_backend()
    {
        var staleHygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-superseded-activation");

        try
        {
            var staleDiscovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            // Finishes its hygiene once released, whether or not its activation was canceled meanwhile.
            var staleTracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                EnsureHygieneOverride = async _ =>
                {
                    staleHygieneStarted.TrySetResult();
                    await releaseStaleHygiene.Task;
                },
            };
            var currentDiscovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var currentTracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            var discoveries = new Queue<PullCycleTestBackend>([staleDiscovery, currentDiscovery]);
            var trackedBackends = new Queue<PullCycleTestBackend>([staleTracked, currentTracked]);
            var publishedServices = new List<IProjectVersionControlService?>();
            using IDisposable subscription = session.EditorService.ProjectVersionControlService.Subscribe(
                publishedServices.Add);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig(),
                candidate => candidate is null
                    ? discoveries.Dequeue()
                    : trackedBackends.Dequeue());
            await staleHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.OnProjectChanged(session.Project);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, currentTracked)
                && coordinator.IsTracked.Value);
            releaseStaleHygiene.TrySetResult();
            await WaitUntilAsync(() => staleTracked.DisposeCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(coordinator.CurrentService, Is.SameAs(currentTracked));
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.SameAs(currentTracked));
                Assert.That(publishedServices, Does.Not.Contain(staleTracked));
                Assert.That(staleTracked.RetirementCalls, Is.EqualTo(1));
                Assert.That(staleDiscovery.RetirementCalls, Is.EqualTo(1));
                Assert.That(currentTracked.RetirementCalls, Is.Zero);
            });
        }
        finally
        {
            releaseStaleHygiene.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Failed_activation_warns_with_its_reason_and_leaves_the_project_untracked()
    {
        const string reason = "fatal: simulated repository refusal";
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-activation-failure-reason");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            EnsureHygieneOverride = _ =>
                Task.FromException(new GitOperationException(128, reason)),
        };
        using var notifications = new NotificationCapture();
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            candidate => candidate is null ? discovery : backend);
        await WaitUntilAsync(() => !notifications.All.IsEmpty);

        Notification warning = notifications.All.Single();
        Assert.Multiple(() =>
        {
            Assert.That(warning.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(warning.Title, Is.EqualTo(Strings.VersionControl));
            Assert.That(
                warning.Message,
                Is.EqualTo(string.Format(Strings.VersionControl_ActivationFailedFormat, reason)),
                "The warning must say why version control could not be enabled.");
            Assert.That(coordinator.IsTracked.Value, Is.False);
            Assert.That(coordinator.CurrentService, Is.Not.SameAs(backend));
            Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Not.SameAs(backend));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
    }

    [AvaloniaTest]
    public async Task Closing_cancels_and_joins_an_initialization_before_retiring_the_backend()
    {
        Task? initialization = null;
        Task? closing = null;
        var identity = new BlockingPrompt<GitIdentity?>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-initialize-close");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                RequireIdentityForInitialization = true,
            };
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            coordinator.RequestIdentityAsync = identity.AnswerLateAsync;
            initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!);
            await identity.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            closing = TestShell.Project.CloseProjectAsync();
            await identity.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await coordinator.NotifySavedAsync());

            Assert.Multiple(() =>
            {
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(backend.RetirementCalls, Is.Zero);
            });

            identity.Release(new GitIdentity("Close Barrier Test", "close-barrier@example.invalid"));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)));

            await closing.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(backend.RetirementCalls, Is.GreaterThanOrEqualTo(1));
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });
        }
        finally
        {
            identity.Release(new GitIdentity("Close Barrier Test", "close-barrier@example.invalid"));
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
        }
    }

    [AvaloniaTest]
    public async Task Closing_rejects_operations_until_the_project_change_is_committed()
    {
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            subsequentClosingFinalizer = null;
        Task? closing = null;
        var subsequentHandlerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubsequentHandler = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-commit-gap");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            subsequentClosingFinalizer = async (_, _) =>
            {
                subsequentHandlerStarted.TrySetResult();
                await releaseSubsequentHandler.Task.WaitAsync(TimeSpan.FromSeconds(5));
            };
            TestShell.Project.ClosingFinalizing += subsequentClosingFinalizer;

            closing = TestShell.Project.CloseProjectAsync();
            await subsequentHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            IProjectVersionControlService? coordinatorServiceDuringClose =
                coordinator.CurrentService;
            IProjectVersionControlService? editorServiceDuringClose =
                session.EditorService.ProjectVersionControlService.Value;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await coordinator.SetRemoteAsync("https://example.invalid/rejected-close.git"));

            RemoteOpResult rejectedPull = await coordinator.PullAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(coordinatorServiceDuringClose, Is.Null);
                Assert.That(editorServiceDuringClose, Is.Null);
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(rejectedPull, Is.EqualTo(new RemoteOpResult.Failed(
                    "The open project changed while the pull was being prepared.")));
                Assert.That(backend.PreflightCalls, Is.Zero);
                Assert.That(backend.RetirementCalls, Is.EqualTo(1));
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
        }
    }

    [AvaloniaTest]
    public async Task Closing_from_a_state_publication_hides_the_service_before_retirement()
    {
        Task? closing = null;
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-reentrant-close-publication");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                RetirementRelease = releaseRetirement.Task,
            };
            var probe = new MutableGitInstallationProbe();
            var config = new VersionControlConfig();
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            VersionControlCoordinator coordinator = session.Start(config, _ => backend, locator);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    session.EditorService.ProjectVersionControlService.Value,
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
                    session.EditorService.ProjectVersionControlService.Value;
                trackedAtRetirement = coordinator.IsTracked.Value;
            };

            using IDisposable subscription = coordinator.IsGitAvailable.Subscribe(
                isAvailable =>
                {
                    if (isAvailable && closing is null)
                    {
                        closing = TestShell.Project.CloseProjectAsync();
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
        }
    }

    [AvaloniaTest]
    public async Task Close_aborted_before_retirement_keeps_the_backend_visible()
    {
        Task? closing = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>? blockingClosing = null;
        var closingStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var closeCancellation = new CancellationTokenSource();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pre-retirement-close-abort");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            var probe = new MutableGitInstallationProbe();
            var config = new VersionControlConfig();
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            VersionControlCoordinator coordinator = session.Start(config, _ => backend, locator);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    session.EditorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            blockingClosing = async (_, cancellationToken) =>
            {
                closingStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            };
            TestShell.Project.Closing += blockingClosing;
            closing = TestShell.Project.CloseProjectAsync(closeCancellation.Token);
            await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closeCancellation.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await closing!.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
                Assert.That(coordinator.CurrentService, Is.SameAs(backend));
                Assert.That(
                    session.EditorService.ProjectVersionControlService.Value,
                    Is.SameAs(backend));
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(backend.RetirementCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
            });

            TestShell.Project.Closing -= blockingClosing;
            blockingClosing = null;
            await TestShell.Project.CloseProjectAsync();

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
        }
    }

    [AvaloniaTest]
    public async Task Closing_finalizer_failure_does_not_abort_the_committed_close()
    {
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            throwingClosingFinalizer = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>?
            observingClosingFinalizer = null;
        bool observingFinalizerCalled = false;
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-aborted-close-lifecycle");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
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

            await TestShell.Project.CloseProjectAsync();

            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(observingFinalizerCalled, Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
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
        }
    }

    [AvaloniaTest]
    public async Task Backend_retirement_failure_does_not_abort_committed_close_or_hold_operation_barrier()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-failed-close-retirement");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            RetirementStarting = () =>
                throw new InvalidOperationException("Expected retirement failure."),
        };
        var config = new VersionControlConfig
        {
            AutoCommitOnClose = true,
        };
        VersionControlCoordinator coordinator = session.Start(config, _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        await TestShell.Project.CloseProjectAsync();
        await coordinator.NotifySavedAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(coordinator.CurrentService, Is.Null);
            Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
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

    [AvaloniaTest]
    public async Task Async_disposal_continues_when_an_operation_cancellation_callback_throws()
    {
        Task<bool>? initialization = null;
        var identity = new BlockingPrompt<GitIdentity?>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-throwing-cancellation-disposal");

        try
        {
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip)
            {
                RequireIdentityForInitialization = true,
            };
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

            coordinator.RequestIdentityAsync = async cancellationToken =>
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(
                    static () => throw new InvalidOperationException(
                        "Cancellation callback failure."));
                return await identity.AnswerLateAsync(cancellationToken);
            };
            initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!);
            await identity.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

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

            await identity.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(synchronousDisposalFailure, Is.Null);
                Assert.That(disposal, Is.Not.Null);
                Assert.That(disposal?.IsCompleted, Is.False);
            });

            identity.Release(null);
            Assert.That(await initialization.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
            await disposal!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backend.SetLocalIdentityCalls, Is.Zero);
                Assert.That(backend.CallsAfterRetirement, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            identity.Release(null);
            if (initialization is not null)
            {
                await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_joins_an_active_conflict_marker_warning()
    {
        Task? opening = null;
        var warningStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWarning = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-opening-warning-disposal");

        try
        {
            // An element file in the scene's folder is one the project loads.
            string sceneDirectory = Path.GetDirectoryName(
                session.Project.Items.OfType<Scene>().First().Uri!.LocalPath)!;
            await File.WriteAllTextAsync(
                Path.Combine(sceneDirectory, "blocked-warning.belm"),
                "<<<<<<< ours\n{}\n=======\n{}\n>>>>>>> theirs\n");
            var backend = new PullCycleTestBackend(
                repository: null,
                discoveredRepository: null,
                MainTip);
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            coordinator.WarnConflictMarkersAsync = async _ =>
            {
                warningStarted.TrySetResult();
                await releaseWarning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            };

            opening = TestShell.Project.OpenProject(session.Project.Uri!.LocalPath);
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
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseWarning.TrySetResult();
            if (opening is not null)
            {
                await opening.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_joins_an_active_close_barrier_before_clearing_state()
    {
        Task? closing = null;
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-barrier-disposal");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
            };
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    session.EditorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            closing = TestShell.Project.CloseProjectAsync();
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
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
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
        }
    }

    [AvaloniaTest]
    public async Task Git_executable_override_rediscoveries_only_an_unassociated_backend()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-override-reactivation");
        string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-valid-git");
        string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-invalid-git");
        var probe = new ConfigurableGitInstallationProbe(validGitPath);
        var config = new VersionControlConfig
        {
            GitExecutablePath = invalidGitPath,
        };
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
        var createdBackends = new List<PullCycleTestBackend>();
        VersionControlCoordinator coordinator = session.Start(
            config,
            candidate =>
            {
                var backend = new PullCycleTestBackend(candidate, session.Repository, MainTip)
                {
                    AvailabilityOverride = locator.LocateAsync,
                };
                createdBackends.Add(backend);
                return backend;
            },
            locator);
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
                session.EditorService.ProjectVersionControlService.Value,
                coordinator.CurrentService));
        IProjectVersionControlService trackedService = coordinator.CurrentService!;

        int backendCountAfterTracking = createdBackends.Count;
        config.GitExecutablePath = invalidGitPath;
        await WaitUntilAsync(() => !coordinator.IsGitAvailable.Value);

        Assert.Multiple(() =>
        {
            Assert.That(trackedService.Repository, Is.Not.Null);
            Assert.That(coordinator.CurrentService, Is.SameAs(trackedService));
            Assert.That(session.EditorService.ProjectVersionControlService.Value,
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

    [AvaloniaTest]
    public async Task Lfs_configuration_reapplies_hygiene_to_the_open_tracked_backend()
    {
        var appliedValues = new List<bool>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-lfs-config-hygiene");
        var config = new VersionControlConfig
        {
            UseLfsWhenAvailable = false,
        };
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
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
        VersionControlCoordinator coordinator = session.Start(config, _ => backend);
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
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
            Assert.That(coordinator.CurrentService, Is.SameAs(backend));
            Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.SameAs(backend));
            Assert.That(backend.RetirementCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Newer_git_configuration_cancels_a_stale_unassociated_rediscovery()
    {
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hygieneCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestInvalidProbeCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-stale-git-configuration");

        try
        {
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-stale-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-stale-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            PullCycleTestBackend? staleTrackedBackend = null;
            var publishedServices = new List<IProjectVersionControlService?>();
            using IDisposable subscription = session.EditorService.ProjectVersionControlService.Subscribe(
                publishedServices.Add);
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    bool blockHygiene = candidate is not null && staleTrackedBackend is null;
                    var backend = new PullCycleTestBackend(candidate, session.Repository, MainTip)
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
                },
                locator);
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
                    session.EditorService.ProjectVersionControlService.Value,
                    coordinator.CurrentService));

            Assert.Multiple(() =>
            {
                Assert.That(staleTrackedBackend, Is.Not.Null);
                Assert.That(publishedServices, Does.Not.Contain(staleTrackedBackend));
                Assert.That(coordinator.CurrentService, Is.Not.SameAs(staleTrackedBackend));
                Assert.That(session.EditorService.ProjectVersionControlService.Value,
                    Is.SameAs(coordinator.CurrentService));
                Assert.That(staleTrackedBackend!.RetirementCalls, Is.EqualTo(1));
                Assert.That(staleTrackedBackend.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Waiting_operation_does_not_interleave_between_coalesced_git_configurations()
    {
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
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-coalesced-git-config-waiter");

        try
        {
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
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    int untrackedIndex = candidate is null
                        ? Interlocked.Increment(ref untrackedBackends)
                        : 0;
                    return new PullCycleTestBackend(candidate, session.Repository, MainTip)
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
                },
                locator);
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);

            config.GitExecutablePath = validGitPath;
            await staleHygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!);
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
        }
    }

    [AvaloniaTest]
    public async Task Git_configuration_change_during_disposal_does_not_start_a_new_activation()
    {
        Task? disposal = null;
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-config-disposal");

        try
        {
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-disposal-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-disposal-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            int factoryCalls = 0;
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return new PullCycleTestBackend(candidate, session.Repository, MainTip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        RetirementStarted = retirementStarted,
                        RetirementRelease = releaseRetirement.Task,
                    };
                },
                locator);
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
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
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
        }
    }

    [AvaloniaTest]
    public async Task Git_configuration_rediscovery_waits_for_inflight_initialization()
    {
        var initializeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialize = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-config-initialize");

        try
        {
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-init-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-init-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    bool initialBackend = createdBackends.Count == 0;
                    var backend = new PullCycleTestBackend(candidate, session.Repository, MainTip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        InitializeStarted = initialBackend ? initializeStarted : null,
                        InitializeRelease = initialBackend ? releaseInitialize.Task : null,
                    };
                    createdBackends.Add(backend);
                    return backend;
                },
                locator);
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value);
            var initialService = (PullCycleTestBackend)coordinator.CurrentService!;

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!);
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
        }
    }

    [AvaloniaTest]
    public async Task Initialization_started_during_git_rediscovery_waits_for_activation()
    {
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-config-wait-initialize");

        try
        {
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-init-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-init-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, session.Repository, MainTip)
                    {
                        AvailabilityOverride = locator.LocateAsync,
                        EnsureHygieneStarted = candidate is not null ? hygieneStarted : null,
                        EnsureHygieneRelease = candidate is not null ? releaseHygiene.Task : null,
                    };
                    createdBackends.Add(backend);
                    return backend;
                },
                locator);
            await WaitUntilAsync(() =>
                coordinator.CurrentService is { Repository: null }
                && !coordinator.IsGitAvailable.Value
                && !coordinator.IsTracked.Value);

            config.GitExecutablePath = validGitPath;
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> initialization = coordinator.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!);

            Assert.That(initialization.IsCompleted, Is.False);

            releaseHygiene.TrySetResult();
            Assert.That(
                await initialization.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True);
            var trackedBackend = (PullCycleTestBackend)coordinator.CurrentService!;

            Assert.Multiple(() =>
            {
                Assert.That(trackedBackend.Repository, Is.EqualTo(session.Repository));
                Assert.That(trackedBackend.InitializeCalls, Is.EqualTo(1));
                Assert.That(coordinator.IsGitAvailable.Value, Is.True);
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(createdBackends, Does.Contain(trackedBackend));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Branch_started_during_git_rediscovery_waits_for_activation()
    {
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var branchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-config-wait-branch");

        try
        {
            string validGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-branch-valid-git");
            string invalidGitPath = Path.Combine(Path.GetTempPath(), "beutl-wait-branch-invalid-git");
            var probe = new ConfigurableGitInstallationProbe(validGitPath);
            var config = new VersionControlConfig
            {
                GitExecutablePath = invalidGitPath,
            };
            var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
            var createdBackends = new List<PullCycleTestBackend>();
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, session.Repository, MainTip)
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
                },
                locator);
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
                Assert.That(trackedBackend.Repository, Is.EqualTo(session.Repository));
                Assert.That(trackedBackend.RetirementCalls, Is.Zero);
                Assert.That(coordinator.IsGitAvailable.Value, Is.True);
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(createdBackends, Does.Contain(trackedBackend));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Git_configuration_change_during_branch_cycle_preserves_backend_identity()
    {
        var branchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBranch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-config-branch");

        try
        {
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
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
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
            VersionControlCoordinator coordinator = session.Start(
                config,
                _ =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return backend;
                },
                locator);
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
                    Is.EqualTo(session.Project.Uri!.LocalPath));
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.SameAs(backend));
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
        }
    }

    [AvaloniaTest]
    public async Task Close_barrier_defers_git_rediscovery_and_retires_the_tracked_backend_once()
    {
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
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-git-config-close");

        try
        {
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
            VersionControlCoordinator coordinator = session.Start(
                config,
                candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, session.Repository, MainTip)
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
                },
                locator);
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
            abortedClose = TestShell.Project.CloseProjectAsync(closeCancellation.Token);
            await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            config.GitExecutablePath = validGitPath;

            Assert.Multiple(() =>
            {
                Assert.That(abortedClose.IsCompleted, Is.False);
                Assert.That(createdBackends, Has.Count.EqualTo(callsBeforeClose));
                Assert.That(trackedHygieneStarted.Task.IsCompleted, Is.False);
            });

            closeCancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await abortedClose.WaitAsync(TimeSpan.FromSeconds(5)));
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
            Task successfulClose = TestShell.Project.CloseProjectAsync();
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
            await probe.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(probe.StartCount, Is.EqualTo(1), "Availability callers share one discovery.");

            Task disposal = coordinator.DisposeAsync().AsTask();
            await probe.ProbeCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(disposal.IsCompleted, Is.False);
            probe.ReleaseCancelledProbe.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.CatchAsync<OperationCanceledException>(async () => await explicitProbe);

            int publicationsAfterDisposal = servicePublications;
            config.GitExecutablePath = "/git-after-disposal";
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(probe.CancellationCount, Is.EqualTo(1));
                Assert.That(probe.CompletionCount, Is.EqualTo(1));
                Assert.That(servicePublications, Is.EqualTo(publicationsAfterDisposal));
                Assert.That(editorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            probe.ReleaseCancelledProbe.TrySetResult();
            await coordinator.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Failed_stale_lock_removal_keeps_the_lock_visible_and_warns_with_its_path()
    {
        using var notifications = new NotificationCapture();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-lock-manual-removal");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            RemoveRecoverableLockResult = false,
        };
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        coordinator.ConfirmRemoveStaleLockAsync = (_, _) => Task.FromResult(true);
        var lockInfo = new RepositoryLockInfo(
            Path.Combine(session.Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow);

        backend.RaiseRecoverableLock(lockInfo);
        await WaitUntilAsync(() =>
            backend.RemoveRecoverableLockCalls == 1
            && notifications.All.Any(notification =>
                notification.Type == NotificationType.Warning));

        Notification warning = notifications.All.Single(notification =>
            notification.Type == NotificationType.Warning);
        Assert.Multiple(() =>
        {
            Assert.That(backend.RecoverableLock, Is.EqualTo(lockInfo));
            Assert.That(warning.Message, Does.Contain(lockInfo.LockPath));
        });
    }

    [AvaloniaTest]
    public async Task Removable_stale_lock_is_removed_only_after_consent()
    {
        using var notifications = new NotificationCapture();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-lock-removal-consent");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            RemoveRecoverableLockResult = true,
        };
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        int confirmationCount = 0;
        coordinator.ConfirmRemoveStaleLockAsync = (_, _) =>
        {
            Interlocked.Increment(ref confirmationCount);
            return Task.FromResult(true);
        };
        var lockInfo = new RepositoryLockInfo(
            Path.Combine(session.Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow,
            requiresManualRemoval: false);

        backend.RaiseRecoverableLock(lockInfo);
        await WaitUntilAsync(() =>
            notifications.All.Any(notification =>
                notification.Message == Strings.VersionControl_StaleLockRemoved));

        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref confirmationCount), Is.EqualTo(1));
            Assert.That(backend.RemoveRecoverableLockCalls, Is.EqualTo(1));
            Assert.That(backend.RecoverableLock, Is.Null);
            Assert.That(
                notifications.All.Any(notification =>
                    notification.Type == NotificationType.Warning),
                Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Stale_lock_requiring_manual_removal_warns_without_asking_for_consent()
    {
        using var notifications = new NotificationCapture();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-lock-manual-removal-only");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            RemoveRecoverableLockResult = true,
        };
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        int confirmationCount = 0;
        coordinator.ConfirmRemoveStaleLockAsync = (_, _) =>
        {
            Interlocked.Increment(ref confirmationCount);
            return Task.FromResult(true);
        };
        var lockInfo = new RepositoryLockInfo(
            Path.Combine(session.Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow,
            requiresManualRemoval: true);

        backend.RaiseRecoverableLock(lockInfo);
        await WaitUntilAsync(() =>
            notifications.All.Any(notification =>
                notification.Type == NotificationType.Warning));

        Notification warning = notifications.All.Single(notification =>
            notification.Type == NotificationType.Warning);
        Assert.Multiple(() =>
        {
            Assert.That(Volatile.Read(ref confirmationCount), Is.Zero);
            Assert.That(backend.RemoveRecoverableLockCalls, Is.Zero);
            Assert.That(backend.RecoverableLock, Is.SameAs(lockInfo));
            Assert.That(
                warning.Message,
                Is.EqualTo(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.VersionControl_StaleLockManualRemovalRequiredFormat,
                    lockInfo.LockPath)));
        });
    }

    [AvaloniaTest]
    public async Task Stale_lock_confirmation_is_not_reused_for_an_equal_replacement_offer()
    {
        var confirmationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-lock-offer-generation");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                RemoveRecoverableLockResult = true,
            };
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            int confirmationCount = 0;
            coordinator.ConfirmRemoveStaleLockAsync = async (_, _) =>
            {
                if (Interlocked.Increment(ref confirmationCount) == 1)
                {
                    confirmationStarted.TrySetResult();
                    return await releaseConfirmation.Task;
                }

                return false;
            };
            string lockPath = Path.Combine(session.Root, ".git", "index.lock");
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            var firstOffer = new RepositoryLockInfo(lockPath, timestamp);
            var replacementOffer = new RepositoryLockInfo(lockPath, timestamp);

            backend.RaiseRecoverableLock(firstOffer);
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            backend.RaiseRecoverableLock(replacementOffer);
            releaseConfirmation.TrySetResult(true);
            await WaitUntilAsync(() => Volatile.Read(ref confirmationCount) == 2);

            Assert.Multiple(() =>
            {
                Assert.That(firstOffer.LockPath, Is.EqualTo(replacementOffer.LockPath));
                Assert.That(
                    firstOffer.LastWriteTimeUtc,
                    Is.EqualTo(replacementOffer.LastWriteTimeUtc));
                Assert.That(firstOffer, Is.Not.SameAs(replacementOffer));
                Assert.That(backend.RecoverableLock, Is.SameAs(replacementOffer));
                Assert.That(backend.RemoveRecoverableLockCalls, Is.Zero);
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
        }
    }

    [AvaloniaTest]
    public async Task Async_disposal_cancels_and_joins_an_active_lock_recovery_prompt()
    {
        var confirmation = new BlockingPrompt<bool>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-lock-recovery-disposal");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            GlobalConfiguration.Instance.VersionControlConfig,
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        coordinator.ConfirmRemoveStaleLockAsync = (_, cancellationToken) =>
            confirmation.AnswerAsync(cancellationToken);

        backend.RaiseRecoverableLock(new RepositoryLockInfo(
            Path.Combine(session.Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow));
        await confirmation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposal = coordinator.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await confirmation.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(backend.RemoveRecoverableLockCalls, Is.Zero);
            Assert.That(coordinator.CurrentService, Is.Null);
            Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task Async_disposal_waits_for_backend_retirement()
    {
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-retirement-disposal");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
            };
            VersionControlCoordinator coordinator = session.Start(
                GlobalConfiguration.Instance.VersionControlConfig,
                _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && ReferenceEquals(
                    session.EditorService.ProjectVersionControlService.Value,
                    backend)
                && coordinator.IsTracked.Value);

            IProjectVersionControlService? coordinatorServiceAtRetirement = backend;
            IProjectVersionControlService? editorServiceAtRetirement = backend;
            bool trackedAtRetirement = true;
            backend.RetirementStarting = () =>
            {
                coordinatorServiceAtRetirement = coordinator.CurrentService;
                editorServiceAtRetirement =
                    session.EditorService.ProjectVersionControlService.Value;
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
                Assert.That(session.EditorService.ProjectVersionControlService.Value, Is.Null);
            });
        }
        finally
        {
            releaseRetirement.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Disposal_from_a_service_publication_callback_finishes_with_a_hidden_service()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);
        VersionControlCoordinator? coordinator = null;

        try
        {
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
        }
    }

    [AvaloniaTest]
    public async Task Throwing_service_publication_subscriber_does_not_stop_later_state_publication()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);
        VersionControlCoordinator? coordinator = null;

        try
        {
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
        }
    }

    [AvaloniaTest]
    public async Task Branch_creation_keeps_the_open_project_and_its_unsaved_edits()
    {
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;

        try
        {
            // Inside the try, so the shell resets before auto-save is back on.
            await using RealGitScope git = await RealGitScope.StartAsync(
                autoCommitOnSave: false,
                autoCommitOnClose: false,
                useLfsWhenAvailable: false);
            EditViewModel.IsAutoSaveSuppressedForTesting = true;

            (Project project, EditViewModel editor) = await CreateTrackedProjectAsync(
                "version-control-branch-in-memory-edit");
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            await AddRectangleAsync(adder, layer: 0);
            HeadlessTestHelpers.Settle();
            int confirmations = 0;
            TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            Assert.That(
                await TestShell.VersionControl.CreateBranchAsync("kept-open"),
                Is.True);
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
                // Like git switch -c, creating a branch only moves HEAD. Nothing closes, so the
                // unsaved scene edit and its undo history stay in the open editor.
                Assert.That(status.Branch, Is.EqualTo("kept-open"));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(
                    project.Items.OfType<Scene>().Single().Children,
                    Has.Count.EqualTo(1));
                Assert.That(editor.HistoryManager.CanUndo, Is.True);
                Assert.That(confirmations, Is.Zero);
                Assert.That(
                    history.Any(commit => commit.Kind == SnapshotKind.Safety),
                    Is.False);
            });
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
        }
    }

    [AvaloniaTest]
    public async Task Restore_saves_in_memory_project_edits_into_the_safety_snapshot()
    {
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;

        try
        {
            // Inside the try, so the shell resets before auto-save is back on.
            await using RealGitScope git = await RealGitScope.StartAsync(
                autoCommitOnSave: true,
                autoCommitOnClose: false,
                useLfsWhenAvailable: false);
            EditViewModel.IsAutoSaveSuppressedForTesting = true;

            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-restore-in-memory-save");
            IProjectVersionControlService service = TestShell.VersionControl.CurrentService!;
            project.Variables[RestoreStateKey] = "version-one";
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            HeadlessTestHelpers.Settle();
            CommitInfo target = (await service.GetHistoryAsync(0, 10, CancellationToken.None))
                .First(commit => commit.Kind == SnapshotKind.Save);

            // Only mutate the in-memory project — nothing reaches the worktree until a save runs.
            project.Variables[RestoreStateKey] = "unsaved-state";
            WorkspaceStatus beforeRestore = await service.GetStatusAsync(CancellationToken.None);
            Assert.That(
                beforeRestore.IsClean,
                Is.True,
                string.Join(
                    ", ",
                    beforeRestore.Changes.Select(change => $"{change.Status}:{change.Path}")));

            TestShell.VersionControl.ConfirmRestoreAsync = _ => Task.FromResult(true);
            Assert.That(await TestShell.VersionControl.RestoreAsync(target.Sha), Is.True);
            HeadlessTestHelpers.Settle();

            service = TestShell.VersionControl.CurrentService!;
            IReadOnlyList<CommitInfo> history =
                await service.GetHistoryAsync(0, 20, CancellationToken.None);
            CommitInfo safety = history.Single(commit =>
                commit.Kind == SnapshotKind.Safety
                && commit.Subject == "beutl: safety snapshot before restore");

            Assert.That(await TestShell.VersionControl.RestoreAsync(safety.Sha), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.That(
                TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                Is.EqualTo("unsaved-state"),
                "The safety snapshot must contain the edit that only existed in memory.");
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
        }
    }

    [AvaloniaTest]
    public async Task Branch_creation_neither_saves_nor_snapshots_open_work()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-branch-open-work");
        // The backend reports a pending change, which a branch switch would snapshot first.
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        var failedCommands = new SaveOperation(false);
        session.EditorService.TabItems.Add(new EditorTabItem(
            new SavableEditorContext(session.Project, failedCommands)));
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            _ => backend);
        int confirmations = 0;
        coordinator.ConfirmSwitchBranchAsync = (_, _) =>
        {
            confirmations++;
            return Task.FromResult(true);
        };
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        bool created = await coordinator.CreateBranchAsync("open-work");
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(confirmations, Is.Zero);
            Assert.That(failedCommands.SaveCalls, Is.Zero);
            Assert.That(backend.CommitAllCalls, Is.Zero);
            Assert.That(backend.CreateBranchCalls, Is.EqualTo(1));
            Assert.That(backend.LastBranchStartPoint, Is.EqualTo(MainTip.Commit));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
    }

    [AvaloniaTest]
    [TestCase("restore")]
    [TestCase("switch")]
    [TestCase("pull")]
    public async Task Exclusive_operation_started_on_the_thread_pool_suspends_editors_and_reopens_on_the_UI_thread(
        string operation)
    {
        Action<Project?>? recordTransition = null;
        await using var session = await FakeVersionControlSession.CreateAsync(
            $"version-control-thread-pool-{operation}");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                RunsExclusiveOperationsOnThreadPool = true,
                Branches =
                [
                    new BranchInfo("main", true, null),
                    new BranchInfo("other", false, null),
                ],
                PullResult = new RemoteOpResult.Success(),
            };
            var context = new SavableEditorContext(session.Project, new SaveOperation(true));
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            var editorChanges = new List<(bool Enabled, bool OnUiThread)>();
            using IDisposable editorSubscription = context.IsEnabled
                .Skip(1)
                .Subscribe(enabled => editorChanges.Add(
                    (enabled, Avalonia.Threading.Dispatcher.UIThread.CheckAccess())));
            var transitionsOnUiThread = new List<bool>();
            recordTransition = _ => transitionsOnUiThread.Add(
                Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            TestShell.Project.TransitionCommitted += recordTransition;
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = false },
                _ => backend);
            coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
            coordinator.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);
            coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);

            bool succeeded = operation switch
            {
                "restore" => await coordinator.RestoreAsync(
                    "2222222222222222222222222222222222222222"),
                "switch" => await coordinator.SwitchBranchAsync("other"),
                _ => await coordinator.PullAsync() is RemoteOpResult.Success,
            };
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(succeeded, Is.True);
                Assert.That(
                    editorChanges,
                    Is.EqualTo(new[] { (false, true), (true, true) }),
                    "The editors are suspended and given back on the UI thread.");
                Assert.That(
                    transitionsOnUiThread,
                    Is.EqualTo(new[] { true, true }),
                    "The project is closed and reopened on the UI thread.");
            });
        }
        finally
        {
            if (recordTransition is not null)
            {
                TestShell.Project.TransitionCommitted -= recordTransition;
            }
        }
    }

    [AvaloniaTest]
    public async Task Title_bar_branch_creation_warns_why_a_rejected_name_created_nothing()
    {
        TitleBarBranchViewModel? titleBar = null;
        using var gitAvailable = new ReactivePropertySlim<bool>(true);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-branch-name-rejected");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig(),
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            titleBar = new TitleBarBranchViewModel(
                session.EditorService.ProjectVersionControlService,
                gitAvailable,
                coordinator);
            await WaitUntilAsync(() => titleBar.IsVisible.Value);
            titleBar.RequestNewBranchNameAsync = () => Task.FromResult<string?>("existing");
            // The backend refuses an invalid name and an existing branch with the same answer.
            backend.EnqueueCanCreateBranchResult(false);
            int canCreateCallsBefore = backend.CanCreateBranchCalls;
            using var notifications = new NotificationCapture();

            await titleBar.CreateBranchAsync();
            HeadlessTestHelpers.Settle();

            Notification warning = notifications.All.Single();
            Assert.Multiple(() =>
            {
                Assert.That(backend.CanCreateBranchCalls, Is.EqualTo(canCreateCallsBefore + 1));
                Assert.That(backend.CreateBranchCalls, Is.Zero);
                Assert.That(warning.Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(warning.Title, Is.EqualTo(Strings.VersionControl));
                Assert.That(
                    warning.Message,
                    Is.EqualTo(string.Format(
                        Strings.VersionControl_BranchNameUnavailableFormat,
                        "existing")),
                    "Clicking Create must not end silently when the name is refused.");
                Assert.That(titleBar.IsBusy.Value, Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
            });
        }
        finally
        {
            titleBar?.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Restore_prefetches_lfs_objects_while_the_project_is_still_open()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-lfs-prefetch");
        const string TargetSha = "2222222222222222222222222222222222222222";
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            Status = new WorkspaceStatus(
                "main",
                Ahead: 0,
                Behind: 0,
                Changes: [],
                HasConflicts: false),
        };
        bool? projectWasOpenDuringPrefetch = null;
        backend.PrefetchCommitLfsObserver = () =>
            projectWasOpenDuringPrefetch = TestShell.Project.CurrentProject.Value is not null;
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            _ => backend);
        coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        await coordinator.RestoreAsync(TargetSha);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(backend.PrefetchCommitLfsCalls, Is.EqualTo(1));
            Assert.That(
                backend.LastPrefetchCommitLfsScope,
                Is.EqualTo(LfsPrefetchScope.ProjectPathspec));
            // The restore checkout runs uncancellable with the project closed, so the download
            // has to have happened before the close.
            Assert.That(projectWasOpenDuringPrefetch, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Branch_switch_prefetches_lfs_objects_while_the_project_is_still_open()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-branch-lfs-prefetch");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            Status = new WorkspaceStatus(
                "main",
                Ahead: 0,
                Behind: 0,
                Changes: [],
                HasConflicts: false),
            Branches =
            [
                new BranchInfo("main", true, null),
                new BranchInfo("feature", false, null),
            ],
        };
        bool? projectWasOpenDuringPrefetch = null;
        backend.PrefetchBranchLfsObserver = () =>
            projectWasOpenDuringPrefetch = TestShell.Project.CurrentProject.Value is not null;
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            _ => backend);
        coordinator.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        await coordinator.SwitchBranchAsync("feature");
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(backend.PrefetchBranchLfsCalls, Is.EqualTo(1));
            // The switch itself runs uncancellable with the project closed, so the download has
            // to have happened before the close.
            Assert.That(projectWasOpenDuringPrefetch, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Restore_stops_when_an_open_editor_cannot_save()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-save-failure");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        var failedCommands = new SaveOperation(false);
        session.EditorService.TabItems.Add(new EditorTabItem(
            new SavableEditorContext(session.Project, failedCommands)));
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            _ => backend);
        coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        bool restored = await coordinator.RestoreAsync(
            "2222222222222222222222222222222222222222");
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(failedCommands.SaveCalls, Is.EqualTo(1));
            Assert.That(backend.CommitAllCalls, Is.Zero);
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
    }

    [AvaloniaTest]
    public async Task Restore_stops_without_throwing_when_the_repository_has_no_commit_identity()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-no-identity");
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            CommitAllResult = new CommitResult.SkippedNoIdentity(),
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            _ => backend);
        coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        bool restored = await coordinator.RestoreAsync(
            "2222222222222222222222222222222222222222");
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(backend.CommitAllCalls, Is.EqualTo(1));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
    }

    [AvaloniaTest]
    public async Task Branch_cycle_saves_dirty_state_reopens_the_selected_branch_and_recovers_from_failure()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync("version-control-branch");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        await RunGitAsync(git.GitPath, projectRoot, "branch", "experiment");
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
            await TestShell.VersionControl.SwitchBranchAsync("experiment"),
            Is.True);
        HeadlessTestHelpers.Settle();

        Project experimentProject = TestShell.Project.CurrentProject.Value!;
        WorkspaceStatus experimentStatus =
            await TestShell.VersionControl.CurrentService!.GetStatusAsync(
                CancellationToken.None);
        string mainSubject = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "log",
            "-1",
            "--format=%s",
            "refs/heads/main")).Trim();
        Assert.Multiple(() =>
        {
            Assert.That(experimentProject, Is.Not.SameAs(project));
            Assert.That(experimentStatus.Branch, Is.EqualTo("experiment"));
            Assert.That(mainSubject, Is.EqualTo("beutl: safety snapshot before switch"));
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

    [AvaloniaTest]
    public async Task Branch_creation_rejects_invalid_existing_and_shorthand_names_before_confirmation_or_close()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, EditViewModel editor) = await CreateTrackedProjectAsync(
            "version-control-create-branch-validation");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        await RunGitAsync(git.GitPath, projectRoot, "branch", "existing");
        await RunGitAsync(git.GitPath, projectRoot, "switch", "existing");
        await RunGitAsync(git.GitPath, projectRoot, "switch", "main");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder, layer: 0);
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

        using var notifications = new NotificationCapture();
        bool invalid = await TestShell.VersionControl.CreateBranchAsync("invalid..branch");
        bool existing = await TestShell.VersionControl.CreateBranchAsync("existing");
        bool shorthand = await TestShell.VersionControl.CreateBranchAsync("@{-1}");
        HeadlessTestHelpers.Settle();
        string[] warnings = notifications.All
            .Where(item => item.Type == NotificationType.Warning)
            .Select(item => item.Message)
            .ToArray();

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
            Assert.That(
                warnings,
                Is.EqualTo(new[] { "invalid..branch", "existing", "@{-1}" }
                    .Select(name => string.Format(
                        Strings.VersionControl_BranchNameUnavailableFormat,
                        name))),
                "Each refused name must tell the user why no branch was created.");
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

    [AvaloniaTest]
    public async Task Branch_switch_rejects_Git_previous_branch_shorthand_before_confirmation_or_close()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-branch-shorthand");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        await RunGitAsync(git.GitPath, projectRoot, "branch", "alternate");
        await RunGitAsync(git.GitPath, projectRoot, "switch", "alternate");
        await RunGitAsync(git.GitPath, projectRoot, "switch", "main");
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

    [AvaloniaTest]
    public async Task Branch_switch_revalidates_the_local_branch_before_closing_the_project()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-branch-revalidation");
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
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
        var config = new VersionControlConfig();
        VersionControlCoordinator coordinator = session.Start(
            config,
            candidate => candidate is null ? discovery : backend);
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
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
    }

    [AvaloniaTest]
    public async Task Branch_switch_checks_out_a_branch_that_exists_only_on_origin()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-origin-only-branch");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        await CreateOriginAsync(git.GitPath, projectRoot, "version-control-origin-only-branch");
        // Only origin has the branch, as after a teammate pushed it and this clone fetched.
        await RunGitAsync(git.GitPath, projectRoot, "push", "origin", "HEAD:refs/heads/teammate");
        await RunGitAsync(git.GitPath, projectRoot, "fetch", "origin");
        int confirmations = 0;
        TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) =>
        {
            confirmations++;
            return Task.FromResult(true);
        };

        Assert.That(
            await TestShell.VersionControl.SwitchBranchAsync("teammate"),
            Is.True);
        HeadlessTestHelpers.Settle();

        WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
            .GetStatusAsync(CancellationToken.None);
        string upstream = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "for-each-ref",
            "--format=%(upstream:short)",
            "refs/heads/teammate")).Trim();
        Assert.Multiple(() =>
        {
            Assert.That(status.Branch, Is.EqualTo("teammate"));
            Assert.That(upstream, Is.EqualTo("origin/teammate"));
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
        });
    }

    [AvaloniaTest]
    public async Task Branch_cycle_publishes_hidden_and_reopened_service_with_matching_tracked_state()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync("version-control-publication");
        await RunGitAsync(
            git.GitPath,
            Path.GetDirectoryName(project.Uri!.LocalPath)!,
            "branch",
            "publication-branch");
        IProjectVersionControlService original = TestShell.VersionControl.CurrentService!;
        var publications = new List<(IProjectVersionControlService? Service, bool IsTracked)>();
        using IDisposable subscription = TestShell.Editor.ProjectVersionControlService.Subscribe(
            service => publications.Add((service, TestShell.VersionControl.IsTracked.Value)));
        TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);

        Assert.That(
            await TestShell.VersionControl.SwitchBranchAsync("publication-branch"),
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

    [AvaloniaTest]
    public async Task External_close_waits_for_branch_cycle_then_retires_the_reopened_service()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);
        var confirmationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            (Project project, _) = await CreateTrackedProjectAsync(
                "version-control-close-during-branch");
            await RunGitAsync(
                git.GitPath,
                Path.GetDirectoryName(project.Uri!.LocalPath)!,
                "branch",
                "close-race");
            IProjectVersionControlService staleService =
                TestShell.VersionControl.CurrentService!;
            TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) =>
            {
                confirmationEntered.TrySetResult();
                return releaseConfirmation.Task;
            };

            Task<bool> branch = TestShell.VersionControl.SwitchBranchAsync("close-race");
            await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task close = TestShell.Project.CloseProjectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            });

            releaseConfirmation.TrySetResult(true);
            Assert.That(await branch.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(TestShell.VersionControl.CurrentService, Is.Null);
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await staleService.GetStatusAsync(CancellationToken.None));
            });
        }
        finally
        {
            releaseConfirmation.TrySetResult(false);
        }
    }

    [AvaloniaTest]
    public async Task Pull_cycle_preserves_dirty_state_while_fast_forwarding_a_remote_ahead_branch()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync("version-control-pull");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string remoteCommit = await CreatePeerAheadOfOriginAsync(
            git.GitPath,
            projectRoot,
            "version-control-pull",
            "remote-marker.txt",
            "remote state\n",
            "remote update");

        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "local-marker.txt"),
            "local safety state\n");
        project.Variables["unsavedPullState"] = "persisted before pull";
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

        string head = (await RunGitAsync(git.GitPath, projectRoot, "rev-parse", "HEAD")).Trim();
        string stashes = await RunGitAsync(git.GitPath, projectRoot, "stash", "list");
        WorkspaceStatus status = await TestShell.VersionControl.CurrentService!.GetStatusAsync(
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.SameAs(beforePull));
            // The branch moves to exactly the fetched commit, and the local changes come back
            // from the stash as local changes.
            Assert.That(head, Is.EqualTo(remoteCommit));
            Assert.That(stashes, Is.Empty);
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, "remote-marker.txt")),
                Is.EqualTo("remote state\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, "local-marker.txt")),
                Is.EqualTo("local safety state\n"));
            Assert.That(
                TestShell.Project.CurrentProject.Value!.Variables["unsavedPullState"],
                Is.EqualTo("persisted before pull"));
            Assert.That(
                status.Changes.Select(static change => change.Path),
                Does.Contain("local-marker.txt"));
        });
    }

    [AvaloniaTest]
    public async Task Pull_reopen_failure_reports_the_error_and_keeps_the_pulled_files()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-pull-recovery");
        project.Variables[RestoreStateKey] = "valid-local-state";
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;

        string remoteCommit = await CreatePeerAheadOfOriginAsync(
            git.GitPath,
            projectRoot,
            "version-control-pull-recovery",
            Path.GetFileName(projectFile),
            "{ invalid remote project json",
            "invalid remote project");

        string localMarker = Path.Combine(projectRoot, "local-recovery-marker.txt");
        await File.WriteAllTextAsync(localMarker, "keep local state\n");
        TestShell.VersionControl.ConfirmPullAsync = _ => Task.FromResult(true);

        RemoteOpResult result = await TestShell.VersionControl.PullAsync();
        HeadlessTestHelpers.Settle();

        string head = (await RunGitAsync(git.GitPath, projectRoot, "rev-parse", "HEAD")).Trim();
        string stashes = await RunGitAsync(git.GitPath, projectRoot, "stash", "list");
        Assert.Multiple(() =>
        {
            // Like git pull, the fast-forward stands; the project the pull brought cannot be
            // opened, and that is what is reported.
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(head, Is.EqualTo(remoteCommit));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(File.ReadAllText(localMarker), Is.EqualTo("keep local state\n"));
            Assert.That(stashes, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task Pull_up_to_date_clean_project_preserves_the_open_editor_session()
    {
        Func<string, Task>? openingObserver = null;
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-up-to-date");

        try
        {
            var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                Status = new WorkspaceStatus(
                    "main",
                    Ahead: 0,
                    Behind: 0,
                    Changes: [],
                    HasConflicts: false),
                PreflightResult = new PullPreflightResult(
                    new RemoteOpResult.Success(),
                    RequiresTransition: false,
                    UpstreamCommit: null),
                PullResult = new RemoteOpResult.Success(),
            };

            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnSave = false, AutoCommitOnClose = false },
                candidate => candidate is null ? discovery : backend);
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
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
                Assert.That(backend.PullCalls, Is.Zero);
                Assert.That(reopenAttempts, Is.Zero);
            });
        }
        finally
        {
            if (openingObserver is not null)
            {
                TestShell.Project.Opening -= openingObserver;
            }
        }
    }

    [AvaloniaTest]
    public async Task Normal_close_completes_while_pull_confirmation_is_pending()
    {
        var confirmation = new BlockingPrompt<bool>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-close-lock-order");

        try
        {
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = false },
                candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            coordinator.ConfirmPullAsync = confirmation.AnswerAsync;

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await confirmation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.CloseProjectAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await confirmation.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
            confirmation.Release(false);
        }
    }

    [AvaloniaTest]
    public async Task Project_close_during_pull_preflight_cancels_the_captured_epoch()
    {
        var preflightStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreflight = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-preflight-epoch");

        try
        {
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                PreflightStarted = preflightStarted,
                PreflightRelease = releasePreflight.Task,
            };
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = false },
                candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            int confirmations = 0;
            coordinator.ConfirmPullAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await preflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestShell.Project.CloseProjectAsync().WaitAsync(TimeSpan.FromSeconds(5));
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
        }
    }

    [AvaloniaTest]
    public async Task Branch_transition_cancels_a_pending_pull_confirmation()
    {
        var confirmation = new BlockingPrompt<bool>();
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-branch-epoch");

        try
        {
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = false },
                candidate => candidate is null ? discovery : tracked);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));
            coordinator.ConfirmPullAsync = confirmation.AnswerAsync;
            coordinator.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);

            Task<RemoteOpResult> pull = coordinator.PullAsync();
            await confirmation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bool branchCreated = await coordinator.CreateBranchAsync(
                    "cancel-pull-confirmation",
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await confirmation.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
            confirmation.Release(false);
        }
    }

    [AvaloniaTest]
    public async Task Explicit_open_asks_for_enclosing_repository_consent_once()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-enclosing-consent-once");
        string repositoryRoot = Path.GetDirectoryName(session.Root)!;
        var repository = new RepositoryInfo(repositoryRoot, session.Root);
        var discovery = new PullCycleTestBackend(null, repository, MainTip)
        {
            HasVersionTrackingOptIn = false,
        };
        var tracked = new PullCycleTestBackend(repository, repository, MainTip)
        {
            HasVersionTrackingOptIn = false,
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int confirmations = 0;
        coordinator.ConfirmUseEnclosingRepositoryAsync = (candidate, _) =>
        {
            Assert.That(candidate, Is.EqualTo(repository));
            Interlocked.Increment(ref confirmations);
            return Task.FromResult(true);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

        Assert.Multiple(() =>
        {
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(session.ProjectService.CurrentProject.Value, Is.Not.Null);
            Assert.That(session.ProjectService.CurrentProject.Value!.Uri!.LocalPath,
                Is.EqualTo(session.ProjectFile));
        });
    }

    [AvaloniaTest]
    public async Task New_project_in_a_repository_is_not_offered_tracking_by_discovery()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-untracked");
            Directory.CreateDirectory(location);
            // The location lies inside a repository that has never tracked the project being created.
            var repository = new RepositoryInfo(location, Path.Combine(location, "untracked"));
            var discovery = new PullCycleTestBackend(null, repository, MainTip)
            {
                HasVersionTrackingOptIn = false,
            };
            var tracked = new PullCycleTestBackend(repository, repository, MainTip)
            {
                HasVersionTrackingOptIn = false,
            };
            var projectService = new ProjectService();
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                new VersionControlConfig
                {
                    AutoCommitOnSave = true,
                    AutoCommitOnClose = false,
                },
                installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            int confirmations = 0;
            coordinator.ConfirmUseEnclosingRepositoryAsync = (_, _) =>
            {
                Interlocked.Increment(ref confirmations);
                return Task.FromResult(true);
            };

            // With "Track history" unchecked, the new-project dialog creates the project and stops.
            Project? project = await projectService.CreateProject(
                640,
                480,
                30,
                44100,
                "untracked",
                location);
            Assert.That(project, Is.Not.Null);
            await WaitUntilAsync(() => coordinator.CurrentService is not null);
            await WaitForActivationDecisionAsync(coordinator);

            Assert.Multiple(() =>
            {
                Assert.That(confirmations, Is.Zero);
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(tracked.CommitAllCalls, Is.Zero);
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
    public async Task New_project_in_a_repository_asks_once_when_tracking_was_requested()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-declined");
            Directory.CreateDirectory(location);
            var repository = new RepositoryInfo(location, Path.Combine(location, "declined"));
            var untrackedBackends = new List<PullCycleTestBackend>();
            var tracked = new PullCycleTestBackend(repository, repository, MainTip)
            {
                HasVersionTrackingOptIn = false,
            };
            var projectService = new ProjectService();
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                new VersionControlConfig { AutoCommitOnClose = false },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    if (candidate is not null)
                    {
                        return tracked;
                    }

                    var untracked = new PullCycleTestBackend(null, repository, MainTip)
                    {
                        HasVersionTrackingOptIn = false,
                    };
                    untrackedBackends.Add(untracked);
                    return untracked;
                });
            int confirmations = 0;
            coordinator.ConfirmUseEnclosingRepositoryAsync = (_, _) =>
            {
                Interlocked.Increment(ref confirmations);
                return Task.FromResult(false);
            };

            // With "Track history" checked, the dialog initializes the project before it opens.
            bool? initialized = null;
            Project? project;
            coordinator.RequestIdentityAsync = _ => Task.FromResult<GitIdentity?>(
                new GitIdentity("Beutl Headless Test", "headless@example.invalid"));
            await using (INewProjectVersionControlSetup setup = coordinator.BeginNewProject())
            {
                project = await projectService.CreateProject(
                    640,
                    480,
                    30,
                    44100,
                    "declined",
                    location,
                    async (created, cancellationToken) => initialized = await setup.InitializeAsync(
                        created,
                        cancellationToken));
            }

            Assert.That(project, Is.Not.Null);
            await WaitUntilAsync(() => coordinator.CurrentService is not null);
            await WaitUntilAsync(() => untrackedBackends.FirstOrDefault()?.DisposeCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(initialized, Is.False);
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(coordinator.IsTracked.Value, Is.False);
                Assert.That(untrackedBackends, Has.Count.EqualTo(2));
                // The backend that asked is discarded, and the project opens with a fresh untracked one.
                Assert.That(untrackedBackends[0].InitializeCalls, Is.Zero);
                Assert.That(coordinator.CurrentService, Is.SameAs(untrackedBackends[1]));
                Assert.That(untrackedBackends[1].DisposeCalls, Is.Zero);
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
    public async Task New_project_with_tracking_opens_with_the_backend_that_recorded_its_first_version()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-prepared");
            Directory.CreateDirectory(location);
            var backends = new List<PullCycleTestBackend>();
            var projectService = new ProjectService();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                projectService,
                editorService,
                new VersionControlConfig { AutoCommitOnClose = true },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, discoveredRepository: null, MainTip)
                    {
                        RequireIdentityForInitialization = true,
                    };
                    backends.Add(backend);
                    return backend;
                });
            int identityRequests = 0;
            bool? initialized = null;
            ProjectLifecycleActivity activityWhileInitializing = ProjectLifecycleActivity.None;
            Project? openProjectWhileInitializing = null;
            ProjectLifecycleActivity activityWhenOpened;
            Project? project;
            coordinator.RequestIdentityAsync = _ =>
            {
                identityRequests++;
                return Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid"));
            };
            INewProjectVersionControlSetup setup = coordinator.BeginNewProject();
            try
            {
                Assert.That(
                    editorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.CreatingProject));
                project = await projectService.CreateProject(
                    640,
                    480,
                    30,
                    44100,
                    "prepared",
                    location,
                    async (created, cancellationToken) =>
                    {
                        activityWhileInitializing = editorService.LifecycleActivity.Value;
                        openProjectWhileInitializing = projectService.CurrentProject.Value;
                        initialized = await setup.InitializeAsync(created, cancellationToken);
                    });
                activityWhenOpened = editorService.LifecycleActivity.Value;

                // The project opens already tracked, with the backend that recorded its first version.
                Assert.Multiple(() =>
                {
                    Assert.That(project, Is.Not.Null);
                    Assert.That(initialized, Is.True);
                    Assert.That(openProjectWhileInitializing, Is.Null);
                    Assert.That(backends, Has.Count.EqualTo(1));
                    Assert.That(coordinator.CurrentService, Is.SameAs(backends[0]));
                    Assert.That(coordinator.IsTracked.Value, Is.True);
                });
            }
            finally
            {
                await setup.DisposeAsync();
            }

            PullCycleTestBackend prepared = backends.Single();
            string projectRoot = Path.GetDirectoryName(project!.Uri!.LocalPath)!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    activityWhileInitializing,
                    Is.EqualTo(ProjectLifecycleActivity.CreatingProject));
                Assert.That(activityWhenOpened, Is.EqualTo(ProjectLifecycleActivity.CreatingProject));
                Assert.That(
                    editorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
                Assert.That(identityRequests, Is.EqualTo(1));
                Assert.That(prepared.InitializeCalls, Is.EqualTo(2));
                Assert.That(
                    prepared.InitializationOptions.Select(options => options.TargetRepository),
                    Has.All.EqualTo(new RepositoryInfo(projectRoot, projectRoot)));
                // Initialization already did what opening a discovered repository would repeat.
                Assert.That(prepared.HasCheckedOutCommitCalls, Is.Zero);
                Assert.That(prepared.EnsureHygieneCalls, Is.Zero);
                Assert.That(prepared.RetirementCalls, Is.Zero);
                Assert.That(prepared.DisposeCalls, Is.Zero);
            });

            await projectService.CloseProjectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.Null);
                Assert.That(prepared.RetirementSnapshots, Has.Count.EqualTo(1));
                Assert.That(prepared.RetirementSnapshots[0]?.Kind, Is.EqualTo(SnapshotKind.Close));
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
    [TestCase(true, TestName = "New_project_interrupted_after_its_first_version_opens_tracked")]
    [TestCase(false, TestName = "New_project_interrupted_before_its_first_version_opens_untracked")]
    public async Task New_project_interrupted_after_attaching_its_repository_opens_as_reopening_finds_it(
        bool hasFirstVersion)
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                $"version-control-new-project-interrupted-{hasFirstVersion}");
            Directory.CreateDirectory(location);
            string projectRoot = Path.Combine(location, "interrupted");
            var backends = new List<PullCycleTestBackend>();
            var projectService = new ProjectService();
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                new VersionControlConfig { AutoCommitOnClose = false },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    PullCycleTestBackend backend = backends.Count == 0
                        // Initialization attaches the repository it creates and then fails.
                        ? new PullCycleTestBackend(null, discoveredRepository: null, MainTip)
                        {
                            InitializeFailureAfterAttach = new IOException("simulated initial commit failure"),
                        }
                        : new PullCycleTestBackend(
                            candidate,
                            new RepositoryInfo(projectRoot, projectRoot),
                            MainTip)
                        {
                            HasCheckedOutCommit = hasFirstVersion,
                            HasVersionTrackingOptIn = false,
                        };
                    backends.Add(backend);
                    return backend;
                });
            int prompts = 0;
            coordinator.ConfirmAdoptExistingRepositoryAsync = (_, _) =>
            {
                Interlocked.Increment(ref prompts);
                return Task.FromResult(true);
            };
            coordinator.ConfirmUseEnclosingRepositoryAsync = (_, _) =>
            {
                Interlocked.Increment(ref prompts);
                return Task.FromResult(true);
            };

            Exception? initializationFailure = null;
            Project? project;
            await using (INewProjectVersionControlSetup setup = coordinator.BeginNewProject())
            {
                project = await projectService.CreateProject(
                    640,
                    480,
                    30,
                    44100,
                    "interrupted",
                    location,
                    async (created, cancellationToken) =>
                    {
                        try
                        {
                            await setup.InitializeAsync(created, cancellationToken);
                        }
                        catch (IOException ex)
                        {
                            initializationFailure = ex;
                        }
                    });
            }

            Assert.That(project, Is.Not.Null);
            await WaitUntilAsync(() => backends.Count >= 2
                                       && backends[0].DisposeCalls == 1
                                       && coordinator.CurrentService is not null
                                       && coordinator.IsTracked.Value == hasFirstVersion);
            await WaitForActivationDecisionAsync(coordinator);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(initializationFailure, Is.Not.Null);
                Assert.That(backends[0].DisposeCalls, Is.EqualTo(1));
                Assert.That(backends[1].HasCheckedOutCommitCalls, Is.EqualTo(1));
                Assert.That(prompts, Is.Zero);
                Assert.That(coordinator.IsTracked.Value, Is.EqualTo(hasFirstVersion));
                if (hasFirstVersion)
                {
                    // Tracking resumes on the repository initialization created, as reopening would.
                    Assert.That(backends, Has.Count.EqualTo(3));
                    Assert.That(coordinator.CurrentService, Is.SameAs(backends[2]));
                    Assert.That(backends[2].Repository?.ProjectRoot, Is.EqualTo(projectRoot));
                    Assert.That(backends[2].EnsureHygieneCalls, Is.EqualTo(1));
                }
                else
                {
                    // Without a first version, it stays untracked and initialization is offered again.
                    Assert.That(backends, Has.Count.EqualTo(2));
                    Assert.That(coordinator.CurrentService, Is.SameAs(backends[1]));
                }
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
    public async Task New_project_whose_reserved_path_step_fails_still_opens_with_its_first_version()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-reserved-path-failure");
            Directory.CreateDirectory(location);
            var backends = new List<PullCycleTestBackend>();
            var projectService = new ProjectService();
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                new VersionControlConfig { AutoCommitOnClose = false },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, discoveredRepository: null, MainTip)
                    {
                        TrackedReservedPaths = [".beutl/state.tmp"],
                    };
                    backends.Add(backend);
                    return backend;
                });
            coordinator.ConfirmUntrackReservedPathsAsync = (_, _) =>
                Task.FromException<bool>(new IOException("simulated confirmation failure"));

            Exception? initializationFailure = null;
            Project? project;
            await using (INewProjectVersionControlSetup setup = coordinator.BeginNewProject())
            {
                project = await projectService.CreateProject(
                    640,
                    480,
                    30,
                    44100,
                    "reserved-path-failure",
                    location,
                    async (created, cancellationToken) =>
                    {
                        try
                        {
                            await setup.InitializeAsync(created, cancellationToken);
                        }
                        catch (IOException ex)
                        {
                            initializationFailure = ex;
                        }
                    });
            }

            Assert.Multiple(() =>
            {
                Assert.That(project, Is.Not.Null);
                Assert.That(initializationFailure, Is.Not.Null);
                Assert.That(backends, Has.Count.EqualTo(1));
                Assert.That(coordinator.CurrentService, Is.SameAs(backends[0]));
                Assert.That(coordinator.IsTracked.Value, Is.True);
                Assert.That(backends[0].DisposeCalls, Is.Zero);
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
    public async Task New_project_backend_is_retired_when_the_project_does_not_open()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var projectService = new ProjectService();
        Func<Project, Task> failOpening = static _ =>
            Task.FromException(new IOException("simulated editor failure"));

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-not-opened");
            Directory.CreateDirectory(location);
            var backends = new List<PullCycleTestBackend>();
            var editorService = new EditorService(new ExtensionProvider());
            coordinator = new VersionControlCoordinator(
                projectService,
                editorService,
                new VersionControlConfig { AutoCommitOnClose = true },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, discoveredRepository: null, MainTip);
                    backends.Add(backend);
                    return backend;
                });
            projectService.Opened += failOpening;

            bool? initialized = null;
            Project? project;
            await using (INewProjectVersionControlSetup setup = coordinator.BeginNewProject())
            {
                project = await projectService.CreateProject(
                    640,
                    480,
                    30,
                    44100,
                    "not-opened",
                    location,
                    async (created, cancellationToken) => initialized = await setup.InitializeAsync(
                        created,
                        cancellationToken));
            }

            await WaitUntilAsync(() => backends.FirstOrDefault()?.DisposeCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(project, Is.Null);
                Assert.That(initialized, Is.True);
                Assert.That(projectService.CurrentProject.Value, Is.Null);
                Assert.That(coordinator.CurrentService, Is.Null);
                Assert.That(backends, Has.Count.EqualTo(1));
                Assert.That(backends[0].RetirementSnapshots, Is.EqualTo(new ProjectVersionControlFinalSnapshot?[] { null }));
                Assert.That(
                    editorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
            });
        }
        finally
        {
            projectService.Opened -= failOpening;
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Close_requested_during_new_project_identity_prompt_cancels_it_and_closes_the_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var projectService = new ProjectService();
        var identityRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Project?>? creation = null;
        Task? close = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-close-during-identity");
            Directory.CreateDirectory(location);
            var backends = new List<PullCycleTestBackend>();
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                new VersionControlConfig { AutoCommitOnClose = true },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, discoveredRepository: null, MainTip)
                    {
                        RequireIdentityForInitialization = true,
                    };
                    backends.Add(backend);
                    return backend;
                });
            bool identityCanceled = false;
            Exception? initializationFailure = null;
            coordinator.RequestIdentityAsync = async cancellationToken =>
            {
                identityRequested.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    identityCanceled = true;
                    throw;
                }

                return null;
            };
            await using INewProjectVersionControlSetup setup = coordinator.BeginNewProject();
            creation = projectService.CreateProject(
                640,
                480,
                30,
                44100,
                "close-during-identity",
                location,
                async (created, cancellationToken) =>
                {
                    try
                    {
                        await setup.InitializeAsync(created, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        initializationFailure = ex;
                        throw;
                    }
                });
            await identityRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Closing the window asks for the close while the creation still waits on the user.
            close = projectService.CloseProjectAsync();
            Project? created = await creation.WaitAsync(TimeSpan.FromSeconds(5));
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => backends.FirstOrDefault()?.DisposeCalls == 1);

            Assert.Multiple(() =>
            {
                Assert.That(identityCanceled, Is.True);
                Assert.That(initializationFailure, Is.InstanceOf<OperationCanceledException>());
                Assert.That(created, Is.Not.Null);
                Assert.That(projectService.CurrentProject.Value, Is.Null);
                Assert.That(backends, Has.Count.EqualTo(2));
                Assert.That(backends[0].InitializeCalls, Is.EqualTo(1));
                // The project opened untracked with a fresh backend, which the close retired.
                Assert.That(backends[1].Repository, Is.Null);
                Assert.That(backends[1].InitializeCalls, Is.Zero);
                Assert.That(backends[1].RetirementCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            if (creation is not null)
            {
                await creation.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (close is not null)
            {
                await close.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task New_project_backend_prepared_before_disposal_is_retired_with_the_coordinator()
    {
        await TestReset.ResetShellAsync();
        VersionControlCoordinator? coordinator = null;
        var projectService = new ProjectService();
        INewProjectVersionControlSetup? setup = null;
        var releaseOpening = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Project?>? creation = null;

        try
        {
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-disposed");
            Directory.CreateDirectory(location);
            var backends = new List<PullCycleTestBackend>();
            coordinator = new VersionControlCoordinator(
                projectService,
                new EditorService(new ExtensionProvider()),
                new VersionControlConfig { AutoCommitOnClose = false },
                installationLocator: null,
                serviceFactory: candidate =>
                {
                    var backend = new PullCycleTestBackend(candidate, discoveredRepository: null, MainTip);
                    backends.Add(backend);
                    return backend;
                });
            setup = coordinator.BeginNewProject();
            var initialized = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            INewProjectVersionControlSetup activeSetup = setup;
            creation = projectService.CreateProject(
                640,
                480,
                30,
                44100,
                "disposed",
                location,
                async (created, cancellationToken) =>
                {
                    initialized.TrySetResult(
                        await activeSetup.InitializeAsync(created, cancellationToken));
                    await releaseOpening.Task;
                });
            Assert.That(await initialized.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);

            // The app shuts down before the project opens: the repository the creation prepared is
            // released with the coordinator rather than left with a live watcher.
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(backends, Has.Count.EqualTo(1));
                Assert.That(backends[0].RetirementCalls, Is.EqualTo(1));
                Assert.That(backends[0].DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseOpening.TrySetResult();
            if (creation is not null)
            {
                await creation.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (setup is not null)
            {
                await setup.DisposeAsync();
            }

            if (coordinator is not null)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_a_tracked_project_shows_the_close_in_place_of_the_editor_until_it_completes()
    {
        var retirementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? close = null;
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-presentation");

        try
        {
            var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                RetirementStarted = retirementStarted,
                RetirementRelease = releaseRetirement.Task,
            };
            var commands = new SaveOperation(true);
            var context = new SavableEditorContext(session.Project, commands);
            session.EditorService.TabItems.Add(new EditorTabItem(context));
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnClose = true },
                _ => backend);
            await WaitUntilAsync(() =>
                ReferenceEquals(coordinator.CurrentService, backend)
                && coordinator.IsTracked.Value);
            ProjectLifecycleActivity activityWhileSaving = ProjectLifecycleActivity.None;
            bool editorEnabledWhileSaving = true;
            commands.Saving = () =>
            {
                activityWhileSaving = session.EditorService.LifecycleActivity.Value;
                editorEnabledWhileSaving = context.IsEnabled.Value;
            };
            Assert.That(
                session.EditorService.LifecycleActivity.Value,
                Is.EqualTo(ProjectLifecycleActivity.None));

            close = TestShell.Project.TryCloseProjectAsync(
                session.Project,
                ProjectService.ProjectCloseIntent.SaveChanges);
            await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(commands.SaveCalls, Is.EqualTo(1));
                Assert.That(activityWhileSaving, Is.EqualTo(ProjectLifecycleActivity.ClosingProject));
                Assert.That(editorEnabledWhileSaving, Is.False);
                Assert.That(
                    session.EditorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.ClosingProject));
                Assert.That(close.IsCompleted, Is.False);
            });

            releaseRetirement.TrySetResult();
            Assert.That(await close.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(
                    session.EditorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
            });
        }
        finally
        {
            releaseRetirement.TrySetResult();
            if (close is not null)
            {
                await close.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [AvaloniaTest]
    public async Task Closing_while_version_control_work_runs_shows_the_close_until_that_work_ends()
    {
        var identityRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // Ignores cancellation, so the close has to wait for the operation to finish.
        var identity = new TaskCompletionSource<GitIdentity?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? initialization = null;
        Task<bool>? close = null;
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-waits-for-operation");

        try
        {
            var backend = new PullCycleTestBackend(null, discoveredRepository: null, MainTip)
            {
                RequireIdentityForInitialization = true,
            };
            session.EditorService.TabItems.Add(new EditorTabItem(
                new SavableEditorContext(session.Project, new SaveOperation(true))));
            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig
                {
                    AutoCommitOnSave = true,
                    AutoCommitOnClose = true,
                },
                _ => backend);
            await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
            await WaitForActivationDecisionAsync(coordinator);
            HeadlessTestHelpers.Settle();
            Assert.That(
                session.EditorService.LifecycleActivity.Value,
                Is.EqualTo(ProjectLifecycleActivity.None));

            coordinator.RequestIdentityAsync = _ =>
            {
                identityRequested.TrySetResult();
                return identity.Task;
            };
            initialization = coordinator.InitializeCurrentProjectAsync(session.Project);
            await identityRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            close = TestShell.Project.TryCloseProjectAsync(
                session.Project,
                ProjectService.ProjectCloseIntent.SaveChanges);
            await WaitUntilAsync(() =>
                session.EditorService.LifecycleActivity.Value == ProjectLifecycleActivity.ClosingProject);
            Assert.That(close.IsCompleted, Is.False);

            identity.TrySetResult(null);
            Assert.That(await initialization.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
            Assert.That(await close.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(
                    session.EditorService.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
            });
        }
        finally
        {
            identity.TrySetResult(null);
            if (initialization is not null)
            {
                await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            }

            if (close is not null)
            {
                await close.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [AvaloniaTest]
    public async Task Closing_an_untracked_project_keeps_the_editor_area_as_is()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-close-untracked-presentation");
        var backend = new PullCycleTestBackend(null, discoveredRepository: null, MainTip);
        var activities = new List<ProjectLifecycleActivity>();
        using IDisposable subscription = session.EditorService.LifecycleActivity.Subscribe(activities.Add);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig
            {
                AutoCommitOnSave = true,
                AutoCommitOnClose = true,
            },
            _ => backend);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        await WaitForActivationDecisionAsync(coordinator);
        HeadlessTestHelpers.Settle();
        Assert.That(coordinator.IsTracked.Value, Is.False);

        Assert.That(
            await TestShell.Project.TryCloseProjectAsync(
                session.Project,
                ProjectService.ProjectCloseIntent.SaveChanges),
            Is.True);
        HeadlessTestHelpers.Settle();

        Assert.That(activities, Is.EqualTo(new[] { ProjectLifecycleActivity.None }));
    }

    [AvaloniaTest]
    public async Task Opening_a_conflicted_repository_keeps_the_project_tracked()
    {
        await AssertBlockedRepositoryStaysTrackedAsync(
            "version-control-open-conflicted",
            new VersionControlConflictedException(Strings.VersionControl_ConflictGuidance));
    }

    [AvaloniaTest]
    public async Task Opening_a_detached_head_keeps_the_project_tracked()
    {
        await AssertBlockedRepositoryStaysTrackedAsync(
            "version-control-open-detached",
            new DetachedHeadNotSupportedException());
    }

    [AvaloniaTest]
    public async Task Adopted_enclosing_repository_resumes_tracking_without_asking_again()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-enclosing-consent-opt-in");
        string repositoryRoot = Path.GetDirectoryName(session.Root)!;
        var repository = new RepositoryInfo(repositoryRoot, session.Root);
        // The repository already records an earlier opt-in for this project.
        var discovery = new PullCycleTestBackend(null, repository, MainTip)
        {
            HasVersionTrackingOptIn = true,
        };
        var tracked = new PullCycleTestBackend(repository, repository, MainTip)
        {
            HasVersionTrackingOptIn = true,
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int confirmations = 0;
        coordinator.ConfirmUseEnclosingRepositoryAsync = (_, _) =>
        {
            Interlocked.Increment(ref confirmations);
            return Task.FromResult(true);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

        Assert.Multiple(() =>
        {
            // Re-asking would turn a dismissed prompt into a session with no snapshots at all,
            // even though the project is already tracked.
            Assert.That(confirmations, Is.Zero);
            Assert.That(session.ProjectService.CurrentProject.Value, Is.Not.Null);
        });
    }

    [AvaloniaTest]
    public async Task Pull_prefetches_lfs_objects_at_the_fetched_upstream_commit()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-lfs-prefetch");
        const string UpstreamCommit = "2222222222222222222222222222222222222222";
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            PreflightResult = new PullPreflightResult(
                new RemoteOpResult.Success(),
                RequiresTransition: true,
                UpstreamCommit),
            PullResult = new RemoteOpResult.Success(),
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnSave = false, AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : backend);
        coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        bool? projectWasOpenDuringPrefetch = null;
        backend.PrefetchCommitLfsObserver = () =>
            projectWasOpenDuringPrefetch = TestShell.Project.CurrentProject.Value is not null;

        await coordinator.PullAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            // The fetch moved the remote-tracking ref only, so the local branch still points at
            // the pre-pull tip: prefetching by branch name would download nothing.
            Assert.That(backend.PrefetchBranchLfsCalls, Is.Zero);
            Assert.That(backend.LastPrefetchedCommit, Is.EqualTo(UpstreamCommit));
            Assert.That(
                backend.LastPrefetchCommitLfsScope,
                Is.EqualTo(LfsPrefetchScope.RepositoryWide));
            // The merge itself runs uncancellable with the project closed.
            Assert.That(projectWasOpenDuringPrefetch, Is.True);
            Assert.That(backend.LastPulledCommit, Is.EqualTo(UpstreamCommit));
        });
    }

    [AvaloniaTest]
    public async Task Pull_reports_a_missing_git_lfs_instead_of_a_changed_project()
    {
        const string LfsMissing = "Git LFS is not installed. Install Git LFS to pull this project.";
        (RemoteOpResult? result, Exception? thrown, bool projectStayedOpen, _, int pullCalls) =
            await RunPullThatFailsBeforeClosingAsync(
                "version-control-pull-lfs-missing",
                backend => backend.PrefetchCommitLfsObserver =
                    () => throw new InvalidOperationException(LfsMissing));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Null);
            Assert.That(result, Is.EqualTo(new RemoteOpResult.Failed(LfsMissing)));
            Assert.That(projectStayedOpen, Is.True);
            Assert.That(pullCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Pull_reports_why_the_preflight_failed_before_confirmation()
    {
        const string PreflightFailure = "The upstream branch could not be fetched.";
        (RemoteOpResult? result, Exception? thrown, bool projectStayedOpen, int confirmations, int pullCalls) =
            await RunPullThatFailsBeforeClosingAsync(
                "version-control-pull-preflight-failure",
                backend => backend.PreflightFailure = new InvalidOperationException(PreflightFailure));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Null);
            Assert.That(result, Is.EqualTo(new RemoteOpResult.Failed(PreflightFailure)));
            Assert.That(confirmations, Is.Zero);
            Assert.That(projectStayedOpen, Is.True);
            Assert.That(pullCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Pull_reports_a_changed_project_when_it_closes_during_confirmation()
    {
        (RemoteOpResult? result, Exception? thrown, bool projectStayedOpen, int confirmations, int pullCalls) =
            await RunPullThatFailsBeforeClosingAsync(
                "version-control-pull-closed-during-confirmation",
                configure: null,
                duringConfirmation: () => TestShell.Project.CloseProjectAsync());

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Null);
            Assert.That(result, Is.EqualTo(new RemoteOpResult.Failed(
                "The open project changed while the pull was being prepared.")));
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(projectStayedOpen, Is.False);
            Assert.That(pullCalls, Is.Zero);
        });
    }

    // The version control tab shows its conflict guidance only for a VersionControlConflictedException, so
    // the pull cycle must not turn a conflict into a plain failure.
    [AvaloniaTest]
    public async Task Pull_passes_through_a_conflict_found_before_confirmation()
    {
        var conflict = new VersionControlConflictedException(Strings.VersionControl_ConflictGuidance);
        (RemoteOpResult? result, Exception? thrown, bool projectStayedOpen, int confirmations, int pullCalls) =
            await RunPullThatFailsBeforeClosingAsync(
                "version-control-pull-preflight-conflict",
                backend => backend.PreflightFailure = conflict);

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(conflict));
            Assert.That(result, Is.Null);
            Assert.That(confirmations, Is.Zero);
            Assert.That(projectStayedOpen, Is.True);
            Assert.That(pullCalls, Is.Zero);
        });
    }

    // The pull checks the repository again once confirmed; a conflict found there passes through as well.
    [AvaloniaTest]
    public async Task Pull_passes_through_a_conflict_found_after_confirmation()
    {
        var conflict = new VersionControlConflictedException(Strings.VersionControl_ConflictGuidance);
        PullCycleTestBackend? pullBackend = null;
        (RemoteOpResult? result, Exception? thrown, bool projectStayedOpen, int confirmations, int pullCalls) =
            await RunPullThatFailsBeforeClosingAsync(
                "version-control-pull-confirmed-conflict",
                backend => pullBackend = backend,
                duringConfirmation: () =>
                {
                    pullBackend!.PreflightFailure = conflict;
                    return Task.CompletedTask;
                });

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(conflict));
            Assert.That(result, Is.Null);
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(projectStayedOpen, Is.True);
            Assert.That(pullCalls, Is.Zero);
        });
    }

    private static async Task<(
            RemoteOpResult? Result,
            Exception? Thrown,
            bool ProjectStayedOpen,
            int Confirmations,
            int PullCalls)>
        RunPullThatFailsBeforeClosingAsync(
            string projectName,
            Action<PullCycleTestBackend>? configure,
            Func<Task>? duringConfirmation = null)
    {
        await using var session = await FakeVersionControlSession.CreateAsync(projectName);
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            PreflightResult = new PullPreflightResult(
                new RemoteOpResult.Success(),
                RequiresTransition: true,
                "2222222222222222222222222222222222222222"),
        };
        configure?.Invoke(backend);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnSave = false, AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : backend);
        int confirmations = 0;
        coordinator.ConfirmPullAsync = async _ =>
        {
            confirmations++;
            if (duringConfirmation is not null)
            {
                await duringConfirmation();
            }

            return true;
        };
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        RemoteOpResult? result = null;
        InvalidOperationException? thrown = null;
        try
        {
            result = await coordinator.PullAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        HeadlessTestHelpers.Settle();

        return (
            result,
            thrown,
            ReferenceEquals(TestShell.Project.CurrentProject.Value, session.Project),
            confirmations,
            backend.PullCalls);
    }

    [AvaloniaTest]
    public async Task Missing_recent_project_keeps_the_current_project_open()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-missing-recent-current");
        string missingProject = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "missing-recent",
            "missing.bep");
        var discovery = new PullCycleTestBackend(
            repository: null,
            discoveredRepository: null,
            MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            _ => discovery);
        var menu = new MenuBarViewModel(TestShell.Project, session.EditorService, coordinator);

        await menu.OpenRecentProject.ExecuteAsync(missingProject);

        Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        Assert.That(Path.GetDirectoryName(session.Project.Uri!.LocalPath), Is.EqualTo(session.Root));
    }

    [AvaloniaTest]
    public async Task Restore_reopen_failure_appends_a_recovery_commit_and_reopens_original_state()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-restore-recovery");
        project.Variables[RestoreStateKey] = "original-state";
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

        string projectFile = project.Uri!.LocalPath;
        string invalidTarget = await CommitInvalidProjectAsync(git.GitPath, project, "invalid restore target");
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

    [AvaloniaTest]
    public async Task Restore_to_new_branch_reopen_failure_retains_the_created_ref_and_warns_after_recovery()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-restore-branch-recovery-cleanup");
        project.Variables[RestoreStateKey] = "original-state";
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        string invalidTarget = await CommitInvalidProjectAsync(git.GitPath, project, "invalid branch restore target");
        TestShell.VersionControl.ConfirmRestoreAsync = _ => Task.FromResult(true);
        using var notifications = new NotificationCapture();

        const string FailedBranch = "failed-restore";
        Assert.That(
            await TestShell.VersionControl.RestoreToNewBranchAsync(
                invalidTarget,
                FailedBranch),
            Is.False);
        HeadlessTestHelpers.Settle();

        string currentBranch = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "branch",
            "--show-current")).Trim();
        string failedBranch = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "branch",
            "--list",
            FailedBranch);
        string retainedBranchMessage = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Strings.VersionControl_RestoreBranchRetainedFormat,
            FailedBranch);
        Assert.Multiple(() =>
        {
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(failedBranch, Does.Contain(FailedBranch));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(
                TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                Is.EqualTo("original-state"));
            Assert.That(
                notifications.All.Count(item =>
                    item.Type == NotificationType.Warning
                    && item.Message == retainedBranchMessage),
                Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task Branch_switch_that_cannot_reopen_the_project_returns_to_the_original_branch()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-switch-reopen-failure");
        project.Variables[RestoreStateKey] = "original-state";
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        string projectPathspec = Path.GetRelativePath(projectRoot, projectFile)
            .Replace('\\', '/');
        string validProject = await File.ReadAllTextAsync(projectFile);
        await RunGitAsync(git.GitPath, projectRoot, "branch", "broken");
        string brokenTree = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "rev-parse",
            "broken^{tree}")).Trim();
        await File.WriteAllTextAsync(projectFile, "{ invalid project json");
        await RunGitAsync(git.GitPath, projectRoot, "add", "--", projectPathspec);
        string invalidTree = (await RunGitAsync(git.GitPath, projectRoot, "write-tree")).Trim();
        string invalidCommit = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "commit-tree",
            invalidTree,
            "-p",
            "broken",
            "-m",
            "invalid project on the branch")).Trim();
        await RunGitAsync(git.GitPath, projectRoot, "update-ref", "refs/heads/broken", invalidCommit);
        await RunGitAsync(git.GitPath, projectRoot, "reset", "-q", "--", projectPathspec);
        await File.WriteAllTextAsync(projectFile, validProject);
        Assert.That(brokenTree, Is.Not.EqualTo(invalidTree));
        TestShell.VersionControl.ConfirmSwitchBranchAsync = (_, _) => Task.FromResult(true);

        Assert.That(
            await TestShell.VersionControl.SwitchBranchAsync("broken"),
            Is.False);
        HeadlessTestHelpers.Settle();

        string currentBranch = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "branch",
            "--show-current")).Trim();
        Assert.Multiple(() =>
        {
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(
                TestShell.Project.CurrentProject.Value!.Variables[RestoreStateKey],
                Is.EqualTo("original-state"));
        });
    }

    [AvaloniaTest]
    public async Task Restore_to_new_branch_validates_revision_before_confirmation_or_close()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-reject-restore-option");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string validSha = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "rev-parse",
            "HEAD")).Trim();
        int confirmationCalls = 0;
        TestShell.VersionControl.ConfirmRestoreAsync = _ =>
        {
            confirmationCalls++;
            return Task.FromResult(true);
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await TestShell.VersionControl.RestoreToNewBranchAsync(
                "--discard-changes",
                "injected-option"));

        string currentBranch = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "branch",
            "--show-current");
        string injectedBranch = await RunGitAsync(
            git.GitPath,
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
            git.GitPath,
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

    [AvaloniaTest]
    public async Task Restore_rejects_revision_without_project_file_before_confirmation_or_close()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        (Project project, _) = await CreateTrackedProjectAsync(
            "version-control-reject-missing-project");
        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        string projectPathspec = Path.GetRelativePath(projectRoot, projectFile)
            .Replace('\\', '/');
        string originalTip = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "rev-parse",
            "HEAD")).Trim();
        await RunGitAsync(git.GitPath, projectRoot, "rm", "--", projectPathspec);
        await RunGitAsync(git.GitPath, projectRoot, "commit", "-m", "remove project file");
        string missingProjectRevision = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "rev-parse",
            "HEAD")).Trim();
        await RunGitAsync(git.GitPath, projectRoot, "reset", "--hard", originalTip);
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
            git.GitPath,
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

    [AvaloniaTest]
    public async Task Restore_to_new_branch_rejects_invalid_existing_and_shorthand_names_before_confirmation_or_close()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, EditViewModel editor) = await CreateTrackedProjectAsync(
            "version-control-restore-branch-validation");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string targetSha = (await RunGitAsync(
            git.GitPath,
            projectRoot,
            "rev-parse",
            "HEAD")).Trim();
        await RunGitAsync(git.GitPath, projectRoot, "branch", "existing");
        await RunGitAsync(git.GitPath, projectRoot, "switch", "existing");
        await RunGitAsync(git.GitPath, projectRoot, "switch", "main");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder, layer: 0);
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

    [AvaloniaTest]
    public async Task Restore_to_new_branch_revalidates_the_name_before_closing_the_project()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-branch-revalidation");
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
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
        var config = new VersionControlConfig();
        VersionControlCoordinator coordinator = session.Start(
            config,
            candidate => candidate is null ? discovery : backend);
        int confirmations = 0;
        coordinator.ConfirmRestoreAsync = _ =>
        {
            confirmations++;
            return Task.FromResult(true);
        };
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        using var notifications = new NotificationCapture();

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
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
            Assert.That(
                notifications.All.Any(item => item.Message == string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.VersionControl_RestoreBranchRetainedFormat,
                    "target")),
                Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Restore_to_new_branch_revalidates_after_the_safety_snapshot_before_closing_the_project()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-branch-post-snapshot-revalidation");
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            Status = new WorkspaceStatus(
                "main",
                Ahead: 0,
                Behind: 0,
                Changes: [],
                HasConflicts: false),
        };
        backend.EnqueueCanCreateBranchResult(true);
        backend.EnqueueCanCreateBranchResult(true);
        backend.EnqueueCanCreateBranchResult(false);
        var config = new VersionControlConfig();
        VersionControlCoordinator coordinator = session.Start(
            config,
            candidate => candidate is null ? discovery : backend);
        int confirmations = 0;
        coordinator.ConfirmRestoreAsync = _ =>
        {
            confirmations++;
            backend.Status = new WorkspaceStatus(
                "main",
                Ahead: 0,
                Behind: 0,
                Changes: [new FileChange("late.scene", FileChangeStatus.Modified)],
                HasConflicts: false);
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
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
    }

    [AvaloniaTest]
    public async Task Restore_reopens_exact_state_preserves_safety_snapshot_and_supports_a_new_branch()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        (Project project, EditViewModel editor) = await CreateTrackedProjectAsync();
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        IProjectVersionControlService service = TestShell.VersionControl.CurrentService!;

        project.Variables[RestoreStateKey] = "version-one";
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        CommitInfo target = (await service.GetHistoryAsync(0, 10, CancellationToken.None))
            .First(commit => commit.Kind == SnapshotKind.Save);

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder, layer: 0);
        project.Variables[RestoreStateKey] = "version-two";
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

        await AddRectangleAsync(adder, layer: 1);
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

        using (IDisposable outputOperation = TestShell.Editor.BeginObservedOutputOperation())
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

    [AvaloniaTest]
    public async Task Pull_cancelled_once_the_project_is_closed_still_finishes_and_reopens_the_project()
    {
        var pullStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePull = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-final-fetch-cancellation");
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            Status = new WorkspaceStatus(
                "main",
                Ahead: 0,
                Behind: 0,
                Changes: [],
                HasConflicts: false),
            PullOverride = async cancellationToken =>
            {
                pullStarted.TrySetResult();
                await releasePull.Task;
                return cancellationToken.CanBeCanceled
                    ? new RemoteOpResult.Failed("The merge was given a cancellable token.")
                    : new RemoteOpResult.Success();
            },
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnSave = false, AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : backend);
        coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        using var cancellation = new CancellationTokenSource();
        Task<RemoteOpResult> pull = coordinator.PullAsync(cancellation.Token);
        await pullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);

        // Git runs uncancellable once the project is closed, so the project always reopens on
        // files Git finished writing.
        cancellation.Cancel();
        releasePull.TrySetResult();
        RemoteOpResult result = await pull.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(backend.PullCalls, Is.EqualTo(1));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(
                TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                Is.EqualTo(session.Project.Uri!.LocalPath));
        });
    }

    [AvaloniaTest]
    public async Task Pull_failure_after_the_close_reopens_the_project_and_reports_the_Git_error()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-pull-merge-failure");
        var gitError = new RemoteOpResult.Failed(
            "error: Your local changes to the following files would be overwritten by merge");
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            PullResult = gitError,
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig
            {
                AutoCommitOnSave = false,
                AutoCommitOnClose = false,
            },
            candidate => candidate is null ? discovery : backend);
        coordinator.ConfirmPullAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));

        RemoteOpResult result = await coordinator.PullAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(gitError));
            Assert.That(backend.PullCalls, Is.EqualTo(1));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.SameAs(session.Project));
            Assert.That(
                TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                Is.EqualTo(session.Project.Uri!.LocalPath));
        });
    }

    [AvaloniaTest]
    public async Task Restore_failure_before_its_commit_reopens_without_a_recovery_commit()
    {
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-restore-before-commit-failure");
        const string Target = "2222222222222222222222222222222222222222";
        var discovery = new PullCycleTestBackend(repository: null, session.Repository, MainTip);
        var backend = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            Status = new WorkspaceStatus(
                "main",
                Ahead: 0,
                Behind: 0,
                Changes: [],
                HasConflicts: false),
            // The service has already put the project back to HEAD when it reports this.
            RestoreProjectTreeFailure = new GitOperationException(1, "pre-commit hook rejected"),
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig
            {
                AutoCommitOnSave = false,
                AutoCommitOnClose = false,
            },
            candidate => candidate is null ? discovery : backend);
        coordinator.ConfirmRestoreAsync = _ => Task.FromResult(true);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, backend));
        using var notifications = new NotificationCapture();

        bool restored = await coordinator.RestoreAsync(Target);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(
                backend.RestoreProjectTreeCalls,
                Is.EqualTo(new[] { (Target, SnapshotKind.Restore) }));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(
                notifications.All.Any(item =>
                    item.Type == NotificationType.Error
                    && item.Message.Contains("pre-commit hook rejected", StringComparison.Ordinal)),
                Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Explicit_saves_wait_for_activation_and_preserve_each_save_point()
    {
        IProjectFileWriteLease? firstWrite = null;
        IProjectFileWriteLease? secondWrite = null;
        var hygieneStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHygiene = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await FakeVersionControlSession.CreateAsync(
            "version-control-save-during-activation");

        try
        {
            string statePath = Path.Combine(session.Root, "save-state.txt");
            var committedStates = new List<string>();
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
            {
                EnsureHygieneStarted = hygieneStarted,
                EnsureHygieneRelease = releaseHygiene.Task,
                CommitAllObserver = _ => committedStates.Add(File.ReadAllText(statePath)),
            };

            VersionControlCoordinator coordinator = session.Start(
                new VersionControlConfig { AutoCommitOnSave = true, AutoCommitOnClose = false },
                candidate => candidate is null ? discovery : tracked);
            await hygieneStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            firstWrite = await session.EditorService.BeginProjectFileWriteAsync(CancellationToken.None);
            await File.WriteAllTextAsync(statePath, "one");
            Task firstSnapshot = coordinator.NotifySavedAsync(firstWrite);
            await WaitUntilAsync(() => session.EditorService.IsWorktreeMutationActive);
            Task<IProjectFileWriteLease> secondWriteTask = session.EditorService
                .BeginProjectFileWriteAsync(CancellationToken.None)
                .AsTask();

            Assert.Multiple(() =>
            {
                Assert.That(firstSnapshot.IsCompleted, Is.False);
                Assert.That(secondWriteTask.IsCompleted, Is.False);
                Assert.That(tracked.CommitAllCalls, Is.Zero);
            });

            releaseHygiene.TrySetResult();
            await firstSnapshot.WaitAsync(TimeSpan.FromSeconds(5));
            secondWrite = await secondWriteTask.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllTextAsync(statePath, "two");
            await coordinator.NotifySavedAsync(secondWrite).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(tracked.CommitAllCalls, Is.EqualTo(2));
                Assert.That(
                    tracked.CommitKinds,
                    Is.EqualTo(new[] { SnapshotKind.Save, SnapshotKind.Save }));
                Assert.That(committedStates, Is.EqualTo(new[] { "one", "two" }));
            });
        }
        finally
        {
            releaseHygiene.TrySetResult();
            firstWrite?.Dispose();
            secondWrite?.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Opening_a_project_Git_cannot_inspect_opens_it_without_version_control()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-discovery-refused");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip)
        {
            DiscoverRepositoryOverride = _ => throw new GitOperationException(
                128,
                "fatal: detected dubious ownership in repository at '/shared/project'"),
        };
        var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        using var notifications = new NotificationCapture();

        await session.ProjectService.OpenProject(session.ProjectFile);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(session.ProjectService.CurrentProject.Value, Is.Not.Null);
            Assert.That(coordinator.IsTracked.Value, Is.False);
            Assert.That(
                notifications.All.Any(static item =>
                    item.Type == NotificationType.Warning
                    && item.Message.Contains("dubious ownership", StringComparison.Ordinal)),
                Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Opening_a_project_in_an_unmanaged_root_repository_asks_before_tracking()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-unmanaged-root-repository");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip)
        {
            HasVersionTrackingOptIn = false,
        };
        var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int adoptionPrompts = 0;
        coordinator.ConfirmAdoptExistingRepositoryAsync = (candidate, _) =>
        {
            Assert.That(candidate, Is.EqualTo(session.Repository));
            Interlocked.Increment(ref adoptionPrompts);
            return Task.FromResult(false);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => adoptionPrompts == 1);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(adoptionPrompts, Is.EqualTo(1));
            Assert.That(coordinator.IsTracked.Value, Is.False);
            Assert.That(coordinator.CurrentService, Is.Not.SameAs(tracked));
            Assert.That(tracked.EnsureHygieneCalls, Is.Zero);
            Assert.That(tracked.CommitAllCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Opening_a_project_in_a_root_repository_without_commits_leaves_it_for_initialization()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-unborn-root-repository");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip)
        {
            HasVersionTrackingOptIn = false,
            HasCheckedOutCommit = false,
        };
        var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int adoptionPrompts = 0;
        coordinator.ConfirmAdoptExistingRepositoryAsync = (_, _) =>
        {
            Interlocked.Increment(ref adoptionPrompts);
            return Task.FromResult(true);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => adoptionPrompts > 0 || discovery.HasCheckedOutCommitCalls > 0);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(discovery.HasCheckedOutCommitCalls, Is.EqualTo(1));
            Assert.That(adoptionPrompts, Is.Zero);
            Assert.That(coordinator.IsTracked.Value, Is.False);
            Assert.That(coordinator.CurrentService, Is.Not.SameAs(tracked));
            Assert.That(tracked.EnsureHygieneCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Opening_a_project_in_an_enclosing_repository_without_commits_does_not_ask_to_share_it()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-unborn-enclosing-repository");
        string repositoryRoot = Path.GetDirectoryName(session.Root)!;
        var repository = new RepositoryInfo(repositoryRoot, session.Root);
        var discovery = new PullCycleTestBackend(null, repository, MainTip)
        {
            HasVersionTrackingOptIn = false,
            HasCheckedOutCommit = false,
        };
        var tracked = new PullCycleTestBackend(repository, repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int confirmations = 0;
        coordinator.ConfirmUseEnclosingRepositoryAsync = (_, _) =>
        {
            Interlocked.Increment(ref confirmations);
            return Task.FromResult(true);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => session.ProjectService.CurrentProject.Value is not null
                                   && discovery.HasCheckedOutCommitCalls > 0);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            // Activation leaves a branch with no commit for initialization, so agreeing to
            // share the enclosing repository would lead nowhere.
            Assert.That(confirmations, Is.Zero);
            Assert.That(coordinator.IsTracked.Value, Is.False);
            Assert.That(tracked.EnsureHygieneCalls, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task Accepted_root_repository_adoption_tracks_the_project()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-root-repository-accepted");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip)
        {
            HasVersionTrackingOptIn = false,
        };
        var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int adoptionPrompts = 0;
        coordinator.ConfirmAdoptExistingRepositoryAsync = (_, _) =>
        {
            Interlocked.Increment(ref adoptionPrompts);
            return Task.FromResult(true);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

        Assert.Multiple(() =>
        {
            Assert.That(adoptionPrompts, Is.EqualTo(1));
            Assert.That(coordinator.IsTracked.Value, Is.True);
            Assert.That(tracked.EnsureHygieneCalls, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    [TestCase("accept")]
    [TestCase("decline")]
    [TestCase("close")]
    public async Task Inline_repository_adoption_waits_for_a_decision_and_does_not_block_project_close(string response)
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            $"inline-adoption-{response}");
        VersionControlCoordinator? coordinator = null;
        var editors = new EditorService(TestShell.MainViewModel.ExtensionProvider);
        try
        {
            var discovery = new PullCycleTestBackend(null, session.Repository, MainTip) { HasVersionTrackingOptIn = false };
            var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
            await session.ProjectService.OpenProject(session.ProjectFile);
            editors.ActivateTabItem(session.ProjectService.CurrentProject.Value!.Items.OfType<Scene>().Single());
            var editor = (EditViewModel)editors.SelectedTabItem.Value!.Context.Value;
            if (editor.FindToolTab<Beutl.Editor.Components.VersionControlTab.ViewModels.VersionControlTabViewModel>() is { } previousTab)
                editor.CloseToolTab(previousTab);
            coordinator = new VersionControlCoordinator(session.ProjectService,
                editors, new VersionControlConfig { AutoCommitOnClose = false }, installationLocator: null,
                serviceFactory: candidate => candidate is null ? discovery : tracked);
            using var service = new ReactivePropertySlim<IProjectVersionControlService?>();
            var tab = new Beutl.Editor.Components.VersionControlTab.ViewModels.VersionControlTabViewModel(
                Beutl.Services.PrimitiveImpls.VersionControlTabExtension.Instance, editor, service, coordinator, action => action());
            Assert.That(editor.OpenToolTab(tab), Is.True);
            await WaitUntilAsync(() => coordinator.PendingRepositoryAdoption is not null);
            RepositoryAdoptionRequest request = coordinator.PendingRepositoryAdoption!;
            Assert.That(request.Repository, Is.EqualTo(session.Repository));
            Assert.That(tracked.EnsureHygieneCalls, Is.Zero);
            Assert.That(coordinator.IsTracked.Value, Is.False);

            if (response == "close")
            {
                await session.ProjectService.CloseProjectAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(session.ProjectService.CurrentProject.Value, Is.Null);
            }
            else
            {
                request.Respond(response == "accept");
            }
            await WaitUntilAsync(() => coordinator.PendingRepositoryAdoption is null);
            if (response == "accept")
                await WaitUntilAsync(() => coordinator.IsTracked.Value);
            Assert.That(tracked.EnsureHygieneCalls, Is.EqualTo(response == "accept" ? 1 : 0));
            Assert.That(coordinator.IsTracked.Value, Is.EqualTo(response == "accept"));
            Assert.That(request.Respond(true), Is.False);
        }
        finally
        {
            if (coordinator is not null) await coordinator.DisposeAsync();
            foreach (EditorTabItem item in editors.TabItems.ToArray())
                await editors.CloseTabItem(item);
        }
    }

    [AvaloniaTest]
    public async Task Root_repository_carrying_beutl_hygiene_is_tracked_without_asking()
    {
        await using var session = await FakeVersionControlSession.CreateForReopenAsync(
            "version-control-root-repository-managed");
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip)
        {
            HasVersionTrackingOptIn = true,
        };
        var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip);
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig { AutoCommitOnClose = false },
            candidate => candidate is null ? discovery : tracked);
        int adoptionPrompts = 0;
        coordinator.ConfirmAdoptExistingRepositoryAsync = (_, _) =>
        {
            Interlocked.Increment(ref adoptionPrompts);
            return Task.FromResult(true);
        };

        await session.ProjectService.OpenProject(session.ProjectFile);
        await WaitUntilAsync(() => ReferenceEquals(coordinator.CurrentService, tracked));

        Assert.Multiple(() =>
        {
            Assert.That(adoptionPrompts, Is.Zero);
            Assert.That(coordinator.IsTracked.Value, Is.True);
        });
    }

    // Git keeps working in these states, so opening must not silently stop tracking the project. The
    // backend itself still refuses every write until the state is resolved outside Beutl.
    private static async Task AssertBlockedRepositoryStaysTrackedAsync(
        string directoryName,
        Exception blockingState)
    {
        await using var session = await FakeVersionControlSession.CreateAsync(directoryName);
        var discovery = new PullCycleTestBackend(null, session.Repository, MainTip);
        var tracked = new PullCycleTestBackend(session.Repository, session.Repository, MainTip)
        {
            EnsureHygieneOverride = _ => Task.FromException(blockingState),
        };
        VersionControlCoordinator coordinator = session.Start(
            new VersionControlConfig(),
            candidate => candidate is null ? discovery : tracked);

        await WaitUntilAsync(() =>
            ReferenceEquals(coordinator.CurrentService, tracked)
            && coordinator.IsTracked.Value);

        Assert.Multiple(() =>
        {
            Assert.That(tracked.EnsureHygieneCalls, Is.EqualTo(1));
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(session.Project));
        });
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
        Project project = await CreateProjectForFakeVersionControlAsync(directoryName);

        bool initialized = await GitTestSupport.InitializeCurrentProjectAsync(
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

    // Points the project at a new bare origin and pushes to it.
    private static async Task<string> CreateOriginAsync(string gitPath, string projectRoot, string name)
    {
        string remoteRoot = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-remote.git");
        await RunGitAsync(gitPath, projectRoot, "init", "--bare", "-b", "main", remoteRoot);
        await TestShell.VersionControl.SetRemoteAsync(remoteRoot);
        Assert.That(
            await TestShell.VersionControl.PushAsync(progress: null),
            Is.TypeOf<RemoteOpResult.Success>());
        return remoteRoot;
    }

    // Puts the origin one commit ahead, a commit from another clone that writes the file, and returns it.
    private static async Task<string> CreatePeerAheadOfOriginAsync(
        string gitPath,
        string projectRoot,
        string name,
        string file,
        string contents,
        string message)
    {
        string remoteRoot = await CreateOriginAsync(gitPath, projectRoot, name);
        string peerRoot = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-peer");
        await RunGitAsync(gitPath, projectRoot, "clone", "--branch", "main", remoteRoot, peerRoot);
        await RunGitAsync(gitPath, peerRoot, "config", "user.name", "Beutl Headless Peer");
        await RunGitAsync(gitPath, peerRoot, "config", "user.email", "headless-peer@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(peerRoot, file), contents);
        await RunGitAsync(gitPath, peerRoot, "add", "--", file);
        await RunGitAsync(gitPath, peerRoot, "commit", "-m", message);
        await RunGitAsync(gitPath, peerRoot, "push");
        return (await RunGitAsync(gitPath, peerRoot, "rev-parse", "HEAD")).Trim();
    }

    // Commits a project file that cannot be read, puts the readable file back and returns the commit.
    private static async Task<string> CommitInvalidProjectAsync(string gitPath, Project project, string message)
    {
        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        string validProject = await File.ReadAllTextAsync(projectFile);
        await File.WriteAllTextAsync(projectFile, "{ invalid project json");
        string projectPathspec = Path.GetRelativePath(projectRoot, projectFile).Replace('\\', '/');
        await RunGitAsync(gitPath, projectRoot, "add", "--", projectPathspec);
        await RunGitAsync(gitPath, projectRoot, "commit", "-m", message);
        string commit = (await RunGitAsync(gitPath, projectRoot, "rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(projectFile, validProject);
        return commit;
    }

    // A save snapshot waits for the activation to finish deciding how the project is tracked.
    private static Task WaitForActivationDecisionAsync(VersionControlCoordinator coordinator)
        => coordinator.NotifySavedAsync();

    private static async Task AddRectangleAsync(IElementAdder adder, int layer)
    {
        ElementAddResult result = await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: layer,
            Source: new ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Failure?.Message);
        HeadlessTestHelpers.Settle();
    }

    // A new project for a test that runs its own coordinator over fake backends. Dispose puts the
    // coordinator away and then resets the shell.
    private sealed class FakeVersionControlSession : IAsyncDisposable
    {
        private VersionControlCoordinator? _coordinator;

        private FakeVersionControlSession(Project project, ProjectService projectService)
        {
            Project = project;
            ProjectService = projectService;
            ProjectFile = project.Uri!.LocalPath;
            Root = Path.GetDirectoryName(ProjectFile)!;
            Repository = new RepositoryInfo(Root, Root);
        }

        public Project Project { get; }

        public ProjectService ProjectService { get; }

        public string ProjectFile { get; }

        public string Root { get; }

        // The project root as a repository of its own.
        public RepositoryInfo Repository { get; }

        public EditorService EditorService { get; } = new(new ExtensionProvider());

        // The project stays open in the shell.
        public static async Task<FakeVersionControlSession> CreateAsync(string directoryName)
        {
            await TestReset.ResetShellAsync();
            Project project = await CreateProjectForFakeVersionControlAsync(directoryName);
            return new FakeVersionControlSession(project, TestShell.Project);
        }

        // The shell closes the project again, for the test to open it with a ProjectService of its own.
        public static async Task<FakeVersionControlSession> CreateForReopenAsync(string directoryName)
        {
            await TestReset.ResetShellAsync();
            Project project = await CreateProjectForFakeVersionControlAsync(directoryName);
            await TestShell.Project.CloseProjectAsync();
            return new FakeVersionControlSession(project, new ProjectService());
        }

        public VersionControlCoordinator Start(
            VersionControlConfig config,
            Func<RepositoryInfo?, IProjectVersionControlBackend> serviceFactory,
            GitInstallationLocator? installationLocator = null)
        {
            _coordinator = new VersionControlCoordinator(
                ProjectService,
                EditorService,
                config,
                installationLocator,
                serviceFactory);
            return _coordinator;
        }

        public async ValueTask DisposeAsync()
        {
            if (_coordinator is not null)
            {
                await _coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TestReset.ResetShellAsync();
        }
    }

    private sealed class BlockingGitInstallationProbe : IGitInstallationProbe
    {
        private int _startCount;
        private int _cancellationCount;
        private int _completionCount;

        public TaskCompletionSource ProbeStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ProbeCancelled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCancelledProbe { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount => Volatile.Read(ref _startCount);

        public int CancellationCount => Volatile.Read(ref _cancellationCount);

        public int CompletionCount => Volatile.Read(ref _completionCount);

        public async Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCount);
            ProbeStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _cancellationCount);
                ProbeCancelled.TrySetResult();

                await ReleaseCancelledProbe.Task;
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
            bool installed = FilePathComparison.AreSameCanonicalPath(
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

    // Records each activity shown in place of the editor area, and the activity shown whenever the editor is
    // disabled or enabled.
    private sealed class EditorPresentationRecorder : IDisposable
    {
        private readonly IDisposable _activitySubscription;
        private readonly IDisposable _editorSubscription;

        public EditorPresentationRecorder(EditorService editorService, IEditorContext context)
        {
            _activitySubscription = editorService.LifecycleActivity.Subscribe(Activities.Add);
            _editorSubscription = context.IsEnabled
                .Skip(1)
                .Subscribe(enabled => EditorStates.Add((enabled, editorService.LifecycleActivity.Value)));
        }

        public List<ProjectLifecycleActivity> Activities { get; } = [];

        public List<(bool Enabled, ProjectLifecycleActivity Activity)> EditorStates { get; } = [];

        public void Dispose()
        {
            _activitySubscription.Dispose();
            _editorSubscription.Dispose();
        }
    }

    // A prompt that waits until it is released or cancelled, reporting when it started and when it saw
    // its cancellation.
    private sealed class BlockingPrompt<T>
    {
        private readonly TaskCompletionSource<T> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(T answer) => _release.TrySetResult(answer);

        // Answers once released, or throws when cancelled, as a dismissed confirmation does.
        public async Task<T> AnswerAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                return await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }

        // Waits for the cancellation, then still answers once released, as a prompt that outlives its
        // request does.
        public async Task<T> AnswerLateAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
            }

            return await _release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    // An editor context whose members do nothing.
    private class FakeEditorContext(CoreObject obj) : IEditorContext
    {
        public CoreObject Object { get; } = obj;

        public EditorExtension Extension => SceneEditorExtension.Instance;

        public IReactiveProperty<bool> IsEnabled { get; } =
            new ReactivePropertySlim<bool>(true);

        public object? GetService(Type serviceType) => null;

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
            => default;

        public T? FindToolTab<T>()
            where T : IToolContext
            => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }
    }

    private sealed class SavableEditorContext(
        CoreObject obj,
        SaveOperation commands) : FakeEditorContext(obj), ISavableEditorContext
    {
        public ValueTask<bool> SaveAsync() => commands.SaveAsync();
    }

    // Re-implements IAsyncDisposable so that this DisposeAsync replaces the interface default.
    private sealed class BlockingDisposeEditorContext(
        CoreObject obj,
        TaskCompletionSource disposeStarted,
        Task releaseDispose) : FakeEditorContext(obj), IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            disposeStarted.TrySetResult();
            await releaseDispose;
        }
    }

    private sealed class SaveOperation(bool result)
    {
        public int SaveCalls { get; private set; }

        public Action? Saving { get; set; }

        public ValueTask<bool> SaveAsync()
        {
            SaveCalls++;
            Saving?.Invoke();
            return ValueTask.FromResult(result);
        }
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
        private readonly List<SnapshotKind> _commitKinds = [];
        private readonly CheckedOutBranchTip _originalTip;
        private readonly SemaphoreSlim _exclusiveGate = new(1, 1);
        private bool _hasIdentity;
        private bool _retired;
        private int _disposeCalls;
        private int _commitAllCalls;
        private int _ensureHygieneCalls;
        private int _hasCheckedOutCommitCalls;
        private int _retirementCalls;

        public PullCycleTestBackend(
            RepositoryInfo? repository,
            RepositoryInfo? discoveredRepository,
            CheckedOutBranchTip originalTip)
        {
            Repository = repository;
            _discoveredRepository = discoveredRepository;
            _originalTip = originalTip;
            // The fake's tip does not move, so a snapshot reports the commit the tip already names.
            CommitAllResult = new CommitResult.Committed(originalTip.Commit);
        }

        public RepositoryInfo? Repository { get; private set; }

        public RepositoryLockInfo? RecoverableLock { get; private set; }

        public RemoteOpResult PullResult { get; init; } = new RemoteOpResult.Failed("pull failed");

        public PullPreflightResult PreflightResult { get; init; } = new(
            new RemoteOpResult.Success(),
            RequiresTransition: true,
            UpstreamCommit: "2222222222222222222222222222222222222222");

        public TaskCompletionSource? PreflightStarted { get; init; }

        public Task? PreflightRelease { get; init; }

        public Exception? PreflightFailure { get; set; }

        public Func<CancellationToken, Task<RemoteOpResult>>? PullOverride { get; init; }

        public WorkspaceStatus Status { get; set; } = DirtyStatus;

        public CommitResult CommitAllResult { get; set; }

        public CommitResult? RetirementResult { get; set; } = new CommitResult.NoChanges();

        public Exception? CommitAllFailure { get; set; }

        public Action<SnapshotKind>? CommitAllObserver { get; init; }

        public CommitResult RestoreProjectTreeResult { get; set; } =
            new CommitResult.NoChanges();

        public Exception? RestoreProjectTreeFailure { get; set; }

        public IReadOnlyList<BranchInfo> Branches { get; init; } =
            [new BranchInfo("main", true, null)];

        public TaskCompletionSource? EnsureHygieneStarted { get; init; }

        public Task? EnsureHygieneRelease { get; init; }

        public TaskCompletionSource? EnsureHygieneCompleted { get; init; }

        public Action<CancellationToken>? EnsureHygieneTokenObserved { get; init; }

        public Func<CancellationToken, Task>? EnsureHygieneOverride { get; init; }

        public bool HasVersionTrackingOptIn { get; init; } = true;

        public bool HasCheckedOutCommit { get; init; } = true;

        public Func<CancellationToken, Task<GitAvailability>>? AvailabilityOverride { get; init; }

        public Func<string, RepositoryInfo?>? DiscoverRepositoryOverride { get; set; }

        public TaskCompletionSource? RetirementStarted { get; init; }

        public Task? RetirementRelease { get; init; }

        public Action? RetirementStarting { get; set; }

        public TaskCompletionSource? InitializeStarted { get; init; }

        public Task? InitializeRelease { get; init; }

        public TaskCompletionSource? SwitchBranchStarted { get; init; }

        public Task? SwitchBranchRelease { get; init; }

        public TaskCompletionSource? CommitAllStarted { get; init; }

        public Task? CommitAllRelease { get; init; }

        public bool RequireIdentityForInitialization { get; init; }

        // Thrown once initialization has attached the repository, as a failed initial commit would.
        public Exception? InitializeFailureAfterAttach { get; init; }

        public bool RequireIdentityForCommit { get; init; }

        // Starts an exclusive operation on the thread pool, as the Git backend does whenever another
        // command held its gate.
        public bool RunsExclusiveOperationsOnThreadPool { get; init; }

        public int InitializeCalls { get; private set; }

        public int SetLocalIdentityCalls { get; private set; }

        public int PreflightCalls { get; private set; }

        public int CommitAllCalls => Volatile.Read(ref _commitAllCalls);

        public int EnsureHygieneCalls => Volatile.Read(ref _ensureHygieneCalls);

        public int HasCheckedOutCommitCalls => Volatile.Read(ref _hasCheckedOutCommitCalls);

        public IReadOnlyList<SnapshotKind> CommitKinds
        {
            get
            {
                lock (_commitKinds)
                {
                    return _commitKinds.ToArray();
                }
            }
        }

        public int PullCalls { get; private set; }

        public string? LastPulledCommit { get; private set; }

        public int GetBranchesCalls { get; private set; }

        public int CanCreateBranchCalls { get; private set; }

        public int CreateBranchCalls { get; private set; }

        public int PrefetchBranchLfsCalls { get; private set; }

        public Action? PrefetchBranchLfsObserver { get; set; }

        public int PrefetchCommitLfsCalls { get; private set; }

        public Action? PrefetchCommitLfsObserver { get; set; }

        public string? LastPrefetchedCommit { get; private set; }

        public LfsPrefetchScope? LastPrefetchCommitLfsScope { get; private set; }

        public int RetirementCalls => Volatile.Read(ref _retirementCalls);

        public int RemoveRecoverableLockCalls { get; private set; }

        public bool RemoveRecoverableLockResult { get; init; }

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

        public Task<bool> HasVersionTrackingOptInAsync(
            RepositoryInfo repository,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(HasVersionTrackingOptIn);
        }

        public Task<bool> HasCheckedOutCommitAsync(
            RepositoryInfo repository,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _hasCheckedOutCommitCalls);
            return Task.FromResult(HasCheckedOutCommit);
        }

        public async Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ensureHygieneCalls);
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

            if (PreflightFailure is not null)
            {
                throw PreflightFailure;
            }

            return PreflightResult;
        }

        public async Task<RemoteOpResult> PullFastForwardAsync(
            string upstreamCommit,
            CancellationToken cancellationToken)
        {
            PullCalls++;
            LastPulledCommit = upstreamCommit;
            if (PullOverride is not null)
            {
                return await PullOverride(cancellationToken);
            }

            return PullResult;
        }

        public async Task<TResult> ExecuteExclusiveAsync<TResult>(
            Func<IProjectVersionControlTransaction, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            await _exclusiveGate.WaitAsync(cancellationToken);
            try
            {
                return RunsExclusiveOperationsOnThreadPool
                    ? await Task.Run(() => operation(this), cancellationToken)
                    : await operation(this);
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
            if (InitializeFailureAfterAttach is not null)
            {
                throw InitializeFailureAfterAttach;
            }
        }

        public async Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
        {
            RecordBackendCall();
            Interlocked.Increment(ref _commitAllCalls);
            lock (_commitKinds)
            {
                _commitKinds.Add(kind);
            }

            if (RequireIdentityForCommit && !_hasIdentity)
            {
                throw new GitIdentityRequiredException();
            }

            CommitAllObserver?.Invoke(kind);
            CommitAllStarted?.TrySetResult();
            if (CommitAllRelease is not null)
            {
                await CommitAllRelease.WaitAsync(cancellationToken);
            }

            if (CommitAllFailure is not null)
            {
                throw CommitAllFailure;
            }

            return CommitAllResult;
        }

        public List<(string Source, SnapshotKind Kind)> RestoreProjectTreeCalls { get; } = [];

        public Task<CommitResult> RestoreProjectTreeAsync(
            string sourceCommit,
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
        {
            RestoreProjectTreeCalls.Add((sourceCommit, kind));
            if (RestoreProjectTreeFailure is not null && kind == SnapshotKind.Restore)
            {
                return Task.FromException<CommitResult>(RestoreProjectTreeFailure);
            }

            return Task.FromResult(RestoreProjectTreeResult);
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
            RecordBackendCall();
            return Task.CompletedTask;
        }

        public TaskCompletionSource? PushStarted { get; init; }

        public Task? PushRelease { get; init; }

        public async Task<RemoteOpResult> PushAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            PushStarted?.TrySetResult();
            if (PushRelease is not null)
            {
                await PushRelease.WaitAsync(cancellationToken);
            }

            return new RemoteOpResult.Success();
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

        public async Task<CommitResult?> RetireAsync(
            ProjectVersionControlFinalSnapshot? finalSnapshot)
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
                return finalSnapshot is null ? null : RetirementResult;
            }
            finally
            {
                _exclusiveGate.Release();
            }
        }

        public Task<bool> RemoveRecoverableLockAsync(
            RepositoryLockInfo expectedLock,
            CancellationToken cancellationToken)
        {
            RemoveRecoverableLockCalls++;
            if (RemoveRecoverableLockResult
                && ReferenceEquals(RecoverableLock, expectedLock))
            {
                RecoverableLock = null;
                return Task.FromResult(true);
            }

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

        public string? LastBranchStartPoint { get; private set; }

        public Task CreateBranchAsync(
            string name,
            string startPoint,
            CancellationToken cancellationToken)
        {
            CreateBranchCalls++;
            LastBranchStartPoint = startPoint;
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> TrackedReservedPaths { get; init; } = [];

        public Task<IReadOnlyList<string>> GetTrackedReservedPathsAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(TrackedReservedPaths);
        }

        public Task UntrackReservedPathsAsync(
            IReadOnlyList<string> reservedPaths,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task PrefetchBranchLfsObjectsAsync(string name, CancellationToken cancellationToken)
        {
            PrefetchBranchLfsCalls++;
            PrefetchBranchLfsObserver?.Invoke();
            return Task.CompletedTask;
        }

        public Task PrefetchCommitLfsObjectsAsync(
            string sha,
            LfsPrefetchScope scope,
            CancellationToken cancellationToken)
        {
            PrefetchCommitLfsCalls++;
            LastPrefetchedCommit = sha;
            LastPrefetchCommitLfsScope = scope;
            PrefetchCommitLfsObserver?.Invoke();
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
        }

        private void RecordBackendCall()
        {
            if (_retired)
            {
                CallsAfterRetirement++;
            }
        }
    }
}
