using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class VersionControlCoordinator :
    IProjectVersionControlCoordinator,
    IProjectVersionControlInitializer,
    IDisposable
{
    private const string SaveSnapshotMessage = "beutl: snapshot on save";
    private const string CloseSnapshotMessage = "beutl: snapshot on close";
    private const string RestoreSafetySnapshotMessage = "beutl: safety snapshot before restore";
    private const string SwitchSafetySnapshotMessage = "beutl: safety snapshot before switch";
    private const string PullSafetySnapshotMessage = "beutl: safety snapshot before pull";
    private const string RestoreRecoveryMessage =
        "beutl: recover original project state after failed restore";

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlConfig _config;
    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<RepositoryInfo?, IProjectVersionControlBackend>? _serviceFactory;
    private readonly IDisposable _projectSubscription;
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinator>();
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _lockRecoveryGate = new(1, 1);
    private readonly ReactivePropertySlim<bool> _isGitAvailable = new();
    private readonly ReactivePropertySlim<bool> _isTracked = new();
    private readonly Queue<StatePublication> _publicationQueue = new();
    private CoordinatorState _state = CoordinatorState.Empty;
    private ActivationContext? _activation;
    private long _nextActivationRevision;
    private long _nextStateRevision;
    private long _lastPublishedRevision;
    private int _availabilityRevision;
    private int _lifecycleUsers;
    private bool _publicationDrainScheduled;
    private bool _publicationDrainRunning;
    private bool _disposePropertiesRequested;
    private bool _propertiesDisposed;
    private volatile bool _disposed;

    public VersionControlCoordinator(
        ProjectService projectService,
        EditorService editorService)
        : this(
            projectService,
            editorService,
            GlobalConfiguration.Instance.VersionControlConfig,
            installationLocator: null,
            serviceFactory: null)
    {
    }

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
        _installationLocator = installationLocator ?? new GitInstallationLocator(config);
        _serviceFactory = serviceFactory;
        ConfirmRestoreAsync = ShowRestoreConfirmationAsync;
        ConfirmSwitchBranchAsync = ShowSwitchBranchConfirmationAsync;
        ConfirmPullAsync = ShowPullConfirmationAsync;
        ConfirmUseEnclosingRepositoryAsync = ShowEnclosingRepositoryConfirmationAsync;
        ConfirmRemoveStaleLockAsync = ShowStaleLockConfirmationAsync;
        WarnConflictMarkersAsync = ShowConflictMarkerWarningAsync;
        RequestIdentityAsync = static _ => Task.FromResult<GitIdentity?>(null);
        PresentPolicyNoticeAsync = ShowPolicyNoticeAsync;
        _config.ConfigurationChanged += OnVersionControlConfigChanged;
        _projectService.Opening += WarnBeforeOpeningConflictedProjectAsync;
        _projectService.Closing += NotifyClosingAsync;
        _projectSubscription = _projectService.ProjectObservable.Subscribe(
            change => OnProjectChanged(change.New));
        _editorService.ProjectVersionControlCoordinator = this;
        _ = RefreshAvailabilityAsync();

        if (_projectService.CurrentProject.Value is { } project)
        {
            OnProjectChanged(project);
        }
    }

    public IProjectVersionControlService? CurrentService
    {
        get
        {
            lock (_stateGate)
            {
                return _state.VisibleService;
            }
        }
    }

    public IReadOnlyReactiveProperty<bool> IsGitAvailable => _isGitAvailable;

    public IReadOnlyReactiveProperty<bool> IsTracked => _isTracked;

    internal Func<CancellationToken, Task<bool>> ConfirmRestoreAsync { get; set; }

    internal Func<string, CancellationToken, Task<bool>> ConfirmSwitchBranchAsync { get; set; }

    internal Func<CancellationToken, Task<bool>> ConfirmPullAsync { get; set; }

    internal Func<RepositoryInfo, CancellationToken, Task<bool>>
        ConfirmUseEnclosingRepositoryAsync
    { get; set; }

    internal Func<RepositoryLockInfo, CancellationToken, Task<bool>>
        ConfirmRemoveStaleLockAsync
    { get; set; }

    internal Func<string, Task> WarnConflictMarkersAsync { get; set; }

    internal Func<CancellationToken, Task<GitIdentity?>> RequestIdentityAsync { get; set; }

    internal Func<VersionControlPolicyNotice, CancellationToken, Task> PresentPolicyNoticeAsync
    {
        get;
        set;
    }

    public async Task<GitAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int revision = Interlocked.Increment(ref _availabilityRevision);
        GitAvailability availability = await _installationLocator.LocateAsync(cancellationToken);
        bool schedulePublication = false;
        lock (_stateGate)
        {
            if (!_disposed && revision == Volatile.Read(ref _availabilityRevision))
            {
                schedulePublication = TransitionStateLocked(
                    _state with
                    {
                        IsGitAvailable = availability.State == GitAvailabilityState.Installed,
                    });
            }
        }

        SchedulePublicationDrain(schedulePublication);

        return availability;
    }

    public async Task<bool> InitializeCurrentProjectAsync(
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestIdentityAsync);

        Project project = _projectService.CurrentProject.Value
                          ?? throw new InvalidOperationException("No project is open.");
        string projectRoot = GetProjectRoot(project);
        Task activationTask;
        lock (_stateGate)
        {
            activationTask = _activation is { ProjectRoot: var activationRoot } activation
                             && string.Equals(activationRoot, projectRoot, PathComparison)
                ? activation.Completion
                : Task.CompletedTask;
        }

        await activationTask.WaitAsync(cancellationToken);

        IProjectVersionControlBackend service;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Project currentProject = _projectService.CurrentProject.Value
                                     ?? throw new InvalidOperationException(
                                         "The project was closed while version control was activating.");
            string currentRoot = GetProjectRoot(currentProject);
            if (!ReferenceEquals(currentProject, project)
                || !string.Equals(currentRoot, projectRoot, PathComparison)
                || _state.ProjectRoot is not { } stateRoot
                || !string.Equals(stateRoot, projectRoot, PathComparison))
            {
                throw new InvalidOperationException(
                    "The open project changed while version control was activating.");
            }

            service = _state.OwnedService
                      ?? throw new InvalidOperationException(
                          "The version control service is not available.");
        }

        RepositoryInfo? targetRepository = service.Repository
                                           ?? await SelectRepositoryForInitializationAsync(
                                               service,
                                               projectRoot,
                                               cancellationToken);
        if (targetRepository is null)
        {
            return false;
        }

        var options = new InitOptions(targetRepository, _config.UseLfsWhenAvailable);
        try
        {
            try
            {
                await service.InitializeAsync(options, cancellationToken);
            }
            catch (GitIdentityRequiredException)
            {
                GitIdentity? identity = await requestIdentityAsync(cancellationToken);
                if (identity is null)
                {
                    return false;
                }

                await service.SetLocalIdentityAsync(identity, cancellationToken);
                await service.InitializeAsync(options, cancellationToken);
            }
        }
        catch (VersionControlConflictedException ex)
        {
            NotificationService.ShowWarning(Strings.VersionControl, ex.Guidance);
            return false;
        }

        bool schedulePublication;
        lock (_stateGate)
        {
            if (_disposed
                || !ReferenceEquals(_state.OwnedService, service)
                || _state.ProjectRoot is not { } stateRoot
                || !string.Equals(stateRoot, projectRoot, PathComparison))
            {
                throw new InvalidOperationException(
                    "The open project changed while version control was being initialized.");
            }

            schedulePublication = TransitionStateLocked(
                _state with { IsTracked = service.Repository is not null });
        }

        SchedulePublicationDrain(schedulePublication);

        return true;
    }

    public Task NotifySavedAsync(CancellationToken cancellationToken = default)
    {
        return CommitSnapshotAsync(
            _config.AutoCommitOnSave,
            SaveSnapshotMessage,
            SnapshotKind.Save,
            cancellationToken);
    }

    public async Task NotifyClosingAsync(CancellationToken cancellationToken = default)
    {
        if (IsInternalVersionControlTransition())
        {
            return;
        }

        ActivationContext? activation;
        string? projectRoot;
        long activationRevision;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            activation = _activation;
            projectRoot = _state.ProjectRoot;
            activationRevision = _nextActivationRevision;
        }

        if (activation is not null)
        {
            await activation.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Task retirement;
            lock (_stateGate)
            {
                if (_disposed
                    || projectRoot is null
                    || activationRevision != _nextActivationRevision
                    || _state.ProjectRoot is not { } currentRoot
                    || !string.Equals(currentRoot, projectRoot, PathComparison))
                {
                    return;
                }

                IProjectVersionControlBackend? service = _state.OwnedService;
                if (service is null)
                {
                    return;
                }

                ProjectVersionControlFinalSnapshot? finalSnapshot =
                    _config.AutoCommitOnClose
                        ? new ProjectVersionControlFinalSnapshot(
                            CloseSnapshotMessage,
                            SnapshotKind.Close)
                        : null;
                retirement = service.RetireAsync(finalSnapshot);
            }

            await retirement.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire version control while closing the project.");
        }
    }

    public async Task CloseCurrentProjectAsync(CancellationToken cancellationToken = default)
    {
        await _projectService.CloseProject(cancellationToken);
    }

    public Task<bool> RestoreAsync(
        string sha,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        return RunRestoreCycleAsync(sha, branchName: null, cancellationToken);
    }

    public Task<bool> RestoreToNewBranchAsync(
        string sha,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return RunRestoreCycleAsync(sha, branchName, cancellationToken);
    }

    public async Task<CommitResult> CommitManualAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IProjectVersionControlBackend service = GetTrackedBackend();
        try
        {
            return await service.CommitAllAsync(
                message.Trim(),
                SnapshotKind.Manual,
                cancellationToken);
        }
        catch (GitIdentityRequiredException)
        {
            GitIdentity? identity = await RequestIdentityAsync(cancellationToken);
            if (identity is null)
            {
                throw;
            }

            await service.SetLocalIdentityAsync(identity, cancellationToken);
            return await service.CommitAllAsync(
                message.Trim(),
                SnapshotKind.Manual,
                cancellationToken);
        }
    }

    public Task<bool> CreateBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return RunBranchCycleAsync(branchName.Trim(), create: true, cancellationToken);
    }

    public Task<bool> SwitchBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return RunBranchCycleAsync(branchName.Trim(), create: false, cancellationToken);
    }

    public Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return GetTrackedBackend().SetRemoteAsync(url.Trim(), cancellationToken);
    }

    public Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return GetTrackedBackend().SetLocalIdentityAsync(identity, cancellationToken);
    }

    public Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        return GetTrackedBackend().PushAsync(progress, cancellationToken);
    }

    public Task<RemoteOpResult> PullAsync(CancellationToken cancellationToken = default)
    {
        return RunPullCycleAsync(cancellationToken);
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _config.ConfigurationChanged -= OnVersionControlConfigChanged;
        _projectService.Opening -= WarnBeforeOpeningConflictedProjectAsync;
        _projectService.Closing -= NotifyClosingAsync;
        _projectSubscription.Dispose();
        if (ReferenceEquals(_editorService.ProjectVersionControlCoordinator, this))
        {
            _editorService.ProjectVersionControlCoordinator = null;
        }

        if (Volatile.Read(ref _lifecycleUsers) == 0)
        {
            ClearProjectState();
        }
        else
        {
            SetVisibleService(null);
        }

        DisposePublishedProperties();
    }

    private async Task<bool> RunBranchCycleAsync(
        string branchName,
        bool create,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _lifecycleUsers);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return false;
            }

            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            return await ownedService.ExecuteExclusiveAsync(
                async service =>
                {
                    WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                    if (!EnsureRepositoryIsNotConflicted(status))
                    {
                        return false;
                    }

                    if (!create
                        && string.Equals(status.Branch, branchName, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (!await ConfirmSwitchBranchAsync(branchName, cancellationToken))
                    {
                        return false;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    CheckedOutBranchTip originalTip = await service.GetCheckedOutBranchTipAsync(
                        CancellationToken.None);
                    if (!status.IsClean)
                    {
                        CommitResult result = await service.CommitAllAsync(
                            SwitchSafetySnapshotMessage,
                            SnapshotKind.Safety,
                            CancellationToken.None);
                        EnsureAutomaticSnapshotWasNotSkipped(result);
                        CheckedOutBranchTip committedTip = await service.GetCheckedOutBranchTipAsync(
                            CancellationToken.None);
                        originalTip = GetExpectedTipAfterCommitAll(
                            originalTip,
                            result,
                            committedTip);
                        if (!BranchTipsEqual(committedTip, originalTip))
                        {
                            throw new InvalidOperationException(
                                "The branch ref changed while the switch safety snapshot was committed.");
                        }
                    }

                    CheckedOutBranchTip expectedResultTip = originalTip;
                    bool projectClosed = false;
                    try
                    {
                        await CloseProjectForOperationAsync(transition, CancellationToken.None);
                        projectClosed = true;
                        try
                        {
                            if (create)
                            {
                                await service.CreateBranchAsync(
                                    branchName,
                                    originalTip.Commit,
                                    CancellationToken.None);
                            }
                            else
                            {
                                await service.SwitchBranchAsync(
                                    branchName,
                                    CancellationToken.None);
                            }
                        }
                        catch
                        {
                            expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                                CancellationToken.None);
                            throw;
                        }

                        expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                            CancellationToken.None);
                        await ReopenProjectAsync(transition, projectFile);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Exception? recoveryFailure = projectClosed
                            ? await TryRestoreOriginalStateAsync(
                                service,
                                originalTip,
                                expectedResultTip,
                                RecoveryKind.Branch,
                                transition,
                                projectFile)
                            : null;
                        return HandleCycleFailure(
                            ex,
                            recoveryFailure,
                            $"branch '{branchName}'",
                            cancellationToken);
                    }
                    finally
                    {
                        FinishInternalTransition();
                    }
                },
                cancellationToken);
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private async Task<RemoteOpResult> RunPullCycleAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _lifecycleUsers);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return new RemoteOpResult.Failed(Strings.VersionControl_ExportInProgress);
            }

            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            return await ownedService.ExecuteExclusiveAsync(
                async service =>
                {
                    WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                    if (!EnsureRepositoryIsNotConflicted(status))
                    {
                        return new RemoteOpResult.Failed(Strings.VersionControl_ConflictGuidance);
                    }

                    if (!await ConfirmPullAsync(cancellationToken))
                    {
                        return new RemoteOpResult.Failed(string.Empty);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    CheckedOutBranchTip originalHead =
                        await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
                    ProjectCheckpoint? checkpoint = status.IsClean
                        ? null
                        : await service.CreateProjectCheckpointAsync(
                            PullSafetySnapshotMessage,
                            CancellationToken.None);
                    bool projectClosed = false;
                    CheckedOutBranchTip expectedCurrentHead = originalHead;
                    PullTransitionState pullTransitionState = PullTransitionState.Unchanged;
                    try
                    {
                        await CloseProjectForOperationAsync(transition, CancellationToken.None);
                        projectClosed = true;
                        FastForwardPullResult pull = await service.PullFastForwardAsync(
                            originalHead,
                            checkpoint,
                            CancellationToken.None);

                        RemoteOpResult result = pull.Result;
                        expectedCurrentHead = pull.Tip;
                        pullTransitionState = pull.TransitionState;
                        if (pullTransitionState is PullTransitionState.OwnershipLost
                            or PullTransitionState.RecoveryFailed)
                        {
                            return new RemoteOpResult.Failed(
                                Strings.VersionControl_PullTransitionUncertain);
                        }

                        if (result is not RemoteOpResult.Success)
                        {
                            Exception? recoveryFailure = await TryRecoverPullAsync(
                                service,
                                originalHead,
                                expectedCurrentHead,
                                checkpoint,
                                transition,
                                projectFile);
                            if (recoveryFailure is not null)
                            {
                                return new RemoteOpResult.Failed(string.Format(
                                    Strings.VersionControl_RecoveryFailed,
                                    GetRemoteOperationError(result),
                                    GetErrorText(recoveryFailure)));
                            }

                            await TryDeleteCheckpointAsync(service, checkpoint);
                            return result;
                        }

                        CheckedOutBranchTip verifiedHead =
                            await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
                        if (!BranchTipsEqual(verifiedHead, expectedCurrentHead))
                        {
                            throw new InvalidOperationException(
                                "The repository ref changed before the pulled project could be reopened.");
                        }

                        await ReopenProjectAsync(transition, projectFile);
                        await TryDeleteCheckpointAsync(service, checkpoint);
                        return new RemoteOpResult.Success();
                    }
                    catch (Exception ex)
                    {
                        if (projectClosed
                            && pullTransitionState is PullTransitionState.OwnershipLost
                                or PullTransitionState.RecoveryFailed)
                        {
                            _logger.LogError(
                                ex,
                                "The pull transition became uncertain after the project was closed.");
                            return new RemoteOpResult.Failed(
                                Strings.VersionControl_PullTransitionUncertain);
                        }

                        Exception? recoveryFailure = projectClosed
                            ? await TryRecoverPullAsync(
                                service,
                                originalHead,
                                expectedCurrentHead,
                                checkpoint,
                                transition,
                                projectFile)
                            : null;
                        if (ex is OperationCanceledException
                            && cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        if (projectClosed && recoveryFailure is null)
                        {
                            await TryDeleteCheckpointAsync(service, checkpoint);
                        }

                        if (recoveryFailure is not null)
                        {
                            HandleCycleFailure(
                                ex,
                                recoveryFailure,
                                "pull",
                                cancellationToken);
                            return new RemoteOpResult.Failed(string.Format(
                                Strings.VersionControl_RecoveryFailed,
                                GetErrorText(ex),
                                GetErrorText(recoveryFailure)));
                        }

                        _logger.LogError(ex, "Failed to pull project versions.");
                        return new RemoteOpResult.Failed(GetErrorText(ex));
                    }
                    finally
                    {
                        FinishInternalTransition();
                    }
                },
                cancellationToken);
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private async Task<Exception?> TryRecoverPullAsync(
        IProjectVersionControlTransaction service,
        CheckedOutBranchTip originalHead,
        CheckedOutBranchTip expectedCurrentHead,
        ProjectCheckpoint? checkpoint,
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        try
        {
            CheckedOutBranchTip actualHead = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
            if (!BranchTipsEqual(actualHead, expectedCurrentHead))
            {
                throw new InvalidOperationException(
                    "The repository HEAD changed while the pull was being recovered.");
            }

            if (!BranchTipsEqual(actualHead, originalHead))
            {
                BranchTipRollbackResult rollback = await service.TryRollbackBranchTipAsync(
                    expectedCurrentHead,
                    originalHead,
                    CancellationToken.None);
                switch (rollback)
                {
                    case BranchTipRollbackResult.RolledBack:
                        break;
                    case BranchTipRollbackResult.RefChanged changed:
                        throw new InvalidOperationException(
                            $"The repository ref changed to '{changed.ActualCommit}' while the pull was being recovered.");
                    case BranchTipRollbackResult.UnsafeRepositoryState:
                        throw new InvalidOperationException(
                            "The repository contains changes that prevent a safe pull rollback.");
                }
            }

            if (checkpoint is not null)
            {
                await service.RestoreProjectCheckpointAsync(
                    checkpoint,
                    CancellationToken.None);
            }

            CheckedOutBranchTip recoveredHead = await service.GetCheckedOutBranchTipAsync(
                CancellationToken.None);
            if (!BranchTipsEqual(recoveredHead, originalHead))
            {
                throw new InvalidOperationException(
                    "The repository ref changed after the pull state was restored.");
            }

            await ReopenProjectAsync(transition, projectFile);
            return null;
        }
        catch (Exception recoveryException)
        {
            return recoveryException;
        }
    }

    private async Task TryDeleteCheckpointAsync(
        IProjectVersionControlTransaction service,
        ProjectCheckpoint? checkpoint)
    {
        if (checkpoint is null)
        {
            return;
        }

        try
        {
            await service.DeleteProjectCheckpointAsync(
                checkpoint,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete completed project checkpoint {CheckpointRef}.",
                checkpoint.RefName);
        }
    }

    private static bool BranchTipsEqual(CheckedOutBranchTip left, CheckedOutBranchTip right)
    {
        return string.Equals(left.RefName, right.RefName, StringComparison.Ordinal)
               && string.Equals(left.Commit, right.Commit, StringComparison.OrdinalIgnoreCase);
    }

    private static CheckedOutBranchTip GetExpectedTipAfterCommit(
        CheckedOutBranchTip previousTip,
        CommitResult result)
    {
        return result switch
        {
            CommitResult.Committed { Revision: CommitRevision.Known revision }
                => new CheckedOutBranchTip(
                    previousTip.RefName,
                    revision.Sha),
            CommitResult.NoChanges or CommitResult.SkippedNoIdentity => previousTip,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static CheckedOutBranchTip GetExpectedTipAfterCommitAll(
        CheckedOutBranchTip previousTip,
        CommitResult result,
        CheckedOutBranchTip observedTip)
    {
        if (result is CommitResult.Committed { Revision: CommitRevision.Unavailable })
        {
            if (!string.Equals(
                    observedTip.RefName,
                    previousTip.RefName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The checked-out branch changed while the snapshot commit revision was resolved.");
            }

            return observedTip;
        }

        return GetExpectedTipAfterCommit(previousTip, result);
    }

    private static string GetRemoteOperationError(RemoteOpResult result)
    {
        return result switch
        {
            RemoteOpResult.AuthFailed failed => failed.Guidance,
            RemoteOpResult.Failed failed => failed.Stderr,
            RemoteOpResult.Diverged => Strings.VersionControl_Diverged,
            RemoteOpResult.Offline => Strings.VersionControl_Offline,
            RemoteOpResult.RepositoryDirty => Strings.VersionControl_RepositoryDirty,
            RemoteOpResult.Success => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private async Task<bool> RunRestoreCycleAsync(
        string sha,
        string? branchName,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _lifecycleUsers);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return false;
            }

            Project project = _projectService.CurrentProject.Value
                              ?? throw new InvalidOperationException("No project is open.");
            string projectFile = project.Uri?.LocalPath
                                 ?? throw new InvalidOperationException(
                                     "The project has no file path.");
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            if (ownedService.Repository is null)
            {
                throw new InvalidOperationException(
                    "The open project is not tracked with Git.");
            }

            return await ownedService.ExecuteExclusiveAsync(
                async service =>
                {
                    WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                    if (status.HasConflicts)
                    {
                        NotificationService.ShowWarning(
                            Strings.VersionControl,
                            Strings.VersionControl_ConflictGuidance);
                        return false;
                    }

                    if (!await ConfirmRestoreAsync(cancellationToken))
                    {
                        return false;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    CheckedOutBranchTip originalTip =
                        await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
                    if (!status.IsClean)
                    {
                        CommitResult result = await service.CommitAllAsync(
                            RestoreSafetySnapshotMessage,
                            SnapshotKind.Safety,
                            CancellationToken.None);
                        EnsureAutomaticSnapshotWasNotSkipped(result);
                        CheckedOutBranchTip committedTip = await service.GetCheckedOutBranchTipAsync(
                            CancellationToken.None);
                        originalTip = GetExpectedTipAfterCommitAll(
                            originalTip,
                            result,
                            committedTip);
                        if (!BranchTipsEqual(committedTip, originalTip))
                        {
                            throw new InvalidOperationException(
                                "The branch ref changed while the restore safety snapshot was committed.");
                        }
                    }

                    CheckedOutBranchTip expectedResultTip = originalTip;
                    bool projectClosed = false;
                    try
                    {
                        await CloseProjectForOperationAsync(transition, CancellationToken.None);
                        projectClosed = true;

                        if (branchName is null)
                        {
                            CommitResult restoreResult = await service.CommitProjectTreeAsync(
                                originalTip,
                                sha,
                                $"beutl: restore project state from {GetShortSha(sha)}",
                                SnapshotKind.Restore,
                                CancellationToken.None);

                            expectedResultTip = GetExpectedTipAfterCommit(
                                originalTip,
                                restoreResult);
                            EnsureAutomaticSnapshotWasNotSkipped(restoreResult);
                        }
                        else
                        {
                            try
                            {
                                await service.CreateBranchAsync(
                                    branchName,
                                    sha,
                                    CancellationToken.None);
                            }
                            catch
                            {
                                expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                                    CancellationToken.None);
                                throw;
                            }

                            expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                                CancellationToken.None);
                        }

                        await ReopenProjectAsync(transition, projectFile);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Exception? recoveryFailure = null;
                        if (projectClosed)
                        {
                            recoveryFailure = await TryRestoreOriginalStateAsync(
                                service,
                                originalTip,
                                expectedResultTip,
                                branchName is null ? RecoveryKind.Restore : RecoveryKind.Branch,
                                transition,
                                projectFile);
                        }

                        if (recoveryFailure is not null)
                        {
                            var combined = new AggregateException(
                                "The version-control operation and recovery both failed.",
                                ex,
                                recoveryFailure);
                            _logger.LogError(
                                combined,
                                "Failed to restore project version {Commit}, and the original state could not be recovered.",
                                sha);
                            NotificationService.ShowError(
                                Strings.VersionControl_ErrorTitle,
                                string.Format(
                                    Strings.VersionControl_RecoveryFailed,
                                    GetErrorText(ex),
                                    GetErrorText(recoveryFailure)));
                            return false;
                        }

                        if (ex is OperationCanceledException
                            && cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        _logger.LogError(ex, "Failed to restore project version {Commit}.", sha);
                        NotificationService.ShowError(
                            Strings.VersionControl_ErrorTitle,
                            ex is GitOperationException { Stderr.Length: > 0 } gitException
                                ? gitException.Stderr
                                : ex.Message);
                        return false;
                    }
                    finally
                    {
                        FinishInternalTransition();
                    }
                },
                cancellationToken);
        }
        finally
        {
            if (gateEntered)
            {
                _lifecycleGate.Release();
            }

            if (Interlocked.Decrement(ref _lifecycleUsers) == 0 && _disposed)
            {
                ClearProjectState();
            }
        }
    }

    private async Task<Exception?> TryRestoreOriginalStateAsync(
        IProjectVersionControlTransaction service,
        CheckedOutBranchTip originalTip,
        CheckedOutBranchTip expectedResultTip,
        RecoveryKind recoveryKind,
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        try
        {
            CheckedOutBranchTip actualTip = await service.GetCheckedOutBranchTipAsync(
                CancellationToken.None);
            if (!BranchTipsEqual(actualTip, expectedResultTip))
            {
                throw new InvalidOperationException(
                    "The checked-out branch changed before the operation could be recovered.");
            }

            if (recoveryKind == RecoveryKind.Branch)
            {
                if (!BranchTipsEqual(actualTip, originalTip))
                {
                    await service.SwitchBranchAsync(
                        GetLocalBranchName(originalTip.RefName),
                        CancellationToken.None);
                    CheckedOutBranchTip restoredTip = await service.GetCheckedOutBranchTipAsync(
                        CancellationToken.None);
                    if (!BranchTipsEqual(restoredTip, originalTip))
                    {
                        throw new InvalidOperationException(
                            "The original branch ref changed while the branch operation was being recovered.");
                    }
                }
            }
            else
            {
                if (!string.Equals(
                        actualTip.RefName,
                        originalTip.RefName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The restore operation is no longer on its original branch.");
                }

                CommitResult recovery = await service.CommitProjectTreeAsync(
                    expectedResultTip,
                    originalTip.Commit,
                    RestoreRecoveryMessage,
                    SnapshotKind.Recovery,
                    CancellationToken.None);
                EnsureAutomaticSnapshotWasNotSkipped(recovery);
                CheckedOutBranchTip expectedRecoveryTip = GetExpectedTipAfterCommit(
                    expectedResultTip,
                    recovery);
                CheckedOutBranchTip verifiedRecoveryTip = await service.GetCheckedOutBranchTipAsync(
                    CancellationToken.None);
                if (!BranchTipsEqual(verifiedRecoveryTip, expectedRecoveryTip))
                {
                    throw new InvalidOperationException(
                        "The branch ref changed while the restore operation was being recovered.");
                }
            }
        }
        catch (Exception recoveryException)
        {
            return recoveryException;
        }

        try
        {
            await ReopenProjectAsync(transition, projectFile);
            return null;
        }
        catch (Exception reopenException)
        {
            return reopenException;
        }
    }

    private static string GetLocalBranchName(string refName)
    {
        const string Prefix = "refs/heads/";
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal)
            || refName.Length == Prefix.Length)
        {
            throw new ArgumentException("A local branch ref is required.", nameof(refName));
        }

        return refName[Prefix.Length..];
    }

    private void EnsureProjectReopened(string projectFile)
    {
        string? reopenedPath = _projectService.CurrentProject.Value?.Uri?.LocalPath;
        if (!string.Equals(
                Path.GetFullPath(reopenedPath ?? string.Empty),
                Path.GetFullPath(projectFile),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The project could not be reopened after restoring files.");
        }
    }

    private IProjectVersionControlBackend GetTrackedBackend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IProjectVersionControlBackend service = GetOwnedBackend()
                                                ?? throw new InvalidOperationException(
                                                    "Version control is not available.");
        if (service.Repository is null)
        {
            throw new InvalidOperationException(
                "The open project is not tracked with Git.");
        }

        return service;
    }

    private IProjectVersionControlBackend? GetOwnedBackend()
    {
        lock (_stateGate)
        {
            return _state.OwnedService;
        }
    }

    private bool IsInternalVersionControlTransition()
    {
        return _projectService.CurrentTransition is
        {
            Purpose: ProjectTransitionPurpose.VersionControlMutation,
            Owner: var owner,
        }
               && ReferenceEquals(owner, this);
    }

    private Project GetOpenProject()
    {
        return _projectService.CurrentProject.Value
               ?? throw new InvalidOperationException("No project is open.");
    }

    private static string GetProjectFile(Project project)
    {
        return project.Uri?.LocalPath
               ?? throw new InvalidOperationException("The project has no file path.");
    }

    private IDisposable? TryBeginWorktreeMutation()
    {
        IDisposable? mutation = _editorService.TryBeginWorktreeMutation();
        if (mutation is not null)
        {
            return mutation;
        }

        NotificationService.ShowWarning(
            Strings.VersionControl,
            Strings.VersionControl_ExportInProgress);
        return null;
    }

    private static bool EnsureRepositoryIsNotConflicted(WorkspaceStatus status)
    {
        if (!status.HasConflicts)
        {
            return true;
        }

        NotificationService.ShowWarning(
            Strings.VersionControl,
            Strings.VersionControl_ConflictGuidance);
        return false;
    }

    private async Task CloseProjectForOperationAsync(
        ProjectService.ProjectTransitionScope transition,
        CancellationToken cancellationToken)
    {
        await transition.CloseProjectAsync(cancellationToken);

        if (_projectService.CurrentProject.Value is not null)
        {
            throw new InvalidOperationException(
                "The project could not be closed before changing version-controlled files.");
        }
    }

    private async Task ReopenProjectAsync(
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        await transition.OpenProjectAsync(projectFile);
        EnsureProjectReopened(projectFile);
    }

    private bool HandleCycleFailure(
        Exception exception,
        Exception? recoveryFailure,
        string operation,
        CancellationToken cancellationToken)
    {
        if (recoveryFailure is not null)
        {
            var combined = new AggregateException(
                "The version-control operation and recovery both failed.",
                exception,
                recoveryFailure);
            _logger.LogError(
                combined,
                "Failed to complete version-control operation {Operation}, and the original state could not be recovered.",
                operation);
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                string.Format(
                    Strings.VersionControl_RecoveryFailed,
                    GetErrorText(exception),
                    GetErrorText(recoveryFailure)));
            return false;
        }

        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogError(
            exception,
            "Failed to complete version-control operation {Operation}.",
            operation);
        NotificationService.ShowError(
            Strings.VersionControl_ErrorTitle,
            GetErrorText(exception));
        return false;
    }

    private void FinishInternalTransition()
    {
        if (_projectService.CurrentProject.Value is null)
        {
            ClearProjectState();
        }
    }

    private void FinishLifecycleOperation(bool gateEntered)
    {
        if (gateEntered)
        {
            _lifecycleGate.Release();
        }

        if (Interlocked.Decrement(ref _lifecycleUsers) == 0 && _disposed)
        {
            ClearProjectState();
        }
    }

    private static void EnsureAutomaticSnapshotWasNotSkipped(CommitResult result)
    {
        if (result is CommitResult.SkippedNoIdentity)
        {
            throw new GitIdentityRequiredException();
        }
    }

    private static string GetShortSha(string sha)
    {
        return sha[..Math.Min(7, sha.Length)];
    }

    private static string GetErrorText(Exception exception)
    {
        return exception is GitOperationException { Stderr.Length: > 0 } gitException
            ? gitException.Stderr
            : exception.Message;
    }

    private static Task<bool> ShowRestoreConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Restore,
            Strings.VersionControl_RestoreConfirmation,
            cancellationToken);
    }

    private static Task<bool> ShowSwitchBranchConfirmationAsync(
        string branchName,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_SwitchBranch,
            string.Format(
                Strings.VersionControl_SwitchBranchConfirmation,
                branchName),
            cancellationToken);
    }

    private static Task<bool> ShowPullConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Pull,
            Strings.VersionControl_PullConfirmation,
            cancellationToken);
    }

    private static Task<bool> ShowEnclosingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_EnclosingRepositoryFound}\n\n{repository.RepoRoot}",
            cancellationToken);
    }

    private static Task<bool> ShowStaleLockConfirmationAsync(
        RepositoryLockInfo lockInfo,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_StaleLockConfirmation}\n\n{lockInfo.LockPath}",
            cancellationToken);
    }

    private async Task WarnBeforeOpeningConflictedProjectAsync(string projectFile)
    {
        string? markerFile = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);
        if (markerFile is not null)
        {
            await WarnConflictMarkersAsync(markerFile);
        }
    }

    private static async Task ShowConflictMarkerWarningAsync(string markerFile)
    {
        await Dispatcher.UIThread.InvokeAsync(
            () => NotificationService.ShowWarning(
                Strings.VersionControl_ConflictMarkerWarningTitle,
                string.Format(
                    Strings.VersionControl_ConflictMarkerWarning,
                    markerFile)));
    }

    private static async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VersionControlPickerFlyout? flyout = null;
        Task<bool>? confirmation = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (GetFlyoutAnchor() is not { } anchor)
            {
                return;
            }

            flyout = new VersionControlPickerFlyout();
            confirmation = flyout.ShowConfirmationAsync(anchor, title, message);
        });

        if (flyout is null || confirmation is null)
        {
            return false;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                var target = (VersionControlPickerFlyout)state!;
                Dispatcher.UIThread.Post(target.Hide);
            },
            flyout);
        return await confirmation.WaitAsync(cancellationToken);
    }

    private static Control? GetFlyoutAnchor()
    {
        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime
                {
                    MainWindow: { } mainWindow,
                })
        {
            return null;
        }

        Control? focused = mainWindow.FocusManager?.GetFocusedElement() as Control;
        return focused?.IsAttachedToVisualTree() == true
            ? focused
            : mainWindow;
    }

    private static async Task ShowPolicyNoticeAsync(
        VersionControlPolicyNotice notice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string message = notice switch
        {
            VersionControlPolicyNotice.LfsRemoteQuota
                => Strings.VersionControl_LfsQuotaNotice,
            VersionControlPolicyNotice.LargeMediaWithoutLfs largeMedia
                => string.Format(
                    Strings.VersionControl_LargeMediaWarningFormat,
                    largeMedia.Path),
            VersionControlPolicyNotice.MissingIdentity
                => Strings.VersionControl_MissingIdentityNotice,
            _ => throw new ArgumentOutOfRangeException(nameof(notice)),
        };

        await Dispatcher.UIThread.InvokeAsync(() =>
            NotificationService.ShowWarning(Strings.VersionControl, message));
    }

    private async Task CommitSnapshotAsync(
        bool enabled,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IProjectVersionControlBackend? service = GetOwnedBackend();
        if (!enabled || service?.Repository is null)
        {
            return;
        }

        try
        {
            await service.CommitAllAsync(message, kind, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the {SnapshotKind} project snapshot.", kind);
        }
    }

    private void OnProjectChanged(Project? project)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (IsInternalVersionControlTransition())
            {
                if (project is null)
                {
                    SetVisibleService(null);
                    return;
                }

                string preservedRoot = GetProjectRoot(project);
                IProjectVersionControlBackend? preservedService = GetOwnedBackend();
                if (preservedService?.Repository is { } preservedRepository
                    && RepositoryPathComparer.AreEquivalent(
                        preservedRepository.ProjectRoot,
                        preservedRoot))
                {
                    SetVisibleService(preservedService);
                    return;
                }
            }

            if (project is null)
            {
                ClearProjectState();
                return;
            }

            string projectRoot = GetProjectRoot(project);
            IProjectVersionControlBackend service = _serviceFactory?.Invoke(null)
                ?? new GitCliVersionControlService(
                    _installationLocator,
                    repository: null,
                    () => _projectService.CurrentProject.Value is null,
                    PresentPolicyNoticeAsync);
            var activation = new ActivationContext(
                Interlocked.Increment(ref _nextActivationRevision),
                projectRoot,
                service);
            if (BeginActivation(activation))
            {
                _ = ActivateRepositoryAsync(activation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ClearProjectState();
        }
    }

    private async Task ActivateRepositoryAsync(ActivationContext activation)
    {
        try
        {
            GitAvailability availability = await activation.Service.GetAvailabilityAsync(
                activation.CancellationToken);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return;
            }

            RepositoryInfo? repository = await activation.Service.DiscoverRepositoryAsync(
                activation.ProjectRoot,
                activation.CancellationToken);
            if (repository is null)
            {
                return;
            }

            if (repository.IsNestedInForeignRepo
                && !await ConfirmUseEnclosingRepositoryAsync(
                    repository,
                    activation.CancellationToken))
            {
                return;
            }

            if (!IsCurrentActivation(activation))
            {
                return;
            }

            IProjectVersionControlBackend trackedService = _serviceFactory?.Invoke(repository)
                ?? new GitCliVersionControlService(
                    _installationLocator,
                    repository,
                    () => _projectService.CurrentProject.Value is null,
                    PresentPolicyNoticeAsync);
            try
            {
                await trackedService.EnsureRepositoryHygieneAsync(
                    activation.CancellationToken);
            }
            catch
            {
                trackedService.Dispose();
                throw;
            }

            CompleteActivation(activation, trackedService);
        }
        catch (OperationCanceledException) when (activation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover version control for the open project.");
        }
        finally
        {
            bool stillOwned;
            lock (_stateGate)
            {
                if (ReferenceEquals(_activation, activation))
                {
                    _activation = null;
                }

                stillOwned = ReferenceEquals(_state.OwnedService, activation.Service);
            }

            activation.Complete();
            if (!stillOwned && !activation.IsRetirementQueued)
            {
                DisposeService(activation.Service);
            }

            activation.Dispose();
        }
    }

    private async Task<RepositoryInfo?> SelectRepositoryForInitializationAsync(
        IProjectVersionControlBackend service,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            projectRoot,
            cancellationToken);
        if (discovered is not { IsNestedInForeignRepo: true })
        {
            return discovered ?? new RepositoryInfo(projectRoot, projectRoot);
        }

        return await ConfirmUseEnclosingRepositoryAsync(discovered, cancellationToken)
            ? discovered
            : null;
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        _ = RefreshAvailabilityAsync();
    }

    private async Task RefreshAvailabilityAsync()
    {
        try
        {
            await GetAvailabilityAsync();
        }
        catch (Exception ex)
        {
            bool schedulePublication = false;
            lock (_stateGate)
            {
                if (!_disposed)
                {
                    schedulePublication = TransitionStateLocked(
                        _state with { IsGitAvailable = false });
                }
            }

            SchedulePublicationDrain(schedulePublication);
            _logger.LogWarning(ex, "Failed to refresh Git availability.");
        }
    }

    private bool BeginActivation(ActivationContext activation)
    {
        ActivationContext? previousActivation;
        bool schedulePublication = false;
        bool rejected;
        lock (_stateGate)
        {
            rejected = _disposed;
            if (rejected)
            {
                previousActivation = null;
            }
            else
            {
                previousActivation = _activation;
                _activation = activation;
                schedulePublication = TransitionOwnedServiceLocked(
                    activation.Service,
                    activation.Service,
                    activation.ProjectRoot,
                    previousActivation,
                    out _);
            }
        }

        if (rejected)
        {
            activation.Cancel();
            activation.Complete();
            activation.Dispose();
            DisposeService(activation.Service);
            return false;
        }

        previousActivation?.Cancel();
        SchedulePublicationDrain(schedulePublication);
        return true;
    }

    private bool CompleteActivation(
        ActivationContext activation,
        IProjectVersionControlBackend trackedService)
    {
        bool accepted;
        bool retirementQueued = false;
        bool schedulePublication = false;
        lock (_stateGate)
        {
            accepted = !_disposed
                       && ReferenceEquals(_activation, activation)
                       && activation.Revision == Volatile.Read(ref _nextActivationRevision)
                       && ReferenceEquals(_state.OwnedService, activation.Service)
                       && _state.ProjectRoot is { } projectRoot
                       && string.Equals(projectRoot, activation.ProjectRoot, PathComparison)
                       && !activation.CancellationToken.IsCancellationRequested;
            if (accepted)
            {
                schedulePublication = TransitionOwnedServiceLocked(
                    trackedService,
                    trackedService,
                    activation.ProjectRoot,
                    activation,
                    out retirementQueued);
            }
        }

        if (!accepted)
        {
            DisposeService(trackedService);
            return false;
        }

        SchedulePublicationDrain(schedulePublication);
        return retirementQueued;
    }

    private bool IsCurrentActivation(ActivationContext activation)
    {
        lock (_stateGate)
        {
            return !_disposed
                   && ReferenceEquals(_activation, activation)
                   && activation.Revision == Volatile.Read(ref _nextActivationRevision)
                   && ReferenceEquals(_state.OwnedService, activation.Service)
                   && _state.ProjectRoot is { } projectRoot
                   && string.Equals(projectRoot, activation.ProjectRoot, PathComparison)
                   && !activation.CancellationToken.IsCancellationRequested;
        }
    }

    private void ClearProjectState()
    {
        ActivationContext? activation;
        bool schedulePublication;
        lock (_stateGate)
        {
            activation = _activation;
            _activation = null;
            schedulePublication = TransitionOwnedServiceLocked(
                ownedService: null,
                visibleService: null,
                projectRoot: null,
                activation,
                out _);
        }

        activation?.Cancel();
        SchedulePublicationDrain(schedulePublication);
    }

    private void SetVisibleService(IProjectVersionControlService? service)
    {
        bool schedulePublication;
        lock (_stateGate)
        {
            if (service is not null && !ReferenceEquals(service, _state.OwnedService))
            {
                return;
            }

            schedulePublication = TransitionStateLocked(
                _state with
                {
                    VisibleService = service,
                    IsTracked = service?.Repository is not null,
                });
        }

        SchedulePublicationDrain(schedulePublication);
    }

    private bool TransitionOwnedServiceLocked(
        IProjectVersionControlBackend? ownedService,
        IProjectVersionControlService? visibleService,
        string? projectRoot,
        ActivationContext? retiringActivation,
        out bool retirementQueued)
    {
        IProjectVersionControlBackend? previous = _state.OwnedService;
        if (!ReferenceEquals(previous, ownedService))
        {
            if (previous is IRepositoryLockRecoveryService previousRecovery)
            {
                previousRecovery.RecoverableLockAvailable -= OnRecoverableLockAvailable;
            }

            if (ownedService is IRepositoryLockRecoveryService recovery)
            {
                recovery.RecoverableLockAvailable += OnRecoverableLockAvailable;
            }
        }

        ServiceRetirement? retirement = null;
        if (previous is not null && !ReferenceEquals(previous, ownedService))
        {
            Task activationReady = ReferenceEquals(retiringActivation?.Service, previous)
                ? retiringActivation.Completion
                : Task.CompletedTask;
            Task lifetimeReady = previous.RetireAsync(finalSnapshot: null);
            retirement = new ServiceRetirement(
                previous,
                Task.WhenAll(activationReady, lifetimeReady));
        }
        if (retirement is not null
            && ReferenceEquals(retiringActivation?.Service, previous))
        {
            retiringActivation!.MarkRetirementQueued();
        }

        retirementQueued = retirement is not null;
        return TransitionStateLocked(
            _state with
            {
                ProjectRoot = projectRoot,
                OwnedService = ownedService,
                VisibleService = visibleService,
                IsTracked = visibleService?.Repository is not null,
            },
            retirement);
    }

    private bool TransitionStateLocked(
        CoordinatorState next,
        ServiceRetirement? retirement = null)
    {
        if (retirement is null
            && ReferenceEquals(_state.OwnedService, next.OwnedService)
            && ReferenceEquals(_state.VisibleService, next.VisibleService)
            && string.Equals(_state.ProjectRoot, next.ProjectRoot, PathComparison)
            && _state.IsGitAvailable == next.IsGitAvailable
            && _state.IsTracked == next.IsTracked)
        {
            return false;
        }

        next = next with { Revision = ++_nextStateRevision };
        _state = next;
        _publicationQueue.Enqueue(new StatePublication(next, retirement));
        if (_publicationDrainScheduled)
        {
            return false;
        }

        _publicationDrainScheduled = true;
        return true;
    }

    private void SchedulePublicationDrain(bool schedulePublication)
    {
        if (!schedulePublication)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            DrainStatePublications();
        }
        else
        {
            Dispatcher.UIThread.Post(DrainStatePublications);
        }
    }

    private void DrainStatePublications()
    {
        lock (_stateGate)
        {
            if (_publicationDrainRunning)
            {
                return;
            }

            _publicationDrainRunning = true;
        }

        bool disposeProperties = false;
        bool reschedule = false;
        try
        {
            while (true)
            {
                StatePublication publication;
                lock (_stateGate)
                {
                    if (_publicationQueue.Count == 0)
                    {
                        break;
                    }

                    publication = _publicationQueue.Dequeue();
                }

                if (publication.State.Revision > _lastPublishedRevision)
                {
                    _lastPublishedRevision = publication.State.Revision;
                    if (!_propertiesDisposed)
                    {
                        PublishStateValue(
                            () => _isGitAvailable.Value = publication.State.IsGitAvailable,
                            publication.State.Revision,
                            nameof(IsGitAvailable));
                        PublishStateValue(
                            () => _isTracked.Value = publication.State.IsTracked,
                            publication.State.Revision,
                            nameof(IsTracked));
                        PublishStateValue(
                            () => _editorService.PublishProjectVersionControlService(
                                publication.State.VisibleService),
                            publication.State.Revision,
                            nameof(EditorService.ProjectVersionControlService));
                    }
                }

                if (publication.Retirement is { } retirement)
                {
                    RetireService(retirement);
                }
            }
        }
        finally
        {
            lock (_stateGate)
            {
                _publicationDrainRunning = false;
                if (_publicationQueue.Count == 0)
                {
                    _publicationDrainScheduled = false;
                    if (_disposePropertiesRequested && !_propertiesDisposed)
                    {
                        _propertiesDisposed = true;
                        disposeProperties = true;
                    }
                }
                else
                {
                    _publicationDrainScheduled = true;
                    reschedule = true;
                }
            }

            if (disposeProperties)
            {
                _isGitAvailable.Dispose();
                _isTracked.Dispose();
            }

            if (reschedule)
            {
                Dispatcher.UIThread.Post(DrainStatePublications);
            }
        }
    }

    private void PublishStateValue(Action publish, long revision, string member)
    {
        try
        {
            publish();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A version-control state subscriber failed while publishing {Member} at revision {Revision}.",
                member,
                revision);
        }
    }

    private void DisposePublishedProperties()
    {
        void DisposeOnUiThread()
        {
            bool drain;
            lock (_stateGate)
            {
                if (_propertiesDisposed)
                {
                    return;
                }

                _disposePropertiesRequested = true;
                drain = !_publicationDrainRunning;
                if (drain)
                {
                    _publicationDrainScheduled = true;
                }
            }

            if (drain)
            {
                DrainStatePublications();
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            DisposeOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(DisposeOnUiThread);
        }
    }

    private void RetireService(ServiceRetirement retirement)
    {
        _ = RetireServiceAsync(retirement);
    }

    private async Task RetireServiceAsync(ServiceRetirement retirement)
    {
        try
        {
            await retirement.Ready.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a project version-control service.");
        }
        finally
        {
            DisposeService(retirement.Service);
        }
    }

    private void DisposeService(IProjectVersionControlBackend? service)
    {
        try
        {
            service?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose a project version control service.");
        }
    }

    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        void StartRecovery()
        {
            _ = OfferLockRecoveryAsync(sender, lockInfo);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            StartRecovery();
        }
        else
        {
            Dispatcher.UIThread.Post(StartRecovery);
        }
    }

    private async Task OfferLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        await _lockRecoveryGate.WaitAsync();
        try
        {
            if (_disposed
                || sender is not IRepositoryLockRecoveryService recovery
                || !ReferenceEquals(CurrentService, sender)
                || !Equals(recovery.RecoverableLock, lockInfo)
                || !await ConfirmRemoveStaleLockAsync(lockInfo, CancellationToken.None))
            {
                return;
            }

            if (await recovery.RemoveRecoverableLockAsync(CancellationToken.None))
            {
                _logger.LogWarning(
                    "Removed stale Git repository lock with user consent. Lock: {LockPath}, LastWriteTimeUtc: {LastWriteTimeUtc}",
                    lockInfo.LockPath,
                    lockInfo.LastWriteTimeUtc);
                NotificationService.ShowInformation(
                    Strings.VersionControl,
                    Strings.VersionControl_StaleLockRemoved);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover a stale Git repository lock.");
        }
        finally
        {
            _lockRecoveryGate.Release();
        }
    }

    private enum RecoveryKind
    {
        Branch,
        Restore,
    }

    private sealed record CoordinatorState(
        long Revision,
        string? ProjectRoot,
        IProjectVersionControlBackend? OwnedService,
        IProjectVersionControlService? VisibleService,
        bool IsGitAvailable,
        bool IsTracked)
    {
        public static CoordinatorState Empty { get; } = new(
            Revision: 0,
            ProjectRoot: null,
            OwnedService: null,
            VisibleService: null,
            IsGitAvailable: false,
            IsTracked: false);
    }

    private sealed record StatePublication(
        CoordinatorState State,
        ServiceRetirement? Retirement);

    private sealed record ServiceRetirement(
        IProjectVersionControlBackend Service,
        Task Ready);

    private sealed class ActivationContext : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _retirementQueued;

        public ActivationContext(
            long revision,
            string projectRoot,
            IProjectVersionControlBackend service)
        {
            Revision = revision;
            ProjectRoot = projectRoot;
            Service = service;
        }

        public long Revision { get; }

        public string ProjectRoot { get; }

        public IProjectVersionControlBackend Service { get; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public Task Completion => _completion.Task;

        public bool IsRetirementQueued => Volatile.Read(ref _retirementQueued) != 0;

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Complete()
        {
            _completion.TrySetResult();
        }

        public void MarkRetirementQueued()
        {
            Interlocked.Exchange(ref _retirementQueued, 1);
        }

        public void Dispose()
        {
            _cancellation.Dispose();
        }
    }

    private static string GetProjectRoot(Project project)
    {
        string projectPath = project.Uri?.LocalPath
                             ?? throw new InvalidOperationException("The project has no file path.");
        return Path.GetDirectoryName(projectPath)
               ?? throw new InvalidOperationException("The project file has no parent directory.");
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
