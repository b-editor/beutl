using System.Reactive.Linq;
using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

// The version control of the window's projects: a coordinator for each loaded project, which keeps tracking that
// project's repository with its own watcher while another project is active, and one more waiting to claim the next
// project that becomes active. The window's menus and title bar act on the coordinator of the active project.
internal sealed class VersionControlCoordinators :
    IProjectVersionControlCoordinator,
    IProjectVersionControlSession,
    IProjectVersionControlInitializer,
    IAsyncDisposable,
    IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinators>();
    private readonly EditorService _editorService;
    private readonly Func<VersionControlCoordinator> _create;
    private readonly List<VersionControlCoordinator> _tracking = [];
    // Coordinators preparing a project being created, which claim only that project once it opens.
    private readonly List<VersionControlCoordinator> _creating = [];
    private readonly List<Task> _retirements = [];
    private readonly ReactivePropertySlim<VersionControlCoordinator> _active;
    private readonly IDisposable _activeWorkspaceSubscription;
    private VersionControlCoordinator _standby;
    private Func<CancellationToken, Task<GitIdentity?>>? _requestIdentity;
    private bool _disposed;

    public VersionControlCoordinators(ProjectService projectService, EditorService editorService)
        : this(editorService, () => new VersionControlCoordinator(projectService, editorService))
    {
    }

    // create makes a coordinator for the editor service, which claims the active project if no other one has.
    internal VersionControlCoordinators(EditorService editorService, Func<VersionControlCoordinator> create)
    {
        _editorService = editorService;
        _create = create;
        _standby = Adopt(create());
        _active = new ReactivePropertySlim<VersionControlCoordinator>(_standby);
        // A coordinator created while a project is active claims it at once.
        if (_standby.TrackedWorkspace is not null)
            OnClaimed(_standby, EventArgs.Empty);

        Active = _active;
        IsGitAvailable = _active.Select(coordinator => coordinator.IsGitAvailable).Switch()
            .ToReadOnlyReactivePropertySlim(_standby.IsGitAvailable.Value);
        IsTracked = _active.Select(coordinator => coordinator.IsTracked).Switch()
            .ToReadOnlyReactivePropertySlim(_standby.IsTracked.Value);
        _activeWorkspaceSubscription = editorService.ActiveWorkspace.Subscribe(_ => UpdateActive());
    }

    // The coordinator of the active project, or the one waiting for a project while none is active.
    internal IReadOnlyReactiveProperty<VersionControlCoordinator> Active { get; }

    // The same, read from the active workspace at the moment, so it agrees with the editor service's published
    // version control even while a subscriber of the active workspace is being notified.
    internal VersionControlCoordinator Current
        => _editorService.ActiveWorkspace.Value.VersionControl is VersionControlCoordinator active
           && _tracking.Contains(active)
            ? active
            : _standby;

    // Every coordinator, the one waiting for a project included.
    internal IReadOnlyList<VersionControlCoordinator> All => [.. _tracking, .. _creating, _standby];

    public IReadOnlyReactiveProperty<bool> IsGitAvailable { get; }

    public IReadOnlyReactiveProperty<bool> IsTracked { get; }

    public IProjectVersionControlService? CurrentService => Current.CurrentService;

    // Raised when a push of any project succeeded; the sender is the coordinator that pushed.
    internal event EventHandler<VersionControlCoordinator.PushSucceededEventArgs>? PushSucceeded;

    // Asks for the Git identity a commit needs, for every coordinator of the window.
    internal Func<CancellationToken, Task<GitIdentity?>>? RequestIdentityAsync
    {
        get => _requestIdentity;
        set
        {
            _requestIdentity = value;
            if (value is null)
                return;

            foreach (VersionControlCoordinator coordinator in All)
                coordinator.RequestIdentityAsync = value;
        }
    }

    // A save is snapshotted by the version control of the project it wrote: the one whose files its write reserved,
    // which need not be the active one by the time the save finishes.
    public Task NotifySavedAsync(IProjectFileWriteLease? completedWrite = null, CancellationToken cancellationToken = default)
    {
        if (completedWrite is not null && EditorWorkspace.OwnerOf(completedWrite) is { } workspace)
        {
            return workspace.VersionControlSession is { } session
                ? session.NotifySavedAsync(completedWrite, cancellationToken)
                : Task.CompletedTask;
        }

        return Current.NotifySavedAsync(completedWrite, cancellationToken);
    }

    public Task<CommitResult> CommitManualAsync(string message, CancellationToken cancellationToken)
        => Current.CommitManualAsync(message, cancellationToken);

    public Task<bool> RestoreAsync(string sha, CancellationToken cancellationToken)
        => Current.RestoreAsync(sha, cancellationToken);

    public Task<bool> RestoreToNewBranchAsync(string sha, string branchName, CancellationToken cancellationToken)
        => Current.RestoreToNewBranchAsync(sha, branchName, cancellationToken);

    public Task<bool> CreateBranchAsync(string branchName, CancellationToken cancellationToken)
        => Current.CreateBranchAsync(branchName, cancellationToken);

    public Task<bool> SwitchBranchAsync(string branchName, CancellationToken cancellationToken)
        => Current.SwitchBranchAsync(branchName, cancellationToken);

    public Task SetRemoteAsync(string url, CancellationToken cancellationToken)
        => Current.SetRemoteAsync(url, cancellationToken);

    public Task SetRemoteAsync(GitRemoteUrl remote, string? expectedOrigin, CancellationToken cancellationToken)
        => Current.SetRemoteAsync(remote, expectedOrigin, cancellationToken);

    public Task<RemoteOpResult> PushAsync(IProgress<string>? progress, CancellationToken cancellationToken)
        => Current.PushAsync(progress, cancellationToken);

    public Task<RemoteOpResult> PullAsync(IProgress<string>? progress, CancellationToken cancellationToken)
        => Current.PullAsync(progress, cancellationToken);

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        => Current.GetAvailabilityAsync(cancellationToken);

    public Task<bool> InitializeCurrentProjectAsync(Project expectedProject, CancellationToken cancellationToken = default)
        => Current.InitializeCurrentProjectAsync(expectedProject, cancellationToken);

    // The coordinator waiting for a project prepares the new one and is kept for it, so a project activated before the
    // created one opens is claimed by another coordinator rather than taking the prepared repository away.
    public INewProjectVersionControlSetup BeginNewProject()
    {
        VersionControlCoordinator creator = _standby;
        INewProjectVersionControlSetup setup = creator.BeginNewProject();
        creator.ReserveForNewProject();
        _creating.Add(creator);
        _standby = Adopt(_create());
        UpdateActive();
        return setup;
    }

    // A creation ended; its coordinator stays only if the created project opened and it claimed it.
    private void OnNewProjectSetupEnded(object? sender, EventArgs e)
    {
        if (_disposed
            || sender is not VersionControlCoordinator coordinator
            || coordinator.IsReservedForNewProject
            || !_creating.Remove(coordinator)
            || coordinator.TrackedWorkspace is not null)
        {
            return;
        }

        Forget(coordinator);
        coordinator.Dispose();
    }

    private VersionControlCoordinator Adopt(VersionControlCoordinator coordinator)
    {
        coordinator.ProjectClaimed += OnClaimed;
        coordinator.ProjectReleased += OnReleased;
        coordinator.PushSucceeded += OnPushSucceeded;
        coordinator.NewProjectSetupEnded += OnNewProjectSetupEnded;
        if (_requestIdentity is { } requestIdentity)
            coordinator.RequestIdentityAsync = requestIdentity;
        return coordinator;
    }

    // The coordinator waiting for a project claimed one, so another takes its place.
    private void OnClaimed(object? sender, EventArgs e)
    {
        if (_disposed || sender is not VersionControlCoordinator coordinator)
            return;

        if (_creating.Remove(coordinator))
        {
            _tracking.Add(coordinator);
        }
        else if (ReferenceEquals(coordinator, _standby))
        {
            _tracking.Add(coordinator);
            _standby = Adopt(_create());
        }

        UpdateActive();
    }

    // The project of a coordinator closed, so the coordinator goes once its work is over.
    private void OnReleased(object? sender, EventArgs e)
    {
        if (sender is not VersionControlCoordinator coordinator || !_tracking.Remove(coordinator))
            return;

        Forget(coordinator);
        UpdateActive();
        Task retirement = RetireAsync(coordinator);
        _retirements.Add(retirement);
        _ = retirement.ContinueWith(
            completed => Dispatcher.UIThread.Post(() => _retirements.Remove(completed)),
            TaskScheduler.Default);
    }

    private async Task RetireAsync(VersionControlCoordinator coordinator)
    {
        try
        {
            await coordinator.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose the version control of a closed project.");
        }
    }

    private void Forget(VersionControlCoordinator coordinator)
    {
        coordinator.ProjectClaimed -= OnClaimed;
        coordinator.ProjectReleased -= OnReleased;
        coordinator.PushSucceeded -= OnPushSucceeded;
        coordinator.NewProjectSetupEnded -= OnNewProjectSetupEnded;
    }

    private void OnPushSucceeded(object? sender, VersionControlCoordinator.PushSucceededEventArgs e)
        => PushSucceeded?.Invoke(sender, e);

    private void UpdateActive()
    {
        if (_disposed)
            return;

        _active.Value = Current;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activeWorkspaceSubscription.Dispose();
        foreach (VersionControlCoordinator coordinator in All)
        {
            Forget(coordinator);
            coordinator.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        VersionControlCoordinator[] coordinators = [.. All];
        Task[] retirements = [.. _retirements];
        Dispose();
        foreach (VersionControlCoordinator coordinator in coordinators)
            await coordinator.DisposeAsync();
        await Task.WhenAll(retirements);
    }
}
