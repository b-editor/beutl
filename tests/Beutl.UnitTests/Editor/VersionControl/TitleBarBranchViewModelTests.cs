using System.Globalization;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Moq;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class TitleBarBranchViewModelTests
{
    [Test]
    public void Linked_binding_cancellation_handles_a_disposed_source()
    {
        var source = new CancellationTokenSource();
        CancellationToken token = source.Token;
        source.Dispose();

        CancellationTokenSource? linked = null;
        Assert.DoesNotThrow(() =>
            linked = TitleBarBranchViewModel.TryCreateLinkedCancellation(
                token,
                CancellationToken.None));
        linked?.Dispose();
    }

    [Test]
    public async Task Visibility_tracks_project_repository_and_git_availability()
    {
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(null);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            coordinator.Object,
            action => action());
        await viewModel.Initialization;
        Assert.That(viewModel.IsVisible.Value, Is.False, "No project is open.");

        Mock<IProjectVersionControlService> untracked = CreateServiceMock();
        untracked.SetupGet(service => service.Repository)
            .Returns((RepositoryInfo?)null);
        serviceSource.Value = untracked.Object;
        await viewModel.Initialization;
        Assert.That(
            viewModel.IsVisible.Value,
            Is.False,
            "An untracked project must not show the widget.");

        Mock<IProjectVersionControlService> unavailable = CreateServiceMock();
        unavailable.Setup(service => service.GetAvailabilityAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitAvailability.NotInstalled);
        serviceSource.Value = unavailable.Object;
        await viewModel.Initialization;
        Assert.That(
            viewModel.IsVisible.Value,
            Is.False,
            "A tracked project without an available Git installation must not show the widget.");

        Mock<IProjectVersionControlService> tracked = CreateServiceMock();
        serviceSource.Value = tracked.Object;
        await viewModel.Initialization;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsVisible.Value, Is.True);
            Assert.That(viewModel.DisplayText.Value, Is.EqualTo("main"));
            Assert.That(viewModel.Branches, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Same_service_becomes_visible_when_its_repository_is_initialized()
    {
        RepositoryInfo? repository = null;
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.SetupGet(item => item.Repository)
            .Returns(() => repository);
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;
        Assert.That(viewModel.IsVisible.Value, Is.False);

        string root = Path.Combine(
            Path.GetTempPath(),
            "beutl-title-bar-branch-initialized");
        repository = new RepositoryInfo(root, root);
        service.Setup(item => item.GetStatusAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 1, 2, [], false));
        service.Raise(
            item => item.StatusChanged += null,
            service.Object,
            new WorkspaceStatus("main", 1, 2, [], false));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsVisible.Value, Is.True);
            Assert.That(viewModel.DisplayText.Value, Is.EqualTo("main ↑1 ↓2"));
            Assert.That(viewModel.CurrentBranchName.Value, Is.EqualTo("main"));
            Assert.That(viewModel.AheadCount.Value, Is.EqualTo(1));
            Assert.That(viewModel.BehindCount.Value, Is.EqualTo(2));
            Assert.That(viewModel.HasAhead.Value, Is.True);
            Assert.That(viewModel.HasBehind.Value, Is.True);
        });
    }

    [Test]
    public async Task Same_service_tracks_coordinator_git_availability_changes()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var gitAvailabilitySource = CreateGitAvailabilitySource(false);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            gitAvailabilitySource,
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;
        Assert.That(viewModel.IsVisible.Value, Is.False);

        gitAvailabilitySource.Value = true;
        Assert.That(viewModel.IsVisible.Value, Is.True);

        gitAvailabilitySource.Value = false;
        Assert.That(viewModel.IsVisible.Value, Is.False);
    }

    [Test]
    public async Task Refresh_clears_stale_state_when_coordinator_reports_git_unavailable()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var gitAvailabilitySource = CreateGitAvailabilitySource(false);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            gitAvailabilitySource,
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;
        viewModel.IsVisible.Value = true;
        viewModel.DisplayText.Value = "stale";
        viewModel.CurrentBranchName.Value = "stale";

        await viewModel.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsVisible.Value, Is.False);
            Assert.That(viewModel.DisplayText.Value, Is.Empty);
            Assert.That(viewModel.CurrentBranchName.Value, Is.Empty);
            Assert.That(viewModel.Branches, Is.Empty);
        });
    }

    [Test]
    public async Task Service_publication_is_marshaled_before_rebinding_ui_state()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(null);
        Action? pendingUiAction = null;
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => pendingUiAction = action);
        await viewModel.Initialization;

        await Task.Run(() => serviceSource.Value = service.Object);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsVisible.Value, Is.False);
            Assert.That((object?)pendingUiAction, Is.Not.Null);
        });

        Action rebindAction = pendingUiAction!;
        pendingUiAction = null;
        rebindAction();
        await viewModel.Initialization;
        Assert.That((object?)pendingUiAction, Is.Not.Null);
        pendingUiAction!();

        Assert.That(viewModel.IsVisible.Value, Is.True);
    }

    [Test]
    public async Task Publication_between_initial_read_and_subscription_is_not_lost()
    {
        Mock<IProjectVersionControlService> serviceA = CreateServiceMock();
        Mock<IProjectVersionControlService> serviceB = CreateServiceMock();
        serviceB.Setup(item => item.GetStatusAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("project-b", 0, 0, [], false));
        var serviceSource =
            new Mock<IReadOnlyReactiveProperty<IProjectVersionControlService?>>();
        serviceSource.SetupGet(item => item.Value)
            .Returns(serviceA.Object);
        serviceSource.Setup(item => item.Subscribe(
                It.IsAny<IObserver<IProjectVersionControlService?>>()))
            .Callback<IObserver<IProjectVersionControlService?>>(
                observer => observer.OnNext(serviceB.Object))
            .Returns(Mock.Of<IDisposable>());

        using var viewModel = new TitleBarBranchViewModel(
            serviceSource.Object,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;

        Assert.That(viewModel.DisplayText.Value, Is.EqualTo("project-b"));
        serviceA.VerifyRemove(
            item => item.StatusChanged -= It.IsAny<EventHandler<WorkspaceStatus>>(),
            Times.Once);
    }

    [Test]
    public async Task Refresh_replaces_the_branch_list_with_current_repository_data()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.SetupSequence(item => item.GetBranchesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BranchInfo("main", true, null),
                new BranchInfo("old", false, null),
            ])
            .ReturnsAsync(
            [
                new BranchInfo("main", true, null),
                new BranchInfo("feature", false, null),
            ]);
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;

        await viewModel.RefreshAsync();

        Assert.That(
            viewModel.Branches.Select(branch => branch.Name),
            Is.EqualTo(["main", "feature"]));
        service.Verify(
            item => item.GetBranchesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task Refresh_does_not_overwrite_a_newer_status_event()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        var branchReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBranchRead = new TaskCompletionSource<IReadOnlyList<BranchInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.SetupSequence(item => item.GetStatusAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false))
            .ReturnsAsync(new WorkspaceStatus("stale", 1, 0, [], false));
        service.SetupSequence(item => item.GetBranchesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BranchInfo("main", true, null)])
            .Returns(() =>
            {
                branchReadStarted.TrySetResult();
                return releaseBranchRead.Task;
            });
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;

        Task refresh = viewModel.RefreshAsync();
        await branchReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Raise(
            item => item.StatusChanged += null,
            service.Object,
            new WorkspaceStatus("fresh", 0, 2, [], false));
        releaseBranchRead.SetResult([new BranchInfo("stale", true, null)]);
        await refresh;

        Assert.That(viewModel.DisplayText.Value, Is.EqualTo("fresh ↓2"));
    }

    [Test]
    public async Task Preparing_the_flyout_refreshes_branches()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;

        await viewModel.PrepareFlyoutAsync();

        Assert.That(viewModel.Branches, Has.Count.EqualTo(2));
        service.Verify(
            item => item.GetBranchesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task Replacing_the_service_detaches_status_from_the_previous_project()
    {
        Mock<IProjectVersionControlService> serviceA = CreateServiceMock();
        Mock<IProjectVersionControlService> serviceB = CreateServiceMock();
        serviceB.Setup(item => item.GetStatusAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("project-b", 0, 0, [], false));
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(serviceA.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => action());
        await viewModel.Initialization;

        serviceSource.Value = serviceB.Object;
        await viewModel.Initialization;
        serviceA.Raise(
            item => item.StatusChanged += null,
            serviceA.Object,
            new WorkspaceStatus("stale-project-a", 9, 9, [], false));

        Assert.That(viewModel.DisplayText.Value, Is.EqualTo("project-b"));

        serviceB.Raise(
            item => item.StatusChanged += null,
            serviceB.Object,
            new WorkspaceStatus("project-b", 2, 1, [], false));

        Assert.That(viewModel.DisplayText.Value, Is.EqualTo("project-b ↑2 ↓1"));
        serviceA.VerifyRemove(
            item => item.StatusChanged -= It.IsAny<EventHandler<WorkspaceStatus>>(),
            Times.Once);
    }

    [Test]
    public async Task Switching_a_branch_routes_through_the_coordinator_and_refreshes_state()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        service.SetupSequence(item => item.GetStatusAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false))
            .ReturnsAsync(new WorkspaceStatus("feature", 1, 0, [], false));
        service.SetupSequence(item => item.GetBranchesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BranchInfo("main", true, null),
                new BranchInfo("feature", false, null),
            ])
            .ReturnsAsync(
            [
                new BranchInfo("main", false, null),
                new BranchInfo("feature", true, null),
            ]);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(item => item.SwitchBranchAsync(
                "feature",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            coordinator.Object,
            action => action());
        await viewModel.Initialization;

        await viewModel.SwitchBranchAsync("feature");

        coordinator.Verify(
            item => item.SwitchBranchAsync(
                "feature",
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsBusy.Value, Is.False);
            Assert.That(viewModel.DisplayText.Value, Is.EqualTo("feature ↑1"));
            Assert.That(
                viewModel.Branches.Single(branch => branch.Name == "feature")
                    .IsCurrent,
                Is.True);
        });
    }

    [Test]
    public async Task Disposal_cancels_an_in_flight_branch_cycle()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(item => item.SwitchBranchAsync(
                "feature",
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return false;
            });
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            coordinator.Object,
            action => action());
        await viewModel.Initialization;

        Task operation = viewModel.SwitchBranchAsync("feature");
        await started.Task;
        viewModel.Dispose();
        await operation;

        coordinator.Verify(
            item => item.SwitchBranchAsync(
                "feature",
                It.Is<CancellationToken>(token => token.IsCancellationRequested)),
            Times.Once);
    }

    [Test]
    public async Task Disposal_ignores_queued_service_publication_and_refresh()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(null);
        Action? pendingUiAction = null;
        var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            Mock.Of<IProjectVersionControlCoordinator>(),
            action => pendingUiAction = action);
        await viewModel.Initialization;

        serviceSource.Value = service.Object;
        Assert.That((object?)pendingUiAction, Is.Not.Null);

        viewModel.Dispose();
        pendingUiAction!();

        Assert.DoesNotThrowAsync(async () => await viewModel.RefreshAsync());
        Assert.DoesNotThrowAsync(async () => await viewModel.PrepareFlyoutAsync());
        Assert.That(viewModel.IsVisible.Value, Is.False);
    }

    [Test]
    public async Task Creating_a_branch_uses_the_existing_coordinator_flow()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.Setup(item => item.CreateBranchAsync(
                "experiment",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            coordinator.Object,
            action => action())
        {
            RequestNewBranchNameAsync =
                () => Task.FromResult<string?>(" experiment "),
        };
        await viewModel.Initialization;

        await viewModel.CreateBranchAsync();

        coordinator.Verify(
            item => item.CreateBranchAsync(
                "experiment",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Disposal_during_branch_name_prompt_prevents_creation()
    {
        Mock<IProjectVersionControlService> service = CreateServiceMock();
        var branchName = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            coordinator.Object,
            action => action())
        {
            RequestNewBranchNameAsync = () => branchName.Task,
        };
        await viewModel.Initialization;

        Task operation = viewModel.CreateBranchAsync();
        viewModel.Dispose();
        branchName.SetResult("late-branch");
        await operation;

        coordinator.Verify(
            item => item.CreateBranchAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Rebind_during_branch_name_prompt_prevents_creation()
    {
        Mock<IProjectVersionControlService> originatingService = CreateServiceMock();
        Mock<IProjectVersionControlService> replacementService = CreateServiceMock();
        var branchName = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var promptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        using var serviceSource =
            new ReactivePropertySlim<IProjectVersionControlService?>(originatingService.Object);
        using var viewModel = new TitleBarBranchViewModel(
            serviceSource,
            CreateGitAvailabilitySource(),
            coordinator.Object,
            action => action())
        {
            RequestNewBranchNameAsync = () =>
            {
                promptStarted.TrySetResult();
                return branchName.Task;
            },
        };
        await viewModel.Initialization;

        Task operation = viewModel.CreateBranchAsync();
        await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        serviceSource.Value = replacementService.Object;
        await viewModel.Initialization;
        branchName.SetResult("late-branch");
        await operation;

        coordinator.Verify(
            item => item.CreateBranchAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("main", 0, 0, "main")]
    [TestCase("main", 2, 0, "main ↑2")]
    [TestCase("main", 0, 1, "main ↓1")]
    [TestCase("main", 2, 1, "main ↑2 ↓1")]
    public void Ahead_and_behind_counts_use_compact_arrow_format(
        string branchName,
        int ahead,
        int behind,
        string expected)
    {
        Assert.That(
            TitleBarBranchViewModel.FormatDisplayText(
                branchName,
                ahead,
                behind,
                CultureInfo.InvariantCulture),
            Is.EqualTo(expected));
    }

    private static Mock<IProjectVersionControlService> CreateServiceMock()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "beutl-title-bar-branch-tests");
        var service = new Mock<IProjectVersionControlService>();
        service.SetupGet(item => item.Repository)
            .Returns(new RepositoryInfo(root, root));
        service.Setup(item => item.GetAvailabilityAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitAvailability(
                GitAvailabilityState.Installed,
                "git",
                new Version(2, 50, 0),
                LfsInstalled: false));
        service.Setup(item => item.GetStatusAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(item => item.GetBranchesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BranchInfo("main", true, null),
                new BranchInfo("feature", false, null),
            ]);
        return service;
    }

    private static ReactivePropertySlim<bool> CreateGitAvailabilitySource(
        bool value = true)
    {
        return new ReactivePropertySlim<bool>(value);
    }
}
