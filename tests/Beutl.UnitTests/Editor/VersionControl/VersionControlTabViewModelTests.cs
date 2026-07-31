using System.Globalization;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlTabViewModelTests
{
    [Test]
    public async Task Not_installed_collapses_to_platform_install_guidance()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitAvailability.NotInstalled);
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);

        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsGitAvailable.Value, Is.False);
            Assert.That(viewModel.IsUnavailable.Value, Is.True);
            Assert.That(viewModel.HasBlockingGuidance.Value, Is.True);
            Assert.That(viewModel.IsTracked.Value, Is.False);
            Assert.That(viewModel.CanEnableVersionControl.Value, Is.False);
            Assert.That(viewModel.StatusMessage.Value, Does.Contain(Strings.VersionControl_GitNotInstalled));
            Assert.That(viewModel.StatusMessage.Value, Does.Contain(GetPlatformInstallGuidance()));
        });
        service.Verify(
            x => x.GetStatusAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        service.Verify(
            x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Version_too_old_collapses_to_versioned_platform_install_guidance()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitAvailability(
                GitAvailabilityState.VersionTooOld,
                "/usr/bin/git",
                new Version(2, 22, 1),
                LfsInstalled: false));
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);

        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsGitAvailable.Value, Is.False);
            Assert.That(viewModel.IsUnavailable.Value, Is.True);
            Assert.That(viewModel.HasBlockingGuidance.Value, Is.True);
            Assert.That(viewModel.CanEnableVersionControl.Value, Is.False);
            Assert.That(viewModel.StatusMessage.Value, Does.Contain("2.22.1"));
            Assert.That(viewModel.StatusMessage.Value, Does.Contain(GetPlatformInstallGuidance()));
        });
    }

    [Test]
    public async Task Installed_git_with_an_untracked_project_shows_only_the_repository_state()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.SetupGet(x => x.Repository).Returns((RepositoryInfo?)null);
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);

        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsGitAvailable.Value, Is.True);
            Assert.That(viewModel.IsUnavailable.Value, Is.False);
            Assert.That(viewModel.IsTracked.Value, Is.False);
            Assert.That(viewModel.CanEnableVersionControl.Value, Is.True);
            Assert.That(viewModel.IsHistoryEmpty.Value, Is.True);
            Assert.That(viewModel.HasSelectedCommit.Value, Is.False);
            Assert.That(viewModel.HasSelectedFile.Value, Is.False);
            Assert.That(viewModel.StatusMessage.Value, Is.EqualTo(Strings.VersionControl_NoRepository));
        });
    }

    [Test]
    public async Task Publishing_a_ready_service_after_construction_rebuilds_the_tracked_state()
    {
        CommitInfo commit = CreateCommit(101, SnapshotKind.Save);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(null);
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            serviceSource,
            versionControlCoordinator: null,
            action => action());
        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsTracked.Value, Is.False);
            Assert.That(viewModel.Commits, Is.Empty);
            Assert.That(viewModel.StatusMessage.Value,
                Is.EqualTo(Strings.VersionControl_NoRepository));
        });

        serviceSource.Value = service.Object;
        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsTracked.Value, Is.True);
            Assert.That(viewModel.IsGitAvailable.Value, Is.True);
            Assert.That(viewModel.Commits.Select(item => item.Commit),
                Is.EqualTo([commit]));
        });
    }

    [Test]
    public async Task Replacing_the_service_rebuilds_state_and_resubscribes_status_events()
    {
        CommitInfo commitA = CreateCommit(102, SnapshotKind.Save);
        CommitInfo commitB = CreateCommit(103, SnapshotKind.Manual);
        Mock<IProjectVersionControlService> serviceA = CreateServiceMock();
        serviceA.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("project-a", 0, 0, [], false));
        serviceA.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commitA]);
        Mock<IProjectVersionControlService> serviceB = CreateServiceMock();
        serviceB.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("project-b", 0, 0, [], false));
        serviceB.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commitB]);
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(serviceA.Object);
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            serviceSource,
            versionControlCoordinator: null,
            action => action());
        await viewModel.Initialization;

        serviceSource.Value = serviceB.Object;
        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Commits.Select(item => item.Commit),
                Is.EqualTo([commitB]));
            Assert.That(
                viewModel.DirtySummary.Value,
                Is.EqualTo(Strings.VersionControl_WorktreeClean));
        });
        serviceA.VerifyRemove(
            x => x.StatusChanged -= It.IsAny<EventHandler<WorkspaceStatus>>(),
            Times.Once);
        serviceB.VerifyAdd(
            x => x.StatusChanged += It.IsAny<EventHandler<WorkspaceStatus>>(),
            Times.Once);

        serviceA.Raise(
            x => x.StatusChanged += null,
            serviceA.Object,
            new WorkspaceStatus(
                "stale-project-a",
                0,
                0,
                [new FileChange("stale.bep", FileChangeStatus.Modified)],
                false));
        Assert.That(
            viewModel.DirtySummary.Value,
            Is.EqualTo(Strings.VersionControl_WorktreeClean));

        serviceB.Raise(
            x => x.StatusChanged += null,
            serviceB.Object,
            new WorkspaceStatus(
                "project-b-updated",
                0,
                0,
                [
                    new FileChange("one.bep", FileChangeStatus.Modified),
                    new FileChange("two.bep", FileChangeStatus.Modified),
                ],
                false));
        Assert.That(viewModel.DirtySummary.Value, Does.Contain("2"));
    }

    [Test]
    public async Task Enable_action_runs_injected_shell_flow_and_updates_the_tracked_state()
    {
        RepositoryInfo? repository = null;
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.SetupGet(x => x.Repository).Returns(() => repository);
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);
        int requestCount = 0;
        viewModel.RequestEnableVersionControlAsync = () =>
        {
            requestCount++;
            repository = new RepositoryInfo(
                Path.Combine(Path.GetTempPath(), "beutl-version-control-tab"),
                Path.Combine(Path.GetTempPath(), "beutl-version-control-tab"));
            return Task.CompletedTask;
        };
        await viewModel.Initialization;

        await viewModel.EnableVersionControlAsync();

        Assert.Multiple(() =>
        {
            Assert.That(requestCount, Is.EqualTo(1));
            Assert.That(viewModel.IsTracked.Value, Is.True);
            Assert.That(viewModel.CanEnableVersionControl.Value, Is.False);
        });
    }

    [Test]
    public async Task Download_action_uses_the_injected_launcher_with_the_git_downloads_uri()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitAvailability.NotInstalled);
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);
        Uri? launchedUri = null;
        viewModel.LaunchUriAsync = uri =>
        {
            launchedUri = uri;
            return Task.FromResult(true);
        };
        await viewModel.Initialization;

        await viewModel.DownloadGitAsync();

        Assert.That(
            launchedUri,
            Is.EqualTo(new Uri("https://git-scm.com/downloads")));
    }

    [Test]
    public async Task Conflicted_repository_collapses_to_external_resolution_guidance()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus(
                "main",
                0,
                0,
                [new FileChange("project.bep", FileChangeStatus.Modified)],
                HasConflicts: true));
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);

        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsConflicted.Value, Is.True);
            Assert.That(viewModel.HasBlockingGuidance.Value, Is.True);
            Assert.That(
                viewModel.StatusMessage.Value,
                Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(viewModel.Commits, Is.Empty);
            Assert.That(viewModel.HasMoreHistory.Value, Is.False);
        });
        service.Verify(
            x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task History_is_loaded_incrementally_and_status_is_formatted()
    {
        CommitInfo[] commits = Enumerable.Range(0, 53)
            .Select(index => CreateCommit(index, SnapshotKind.Save))
            .ToArray();
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 2, 1, [], false));
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int skip, int take, CancellationToken _) =>
                commits.Skip(skip).Take(take).ToArray());
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);

        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsTracked.Value, Is.True);
            Assert.That(viewModel.CanEnableVersionControl.Value, Is.False);
            Assert.That(viewModel.IsHistoryEmpty.Value, Is.False);
            Assert.That(viewModel.Commits, Has.Count.EqualTo(50));
            Assert.That(viewModel.HasMoreHistory.Value, Is.True);
            Assert.That(viewModel.DirtySummary.Value, Is.Not.Empty);
        });

        await viewModel.LoadMoreAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Commits, Has.Count.EqualTo(53));
            Assert.That(viewModel.HasMoreHistory.Value, Is.False);
        });
        service.Verify(
            x => x.GetHistoryAsync(0, 50, It.IsAny<CancellationToken>()),
            Times.Once);
        service.Verify(
            x => x.GetHistoryAsync(50, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Primary_action_follows_the_complete_priority_matrix()
    {
        (
            string Name,
            bool HasChanges,
            int Ahead,
            int Behind,
            bool HasRemote,
            bool IsRunning,
            string CommitMessage,
            VersionControlPrimaryActionKind ExpectedKind,
            string ExpectedLabel,
            bool ExpectedCanExecute)[] cases =
        [
            (
                "uncommitted changes",
                true,
                0,
                0,
                false,
                false,
                "checkpoint",
                VersionControlPrimaryActionKind.Commit,
                Strings.VersionControl_CommitNow,
                true),
            (
                "commit requires a message",
                true,
                0,
                0,
                false,
                false,
                string.Empty,
                VersionControlPrimaryActionKind.Commit,
                Strings.VersionControl_CommitNow,
                false),
            (
                "behind remote",
                false,
                0,
                3,
                true,
                false,
                string.Empty,
                VersionControlPrimaryActionKind.Pull,
                string.Format(Strings.VersionControl_PullCountFormat, 3),
                true),
            (
                "ahead remote",
                false,
                2,
                0,
                true,
                false,
                string.Empty,
                VersionControlPrimaryActionKind.Push,
                string.Format(Strings.VersionControl_PushCountFormat, 2),
                true),
            (
                "clean remote",
                false,
                0,
                0,
                true,
                false,
                string.Empty,
                VersionControlPrimaryActionKind.UpToDate,
                Strings.VersionControl_UpToDate,
                false),
            (
                "clean unpublished branch",
                false,
                0,
                0,
                false,
                false,
                string.Empty,
                VersionControlPrimaryActionKind.PublishBranch,
                Strings.VersionControl_PublishBranch,
                true),
            (
                "remote operation",
                false,
                0,
                0,
                true,
                true,
                string.Empty,
                VersionControlPrimaryActionKind.Cancel,
                Strings.Cancel,
                true),
            (
                "changes win over behind",
                true,
                0,
                4,
                true,
                false,
                "checkpoint",
                VersionControlPrimaryActionKind.Commit,
                Strings.VersionControl_CommitNow,
                true),
        ];

        foreach (var testCase in cases)
        {
            Mock<IProjectVersionControlService> service = CreateServiceMock();
            service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkspaceStatus(
                    "main",
                    testCase.Ahead,
                    testCase.Behind,
                    testCase.HasChanges
                        ? [new FileChange("project.bep", FileChangeStatus.Modified)]
                        : [],
                    false));
            service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(testCase.HasRemote
                    ? [new RemoteInfo("origin", "https://example.invalid/repo.git")]
                    : []);
            using VersionControlTabViewModel viewModel = CreateViewModel(
                service.Object,
                Mock.Of<IProjectVersionControlCoordinator>());
            await viewModel.Initialization;
            viewModel.CommitMessage.Value = testCase.CommitMessage;
            viewModel.IsRemoteOperationRunning.Value = testCase.IsRunning;

            VersionControlPrimaryAction action = viewModel.PrimaryAction.Value;
            Assert.Multiple(() =>
            {
                Assert.That(
                    action.Kind,
                    Is.EqualTo(testCase.ExpectedKind),
                    testCase.Name);
                Assert.That(
                    action.Label,
                    Is.EqualTo(testCase.ExpectedLabel),
                    testCase.Name);
                Assert.That(
                    action.Command.CanExecute(null),
                    Is.EqualTo(testCase.ExpectedCanExecute),
                    testCase.Name);
                Assert.That(
                    viewModel.IsPrimaryActionEnabled.Value,
                    Is.EqualTo(testCase.ExpectedCanExecute),
                    testCase.Name);
            });

            if (testCase.IsRunning)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(viewModel.CommitCommand.CanExecute(), Is.False);
                    Assert.That(viewModel.PushCommand.CanExecute(), Is.False);
                    Assert.That(viewModel.PullCommand.CanExecute(), Is.False);
                    Assert.That(viewModel.SetRemoteCommand.CanExecute(), Is.False);
                    Assert.That(viewModel.CancelRemoteOperationCommand.CanExecute(), Is.True);
                });
            }
        }
    }

    [Test]
    public async Task Background_status_event_is_marshaled_before_view_model_state_changes()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Action? pendingUiAction = null;
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            CreateServiceSource(service.Object),
            versionControlCoordinator: null,
            action => pendingUiAction = action);
        await viewModel.Initialization;

        await Task.Run(() => service.Raise(
            x => x.StatusChanged += null,
            service.Object,
            new WorkspaceStatus(
                "external",
                3,
                4,
                [new FileChange("project.bep", FileChangeStatus.Modified)],
                false)));

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.DirtySummary.Value,
                Is.EqualTo(Strings.VersionControl_WorktreeClean));
            Assert.That((object?)pendingUiAction, Is.Not.Null);
        });

        pendingUiAction!();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.DirtySummary.Value, Does.Contain("1"));
        });
    }

    [Test]
    public async Task Stale_lock_event_is_marshaled_and_the_button_removes_only_on_request()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        Mock<IRepositoryLockRecoveryService> recovery
            = service.As<IRepositoryLockRecoveryService>();
        RepositoryLockInfo? currentLock = null;
        recovery.SetupGet(x => x.RecoverableLock).Returns(() => currentLock);
        recovery.Setup(x => x.RemoveRecoverableLockAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                currentLock = null;
                return true;
            });
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Action? pendingUiAction = null;
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            CreateServiceSource(service.Object),
            versionControlCoordinator: null,
            action => pendingUiAction = action);
        await viewModel.Initialization;
        currentLock = new RepositoryLockInfo(
            Path.Combine(Path.GetTempPath(), "index.lock"),
            DateTimeOffset.UtcNow - TimeSpan.FromMinutes(11));

        await Task.Run(() => recovery.Raise(
            x => x.RecoverableLockAvailable += null,
            service.Object,
            currentLock));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasRecoverableLock.Value, Is.False);
            Assert.That((object?)pendingUiAction, Is.Not.Null);
        });

        pendingUiAction!();
        Assert.That(viewModel.HasRecoverableLock.Value, Is.True);

        await viewModel.RemoveStaleLockAsync();

        Assert.That(viewModel.HasRecoverableLock.Value, Is.False);
        recovery.Verify(
            x => x.RemoveRecoverableLockAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Selecting_commit_and_file_loads_changes_and_classifies_diff_lines()
    {
        CommitInfo commit = CreateCommit(1, SnapshotKind.Restore);
        var file = new FileChange("project.bep", FileChangeStatus.Modified);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        service.Setup(x => x.GetCommitFilesAsync(
                commit.Sha,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([file]);
        service.Setup(x => x.GetDiffAsync(
                commit.Sha,
                file.Path,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("--- a/project.bep\n+++ b/project.bep\n-old\n+new\n unchanged\n");
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);
        await viewModel.Initialization;

        VersionControlCommitViewModel commitViewModel = viewModel.Commits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsHistoryEmpty.Value, Is.False);
            Assert.That(viewModel.HasSelectedCommit.Value, Is.False);
            Assert.That(viewModel.HasSelectedFile.Value, Is.False);
        });

        await viewModel.SelectCommitAsync(commitViewModel);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasSelectedCommit.Value, Is.True);
            Assert.That(viewModel.HasSelectedFile.Value, Is.False);
            Assert.That(viewModel.ShowingDetail.Value, Is.False);
        });

        await viewModel.SelectFileAsync(viewModel.ChangedFiles.Single());

        Assert.Multiple(() =>
        {
            Assert.That(commitViewModel.KindText, Is.EqualTo(Strings.VersionControl_SnapshotRestore));
            Assert.That(viewModel.HasSelectedCommit.Value, Is.True);
            Assert.That(viewModel.HasSelectedFile.Value, Is.True);
            Assert.That(viewModel.ChangedFiles.Single().PathText, Is.EqualTo("project.bep"));
            Assert.That(
                viewModel.DiffLines.Count(x => x.Kind == VersionControlDiffLineKind.Header),
                Is.EqualTo(2));
            Assert.That(
                viewModel.DiffLines.Any(line => line.Text == "-old" && line.IsRemoved),
                Is.True);
            Assert.That(
                viewModel.DiffLines.Any(line => line.Text == "+new" && line.IsAdded),
                Is.True);
        });

        await viewModel.SelectFileAsync(null);
        await viewModel.SelectCommitAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasSelectedCommit.Value, Is.False);
            Assert.That(viewModel.HasSelectedFile.Value, Is.False);
        });
    }

    [Test]
    public async Task Selecting_another_commit_swallows_the_previous_selection_cancellation()
    {
        CommitInfo firstCommit = CreateCommit(1, SnapshotKind.Save);
        CommitInfo secondCommit = CreateCommit(2, SnapshotKind.Manual);
        var secondFile = new FileChange("elements/two.belm", FileChangeStatus.Modified);
        var firstRequestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstCommit, secondCommit]);
        service.Setup(x => x.GetCommitFilesAsync(
                firstCommit.Sha,
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken cancellationToken) =>
            {
                firstRequestStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Array.Empty<FileChange>();
            });
        service.Setup(x => x.GetCommitFilesAsync(
                secondCommit.Sha,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([secondFile]);
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);
        await viewModel.Initialization;

        Task firstSelection = viewModel.SelectCommitAsync(viewModel.Commits[0]);
        await firstRequestStarted.Task;
        Task secondSelection = viewModel.SelectCommitAsync(viewModel.Commits[1]);

        Assert.DoesNotThrowAsync(async () =>
            await Task.WhenAll(firstSelection, secondSelection));
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedCommit.Value, Is.SameAs(viewModel.Commits[1]));
            Assert.That(viewModel.ChangedFiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.ChangedFiles[0].PathText, Is.EqualTo(secondFile.Path));
        });
    }

    [Test]
    public async Task Narrow_drill_down_opens_detail_and_back_preserves_the_selection()
    {
        CommitInfo commit = CreateCommit(1, SnapshotKind.Save);
        var file = new FileChange("elements/one.belm", FileChangeStatus.Modified);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        service.Setup(x => x.GetCommitFilesAsync(
                commit.Sha,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([file]);
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);
        await viewModel.Initialization;
        VersionControlCommitViewModel selected = viewModel.Commits.Single();

        await viewModel.OpenCommitDetailAsync(selected);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowingDetail.Value, Is.True);
            Assert.That(viewModel.SelectedCommit.Value, Is.SameAs(selected));
            Assert.That(viewModel.ChangedFiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.BackToHistoryCommand.CanExecute(), Is.True);
        });

        viewModel.BackToHistoryCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowingDetail.Value, Is.False);
            Assert.That(viewModel.SelectedCommit.Value, Is.SameAs(selected));
            Assert.That(viewModel.ChangedFiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.BackToHistoryCommand.CanExecute(), Is.False);
        });

        viewModel.ShowSelectedCommitDetail();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowingDetail.Value, Is.True);
            Assert.That(viewModel.SelectedCommit.Value, Is.SameAs(selected));
            Assert.That(viewModel.ChangedFiles, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Restore_request_ignores_reentry_without_disabling_the_view_action()
    {
        CommitInfo commit = CreateCommit(1, SnapshotKind.Manual);
        var restoreStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.RestoreAsync(
                commit.Sha,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                restoreStarted.TrySetResult();
                return restoreCompletion.Task;
            });
        using VersionControlTabViewModel viewModel = CreateViewModel(
            service.Object,
            coordinator.Object);
        await viewModel.Initialization;

        Task<bool> firstRequest = viewModel.RestoreAsync(commit);
        await restoreStarted.Task;
        bool reentered = await viewModel.RestoreAsync(commit);
        restoreCompletion.SetResult(true);
        bool completed = await firstRequest;

        Assert.Multiple(() =>
        {
            Assert.That(reentered, Is.False);
            Assert.That(completed, Is.True);
        });
        coordinator.Verify(
            x => x.RestoreAsync(
                commit.Sha,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Restore_to_new_branch_uses_prompted_name_and_coordinator_cycle()
    {
        CommitInfo commit = CreateCommit(1, SnapshotKind.Save);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.RestoreToNewBranchAsync(
                commit.Sha,
                "restored-version",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            CreateServiceSource(service.Object),
            coordinator.Object,
            action => action())
        {
            RequestBranchNameAsync = _ => Task.FromResult<string?>(" restored-version "),
        };
        await viewModel.Initialization;

        bool restored = await viewModel.RestoreToNewBranchAsync(commit);

        Assert.That(restored, Is.True);
        coordinator.Verify(
            x => x.RestoreToNewBranchAsync(
                commit.Sha,
                "restored-version",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Manual_commit_reports_no_changes_and_preserves_the_message()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.CommitManualAsync(
                "rough cut",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitResult.NoChanges());
        using var viewModel = CreateViewModel(service.Object, coordinator.Object);
        await viewModel.Initialization;
        viewModel.CommitMessage.Value = " rough cut ";

        await viewModel.CommitManualAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.StatusMessage.Value,
                Is.EqualTo(Strings.VersionControl_NothingToCommit));
            Assert.That(viewModel.CommitMessage.Value, Is.EqualTo(" rough cut "));
        });
        coordinator.Verify(
            x => x.CommitManualAsync("rough cut", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Manual_commit_clears_the_message_and_manual_history_has_a_distinct_badge_state()
    {
        CommitInfo manual = CreateCommit(1, SnapshotKind.Manual);
        CommitInfo automatic = CreateCommit(2, SnapshotKind.Save);
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([manual, automatic]);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.CommitManualAsync(
                "milestone",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitResult.Committed(manual.Sha));
        using var viewModel = CreateViewModel(service.Object, coordinator.Object);
        await viewModel.Initialization;
        viewModel.CommitMessage.Value = "milestone";

        await viewModel.CommitManualAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CommitMessage.Value, Is.Empty);
            Assert.That(viewModel.StatusMessage.Value,
                Is.EqualTo(Strings.VersionControl_CommitCreated));
            Assert.That(viewModel.Commits[0].IsManual, Is.True);
            Assert.That(viewModel.Commits[1].IsManual, Is.False);
            Assert.That(viewModel.Commits[0].KindText,
                Is.EqualTo(Strings.VersionControl_SnapshotManual));
        });
    }

    [Test]
    public async Task Every_snapshot_kind_exposes_exactly_one_badge_class_state()
    {
        SnapshotKind[] kinds =
        [
            SnapshotKind.Manual,
            SnapshotKind.Save,
            SnapshotKind.Close,
            SnapshotKind.Safety,
            SnapshotKind.Restore,
            SnapshotKind.Init,
        ];
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(kinds.Select((kind, index) => CreateCommit(index, kind)).ToArray());
        using VersionControlTabViewModel viewModel = CreateViewModel(service.Object);

        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            foreach (VersionControlCommitViewModel commit in viewModel.Commits)
            {
                int activeClasses = new[]
                {
                    commit.IsManual,
                    commit.IsSave,
                    commit.IsClose,
                    commit.IsSafety,
                    commit.IsRestore,
                    commit.IsInit,
                }.Count(static value => value);
                Assert.That(activeClasses, Is.EqualTo(1), commit.Commit.Kind.ToString());
            }
        });
    }

    [TestCase("en-US", 0, "Just now")]
    [TestCase("en-US", 59, "Just now")]
    [TestCase("en-US", 60, "1 minute ago")]
    [TestCase("en-US", 120, "2 minutes ago")]
    [TestCase("en-US", 3600, "1 hour ago")]
    [TestCase("en-US", 7200, "2 hours ago")]
    [TestCase("en-US", 86400, "1 day ago")]
    [TestCase("en-US", 172800, "2 days ago")]
    [TestCase("ja-JP", 0, "たった今")]
    [TestCase("ja-JP", 59, "たった今")]
    [TestCase("ja-JP", 60, "1分前")]
    [TestCase("ja-JP", 120, "2分前")]
    [TestCase("ja-JP", 3600, "1時間前")]
    [TestCase("ja-JP", 7200, "2時間前")]
    [TestCase("ja-JP", 86400, "1日前")]
    [TestCase("ja-JP", 172800, "2日前")]
    public void Relative_time_formatter_uses_localized_thresholds(
        string cultureName,
        int elapsedSeconds,
        string expected)
    {
        var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var formatter = new VersionControlRelativeTimeFormatter(
            timeProvider,
            CultureInfo.GetCultureInfo(cultureName));

        string result = formatter.Format(now - TimeSpan.FromSeconds(elapsedSeconds));

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task Remote_push_reports_progress_and_maps_expected_failure_to_notification()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RemoteInfo("origin", "https://example.invalid/repo.git")]);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.PushAsync(
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IProgress<string>, CancellationToken>((progress, _) =>
            {
                progress.Report("Writing objects: 50%");
                return Task.FromResult<RemoteOpResult>(new RemoteOpResult.Offline());
            });
        RemoteOpResult? shownResult = null;
        using var viewModel = CreateViewModel(service.Object, coordinator.Object);
        viewModel.ShowRemoteResultAsync = result =>
        {
            shownResult = result;
            return Task.CompletedTask;
        };
        await viewModel.Initialization;

        await viewModel.PushAsync();

        Assert.Multiple(() =>
        {
            Assert.That(shownResult, Is.TypeOf<RemoteOpResult.Offline>());
            Assert.That(viewModel.RemoteProgress.Value, Is.EqualTo("Writing objects: 50%"));
            Assert.That(viewModel.IsRemoteOperationRunning.Value, Is.False);
        });
    }

    [Test]
    public async Task Set_remote_prompt_prefills_the_current_url_and_updates_origin()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RemoteInfo("origin", "https://example.invalid/old.git")]);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        using VersionControlTabViewModel viewModel = CreateViewModel(
            service.Object,
            coordinator.Object);
        string? prefilledUrl = null;
        viewModel.RequestRemoteUrlAsync = currentUrl =>
        {
            prefilledUrl = currentUrl;
            return Task.FromResult<string?>(" https://example.invalid/new.git ");
        };
        await viewModel.Initialization;

        await viewModel.SetRemoteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(prefilledUrl, Is.EqualTo("https://example.invalid/old.git"));
            Assert.That(
                viewModel.RemoteUrl.Value,
                Is.EqualTo("https://example.invalid/new.git"));
            Assert.That(viewModel.HasRemote.Value, Is.True);
        });
        coordinator.Verify(
            x => x.SetRemoteAsync(
                "https://example.invalid/new.git",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Publish_branch_sets_the_remote_then_pushes()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.PushAsync(
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteOpResult.Success());
        using VersionControlTabViewModel viewModel = CreateViewModel(
            service.Object,
            coordinator.Object);
        string? prefilledUrl = "not requested";
        viewModel.RequestRemoteUrlAsync = currentUrl =>
        {
            prefilledUrl = currentUrl;
            return Task.FromResult<string?>("https://example.invalid/new.git");
        };
        await viewModel.Initialization;

        await viewModel.PublishBranchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(prefilledUrl, Is.Null);
            Assert.That(viewModel.IsRemoteOperationRunning.Value, Is.False);
        });
        coordinator.Verify(
            x => x.SetRemoteAsync(
                "https://example.invalid/new.git",
                It.IsAny<CancellationToken>()),
            Times.Once);
        coordinator.Verify(
            x => x.PushAsync(
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void Remote_results_have_distinct_actionable_messages()
    {
        const string authGuidance = "Configure a credential helper or SSH agent.";
        const string stderr = "fatal: unexpected remote failure";

        Assert.Multiple(() =>
        {
            Assert.That(
                VersionControlTabViewModel.GetRemoteResultMessage(
                    new RemoteOpResult.AuthFailed(authGuidance)),
                Is.EqualTo(authGuidance));
            Assert.That(
                VersionControlTabViewModel.GetRemoteResultMessage(
                    new RemoteOpResult.Diverged()),
                Is.EqualTo(Strings.VersionControl_Diverged));
            Assert.That(
                VersionControlTabViewModel.GetRemoteResultMessage(
                    new RemoteOpResult.Offline()),
                Is.EqualTo(Strings.VersionControl_Offline));
            Assert.That(
                VersionControlTabViewModel.GetRemoteResultMessage(
                    new RemoteOpResult.Failed(stderr)),
                Is.EqualTo(stderr));
            Assert.That(
                VersionControlTabViewModel.GetRemoteResultMessage(
                    new RemoteOpResult.Success()),
                Is.Empty);
        });
    }

    [Test]
    public async Task Remote_pull_can_be_canceled()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RemoteInfo("origin", "https://example.invalid/repo.git")]);
        var pullStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(x => x.PullAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async cancellationToken =>
            {
                pullStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new RemoteOpResult.Success();
            });
        using var viewModel = CreateViewModel(service.Object, coordinator.Object);
        await viewModel.Initialization;

        Task pull = viewModel.PullAsync();
        await pullStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelRemoteOperationCommand.Execute();
        await pull;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsRemoteOperationRunning.Value, Is.False);
            Assert.That(viewModel.StatusMessage.Value,
                Is.EqualTo(Strings.VersionControl_RemoteOperationCanceled));
        });
    }

    private static VersionControlTabViewModel CreateViewModel(
        IProjectVersionControlService service,
        IProjectVersionControlCoordinator? coordinator = null)
    {
        return new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            CreateServiceSource(service),
            versionControlCoordinator: coordinator,
            action => action());
    }

    private static ReactivePropertySlim<IProjectVersionControlService?> CreateServiceSource(
        IProjectVersionControlService? service)
    {
        return new ReactivePropertySlim<IProjectVersionControlService?>(service);
    }

    private static Mock<IProjectVersionControlService> CreateServiceMock()
    {
        var service = new Mock<IProjectVersionControlService>();
        service.SetupGet(x => x.Repository)
            .Returns(new RepositoryInfo(
                Path.Combine(Path.GetTempPath(), "beutl-version-control-tab"),
                Path.Combine(Path.GetTempPath(), "beutl-version-control-tab")));
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitAvailability(
                GitAvailabilityState.Installed,
                "git",
                new Version(2, 50, 0),
                LfsInstalled: false));
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return service;
    }

    private static string GetPlatformInstallGuidance()
    {
        return OperatingSystem.IsWindows()
            ? Strings.VersionControl_InstallGitWindows
            : OperatingSystem.IsMacOS()
                ? Strings.VersionControl_InstallGitMacOS
                : Strings.VersionControl_InstallGitLinux;
    }

    private static CommitInfo CreateCommit(int index, SnapshotKind kind)
    {
        string sha = index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture);
        return new CommitInfo(
            sha,
            sha[..7],
            $"commit {index}",
            "Beutl Test",
            new DateTimeOffset(2026, 1, 2, 3, index % 60, 0, TimeSpan.Zero),
            kind);
    }
}
