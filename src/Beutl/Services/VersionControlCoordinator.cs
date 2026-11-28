using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

// Tracks one project with Git: its repository, the backend and watcher that follow it, and the operations that
// change it. A coordinator claims the workspace of the first project that becomes active without one, and tracks
// that project, active or in the background, until it closes; each loaded project thus keeps its own repository
// state and watcher while another project is active (VersionControlCoordinators keeps one per project).
//
// All of the coordinator's state belongs to the UI thread. Entry points called from another thread move
// there once, and every continuation after that stays there; only the Git commands themselves run
// elsewhere, inside the backend.
internal sealed partial class VersionControlCoordinator :
    IProjectVersionControlCoordinator,
    IRepositoryAdoptionConfirmationSource,
    IProjectVersionControlInitializer,
    IProjectVersionControlSession,
    IDisposable,
    IAsyncDisposable
{
    private const string SaveSnapshotMessage = "beutl: snapshot on save";
    private const string CloseSnapshotMessage = "beutl: snapshot on close";
    private const string RestoreSafetySnapshotMessage = "beutl: safety snapshot before restore";
    private const string SwitchSafetySnapshotMessage = "beutl: safety snapshot before switch";
    private const string RestoreRecoveryMessage =
        "beutl: recover original project state after failed restore";

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlConfig _config;
    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<RepositoryInfo?, IProjectVersionControlBackend>? _serviceFactory;
    private readonly Dispatcher _dispatcher;
    private readonly INotificationService _notifications;
    private readonly VersionControlConfirmationPresenter _prompts;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinator>();
    // Held by every operation that changes the repository, the open project or how it is tracked: commits
    // and snapshots, push, remote changes, initialization, restore, branch changes, pull, the stale-lock
    // removal, a configuration change and a project close.
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ReactivePropertySlim<bool> _isGitAvailable = new();
    private readonly ReactivePropertySlim<bool> _isTracked = new();
    private readonly TaskCompletionSource _disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private CoordinatorState _state = CoordinatorState.Empty;
    private EditorWorkspace? _workspace;
    private ActivationContext? _activation;
    private PreparedClose? _close;
    // Canceled when a project close begins, which stops the operation then running and any waiting for the
    // operation gate.
    private CancellationTokenSource _operationEpochCancellation = new();
    // Canceled whenever the open project, its branch or the Git executable changes, which stops a pull that
    // is still checking the remote or waiting for confirmation.
    private CancellationTokenSource _projectServiceEpochCancellation = new();
    // A project created with tracking gets its repository before it opens. The initialized backend
    // waits here until the activation of that same creation adopts it.
    private PreparedNewProject? _preparedNewProject;
    private RepositoryAdoptionRequest? _pendingRepositoryAdoption;
    private object? _latestAvailabilityProbe;
    private TaskCompletionSource? _workDrained;
    private int _runningWork;
    private string? _observedGitExecutablePath;
    private bool _observedUseLfsWhenAvailable;
    private bool _configurationActivationRunning;
    // A changed Git executable waits to rediscover the repository of the untracked project.
    private bool _rediscoveryRequested;
    // A save snapshot is waiting for the operation that held the gate when the project was saved.
    private bool _saveSnapshotDeferred;
    private bool _repositoryHygieneConfigurationDirty;
    private bool _propertiesDisposed;
    private bool _disposed;

    public VersionControlCoordinator(
        ProjectService projectService,
        EditorService editorService)
        : this(
            projectService,
            editorService,
            editorService.AppServices.GetRequiredService<GlobalConfiguration>().VersionControlConfig,
            installationLocator: null,
            serviceFactory: null)
    {
    }

    // Constructed on the UI thread.
    internal VersionControlCoordinator(
        ProjectService projectService,
        EditorService editorService,
        VersionControlConfig config,
        GitInstallationLocator? installationLocator,
        Func<RepositoryInfo?, IProjectVersionControlBackend>? serviceFactory = null)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _observedGitExecutablePath = NormalizeGitExecutablePath(config.GitExecutablePath.Value);
        _observedUseLfsWhenAvailable = config.UseLfsWhenAvailable.Value;
        _installationLocator = installationLocator ?? new GitInstallationLocator(config);
        _serviceFactory = serviceFactory;
        _dispatcher = Dispatcher.UIThread;
        _notifications = editorService.AppServices.GetRequiredService<INotificationService>();
        _prompts = new VersionControlConfirmationPresenter(_dispatcher, _notifications);
        ConfirmRestoreAsync = _prompts.ShowRestoreConfirmationAsync;
        ConfirmSwitchBranchAsync = ShowSwitchBranchConfirmationAsync;
        ConfirmPullAsync = _prompts.ShowPullConfirmationAsync;
        ConfirmUseEnclosingRepositoryAsync = _prompts.ShowEnclosingRepositoryConfirmationAsync;
        ConfirmAdoptExistingRepositoryAsync = ShowAdoptExistingRepositoryConfirmationAsync;
        ConfirmUntrackReservedPathsAsync = _prompts.ShowUntrackReservedPathsConfirmationAsync;
        WarnConflictMarkersAsync = _prompts.ShowConflictMarkerWarningAsync;
        RequestIdentityAsync = static _ => Task.FromResult<GitIdentity?>(null);
        ConfirmCloseWithoutSnapshotAsync = _prompts.ShowCloseWithoutSnapshotConfirmationAsync;
        PresentPolicyNoticeAsync = _prompts.ShowPolicyNoticeAsync;
        _config.ConfigurationChanged += OnVersionControlConfigChanged;
        _projectService.Opening += InspectProjectOpeningAsync;
        _projectService.ClosingPreparing += PrepareProjectClosingAsync;
        _projectService.ClosingFinalizing += NotifyProjectClosingAsync;
        _projectService.TransitionCommitted += OnTransitionCommitted;
        // A project already active when the coordinator starts is tracked at once, unless another coordinator of the
        // editor service tracks it.
        if (_projectService.Sessions.ActiveSession.Value is { } active && TryClaim(active))
        {
            ActivateProject(
                TrackedProject,
                newProject: false,
                preparedNewProject: null,
                CancellationToken.None);
        }

        StartAvailabilityRefresh();
    }

    // Set while the coordinator prepares the version control of a project being created: it then claims only the
    // activation of the created project, and another coordinator claims any other project meanwhile.
    private bool _reservedForNewProject;

    internal bool IsReservedForNewProject => _reservedForNewProject;

    internal void ReserveForNewProject() => _reservedForNewProject = true;

    // Raised once a setup from BeginNewProject is disposed, whether or not its project opened.
    internal event EventHandler? NewProjectSetupEnded;

    private void EndNewProjectReservation()
    {
        _reservedForNewProject = false;
        NewProjectSetupEnded?.Invoke(this, EventArgs.Empty);
    }

    // Raised once the coordinator claims a project, and once it lets go of it because the project closed.
    internal event EventHandler? ProjectClaimed;

    internal event EventHandler? ProjectReleased;

    // The workspace of the tracked project, or null before the coordinator claims one and after it closes.
    internal EditorWorkspace? TrackedWorkspace => _workspace;

    // The loaded model of the tracked project, or null while the coordinator tracks none or the project is closed
    // (as it is in the middle of a restore, a branch switch or a pull).
    internal Project? TrackedProject => _workspace?.Project.Value;

    // Where the work on the tracked project happens. Before a project is claimed, only the creation of one can run,
    // and its work concerns no loaded project.
    private EditorWorkspace Workspace => _workspace ?? _editorService.ShellWorkspace;

    private bool TracksSession(ProjectSession session)
        => _workspace is not null && ReferenceEquals(_editorService.WorkspaceFor(session), _workspace);

    // Starts tracking the project of session, unless this coordinator tracks another one or another coordinator
    // tracks this one.
    private bool TryClaim(ProjectSession session)
    {
        if (_disposed || _workspace is not null)
            return TracksSession(session);

        EditorWorkspace workspace = _editorService.WorkspaceFor(session);
        if (workspace.ProjectFile is null || workspace.VersionControl is not null)
            return false;

        workspace.VersionControl = this;
        _workspace = workspace;
        ProjectClaimed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Lets go of the tracked project once it closed, so the coordinator can be disposed.
    private void ReleaseProject()
    {
        if (_workspace is not { } workspace)
            return;

        workspace.PublishProjectVersionControlService(null);
        workspace.SetRepositoryRoot(null);
        if (ReferenceEquals(workspace.VersionControl, this))
            workspace.VersionControl = null;
        _workspace = null;
        _editorService.ScheduleWorkspaceRelease();
        ProjectReleased?.Invoke(this, EventArgs.Empty);
    }

    public IProjectVersionControlService? CurrentService => _state.VisibleService;

    public IReadOnlyReactiveProperty<bool> IsGitAvailable => _isGitAvailable;

    public IReadOnlyReactiveProperty<bool> IsTracked => _isTracked;

    public event EventHandler? RepositoryAdoptionChanged;

    internal event EventHandler<PushSucceededEventArgs>? PushSucceeded;

    internal sealed record PushSucceededEventArgs(IReadOnlyList<string> PushUrls);

    public RepositoryAdoptionRequest? PendingRepositoryAdoption => _pendingRepositoryAdoption;

    internal Func<CancellationToken, Task<bool>> ConfirmRestoreAsync { get; set; }

    internal Func<string, CancellationToken, Task<bool>> ConfirmSwitchBranchAsync { get; set; }

    internal Func<CancellationToken, Task<bool>> ConfirmPullAsync { get; set; }

    internal Func<RepositoryInfo, CancellationToken, Task<bool>>
        ConfirmUseEnclosingRepositoryAsync
    { get; set; }

    internal Func<RepositoryInfo, CancellationToken, Task<bool>>
        ConfirmAdoptExistingRepositoryAsync
    { get; set; }

    internal Func<IReadOnlyList<string>, CancellationToken, Task<bool>>
        ConfirmUntrackReservedPathsAsync
    { get; set; }

    internal Func<string, Task> WarnConflictMarkersAsync { get; set; }

    internal Func<CancellationToken, Task<bool>> ConfirmCloseWithoutSnapshotAsync { get; set; }

    internal Func<CancellationToken, Task<GitIdentity?>> RequestIdentityAsync { get; set; }

    internal Func<VersionControlPolicyNotice, CancellationToken, Task> PresentPolicyNoticeAsync
    {
        get;
        set;
    }

    public Task<GitAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        return RunOnUiThreadAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetAvailabilityCoreAsync(cancellationToken);
        });
    }

    public INewProjectVersionControlSetup BeginNewProject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new NewProjectSetup(
            this,
            _editorService.BeginLifecycleActivity(ProjectLifecycleActivity.CreatingProject));
    }

    public Task NotifySavedAsync(
        IProjectFileWriteLease? completedWrite = null,
        CancellationToken cancellationToken = default)
    {
        return RunOnUiThreadAsync(async () =>
        {
            if (!_config.AutoCommitOnSave.Value)
            {
                return;
            }

            // The caller still holds its project-file write, which auto-save, the next save, tab
            // closes and imports wait for, so the snapshot must not wait here for an operation such
            // as a push. It runs once that operation has finished instead.
            using OperationLease? operation = TryBeginOperation(cancellationToken);
            if (operation is null)
            {
                DeferSaveSnapshot();
                return;
            }

            await CommitSaveSnapshotAsync(completedWrite, operation.CancellationToken);
        });
    }

    public Task<bool> RestoreAsync(
        string sha,
        CancellationToken cancellationToken = default)
    {
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return RunOnUiThreadAsync(
            () => RunRestoreCycleAsync(sha, branchName: null, cancellationToken));
    }

    public Task<bool> RestoreToNewBranchAsync(
        string sha,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return RunOnUiThreadAsync(
            () => RunRestoreCycleAsync(sha, branchName, cancellationToken));
    }

    public Task<bool> CreateBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return RunOnUiThreadAsync(
            () => RunBranchCycleAsync(branchName.Trim(), create: true, cancellationToken));
    }

    public Task<bool> SwitchBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return RunOnUiThreadAsync(
            () => RunBranchCycleAsync(branchName.Trim(), create: false, cancellationToken));
    }

    public Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return RunOnUiThreadAsync(async () =>
        {
            using OperationLease operation = await BeginOperationAsync(cancellationToken);
            await GetTrackedBackend().SetRemoteAsync(url.Trim(), operation.CancellationToken);
        });
    }

    public Task SetRemoteAsync(
        GitRemoteUrl remote, string? expectedOrigin, CancellationToken cancellationToken)
        => RunOnUiThreadAsync(async () =>
        {
            using OperationLease operation = await BeginOperationAsync(cancellationToken);
            await GetTrackedBackend().SetRemoteAsync(remote, expectedOrigin, operation.CancellationToken);
        });

    public Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        return RunOnUiThreadAsync(async () =>
        {
            RemoteOpResult result;
            using (OperationLease operation = await BeginOperationAsync(cancellationToken))
            {
                result = await GetTrackedBackend().PushAsync(progress, operation.CancellationToken);
            }

            if (result is RemoteOpResult.Success { PushUrls: { Count: > 0 } pushUrls })
            {
                PushSucceeded?.Invoke(this, new PushSucceededEventArgs(pushUrls));
            }

            return result;
        });
    }

    public Task<RemoteOpResult> PullAsync(CancellationToken cancellationToken = default)
        => PullAsync(progress: null, cancellationToken);

    public Task<RemoteOpResult> PullAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return RunOnUiThreadAsync(() => RunPullCycleAsync(progress, cancellationToken));
    }

    public void Dispose()
    {
        if (_dispatcher.CheckAccess())
        {
            BeginDisposal();
        }
        else
        {
            _dispatcher.Post(BeginDisposal, DispatcherPriority.Normal);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_dispatcher.CheckAccess())
        {
            return new ValueTask(_dispatcher.InvokeAsync(
                () => DisposeAsync().AsTask(),
                DispatcherPriority.Normal));
        }

        BeginDisposal();
        return new ValueTask(_disposalCompletion.Task);
    }

    // Moves a caller on another thread to the UI thread, and counts the operation as running work until
    // it finishes.
    private Task RunOnUiThreadAsync(Func<Task> operation)
    {
        return _dispatcher.CheckAccess()
            ? RunAsync()
            : _dispatcher.InvokeAsync(RunAsync, DispatcherPriority.Normal);

        async Task RunAsync()
        {
            using RunningWork work = BeginWork();
            await operation();
        }
    }

    private Task<TResult> RunOnUiThreadAsync<TResult>(Func<Task<TResult>> operation)
    {
        return _dispatcher.CheckAccess()
            ? RunAsync()
            : _dispatcher.InvokeAsync(RunAsync, DispatcherPriority.Normal);

        async Task<TResult> RunAsync()
        {
            using RunningWork work = BeginWork();
            return await operation();
        }
    }
}
