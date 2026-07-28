using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Moq;

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
            Assert.That(viewModel.IsUnavailable.Value, Is.True);
            Assert.That(viewModel.HasBlockingGuidance.Value, Is.True);
            Assert.That(viewModel.IsTracked.Value, Is.False);
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
            Assert.That(viewModel.IsUnavailable.Value, Is.True);
            Assert.That(viewModel.HasBlockingGuidance.Value, Is.True);
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
            Assert.That(viewModel.IsUnavailable.Value, Is.False);
            Assert.That(viewModel.IsTracked.Value, Is.False);
            Assert.That(viewModel.StatusMessage.Value, Is.EqualTo(Strings.VersionControl_NoRepository));
        });
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
            Assert.That(viewModel.Commits, Has.Count.EqualTo(50));
            Assert.That(viewModel.HasMoreHistory.Value, Is.True);
            Assert.That(viewModel.BranchText.Value, Does.Contain("main"));
            Assert.That(viewModel.AheadBehindText.Value, Does.Contain("2"));
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
            service.Object,
            restoreCoordinator: null,
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
            Assert.That(viewModel.BranchText.Value, Does.Contain("main"));
            Assert.That((object?)pendingUiAction, Is.Not.Null);
        });

        pendingUiAction!();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.BranchText.Value, Does.Contain("external"));
            Assert.That(viewModel.AheadBehindText.Value, Does.Contain("3"));
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
            service.Object,
            restoreCoordinator: null,
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
        await viewModel.SelectCommitAsync(commitViewModel);
        await viewModel.SelectFileAsync(viewModel.ChangedFiles.Single());

        Assert.Multiple(() =>
        {
            Assert.That(commitViewModel.KindText, Is.EqualTo(Strings.VersionControl_SnapshotRestore));
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
        var coordinator = new Mock<IVersionControlRestoreCoordinator>();
        coordinator.Setup(x => x.RestoreToNewBranchAsync(
                commit.Sha,
                "restored-version",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            service.Object,
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

    private static VersionControlTabViewModel CreateViewModel(
        IProjectVersionControlService service)
    {
        return new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(),
            Mock.Of<IEditorContext>(),
            service,
            restoreCoordinator: null,
            action => action());
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
