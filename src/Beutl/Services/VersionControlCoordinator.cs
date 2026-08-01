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
    IDisposable,
    IAsyncDisposable
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
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinator>();
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _lockRecoveryGate = new(1, 1);
    private readonly SemaphoreSlim _operationCloseGate = new(1, 1);
    private readonly ReactivePropertySlim<bool> _isGitAvailable = new();
    private readonly ReactivePropertySlim<bool> _isTracked = new();
    private readonly Queue<StatePublication> _publicationQueue = new();
    private readonly Dictionary<IProjectVersionControlBackend, HashSet<ActivationContext>>
        _candidateServiceUsers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IProjectVersionControlBackend> _managedServices = new(
        ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource _propertiesDisposedCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _asyncDisposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private CoordinatorState _state = CoordinatorState.Empty;
    private ActivationContext? _activation;
    private TaskCompletionSource? _activationSetupsQuiesced;
    private TaskCompletionSource? _availabilityQuiesced;
    private TaskCompletionSource? _closeBarriersQuiesced;
    private TaskCompletionSource? _lifecycleQuiesced;
    private TaskCompletionSource? _lockRecoveryQuiesced;
    private TaskCompletionSource? _notificationsQuiesced;
    private TaskCompletionSource? _operationsQuiesced;
    private TaskCompletionSource? _publicationDrainQuiesced;
    private TaskCompletionSource? _retirementsQuiesced;
    private CancellationTokenSource? _operationEpochCancellation = new();
    private long _nextActivationRevision;
    private long _latestActivationRevision;
    private long _nextStateRevision;
    private long _lastPublishedRevision;
    private int _availabilityRevision;
    private int _activationSetupUsers;
    private int _availabilityUsers;
    private int _closeBarrierUsers;
    private int _lifecycleUsers;
    private int _lockRecoveryUsers;
    private int _notificationUsers;
    private int _operationUsers;
    private int _retirementUsers;
    private int _asyncDisposalStarted;
    private bool _publicationDrainScheduled;
    private bool _publicationDrainRunning;
    private bool _disposePropertiesRequested;
    private bool _operationCloseBarrierActive;
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
        _dispatcher = Dispatcher.UIThread;
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
        _projectService.ClosingFinalizing += NotifyProjectClosingAsync;
        _projectSubscription = _projectService.ProjectObservable.Subscribe(
            change => OnProjectChanged(change.New));
        _editorService.ProjectVersionControlCoordinator = this;
        StartAvailabilityRefresh();

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

    public Task<GitAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _availabilityUsers++;
        }

        return GetAvailabilityTrackedAsync(cancellationToken);
    }

    private async Task<GitAvailability> GetAvailabilityTrackedAsync(
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        try
        {
            int revision = Interlocked.Increment(ref _availabilityRevision);
            GitAvailability availability = await _installationLocator.LocateAsync(
                linkedCancellation.Token);
            linkedCancellation.Token.ThrowIfCancellationRequested();
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
        finally
        {
            FinishAvailabilityOperation();
        }
    }

    public async Task<bool> InitializeCurrentProjectAsync(
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestIdentityAsync);
        using NonTransactionalOperationLease operation =
            BeginNonTransactionalOperation(cancellationToken);
        CancellationToken operationCancellation = operation.CancellationToken;

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

        await activationTask.WaitAsync(operationCancellation);

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
                                               operationCancellation);
        if (targetRepository is null)
        {
            return false;
        }

        var options = new InitOptions(targetRepository, _config.UseLfsWhenAvailable);
        try
        {
            try
            {
                await service.InitializeAsync(options, operationCancellation);
            }
            catch (GitIdentityRequiredException)
            {
                GitIdentity? identity = await requestIdentityAsync(operationCancellation);
                if (identity is null)
                {
                    return false;
                }

                operationCancellation.ThrowIfCancellationRequested();
                await service.SetLocalIdentityAsync(identity, operationCancellation);
                await service.InitializeAsync(options, operationCancellation);
            }
        }
        catch (VersionControlConflictedException ex)
        {
            PublishNotification(() =>
                NotificationService.ShowWarning(Strings.VersionControl, ex.Guidance));
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

    public async Task NotifySavedAsync(CancellationToken cancellationToken = default)
    {
        using NonTransactionalOperationLease operation =
            BeginNonTransactionalOperation(cancellationToken);
        await CommitSnapshotAsync(
            _config.AutoCommitOnSave,
            SaveSnapshotMessage,
            SnapshotKind.Save,
            operation.CancellationToken);
    }

    private async Task NotifyProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (IsInternalVersionControlTransition())
        {
            return;
        }

        NonTransactionalCloseBarrier? closeBarrier =
            await TryBeginNonTransactionalCloseBarrierAsync(cancellationToken)
                .ConfigureAwait(false);
        if (closeBarrier is null)
        {
            return;
        }

        bool completionRegistered = false;
        try
        {
            closeContext.RegisterCompletion(closeBarrier.CompleteAsync);
            completionRegistered = true;
            await NotifyClosingCoreAsync(closeBarrier).ConfigureAwait(false);
        }
        finally
        {
            if (!completionRegistered)
            {
                await closeBarrier.CompleteAsync(projectClosed: false).ConfigureAwait(false);
            }
        }
    }

    private async Task NotifyClosingCoreAsync(NonTransactionalCloseBarrier closeBarrier)
    {
        CancellationToken closeCancellation = closeBarrier.CancellationToken;

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
            activationRevision = _latestActivationRevision;
        }

        if (activation is not null)
        {
            await activation.Completion.WaitAsync(closeCancellation).ConfigureAwait(false);
        }

        closeCancellation.ThrowIfCancellationRequested();
        try
        {
            IProjectVersionControlBackend service;
            ProjectVersionControlFinalSnapshot? finalSnapshot;
            bool schedulePublication;
            lock (_stateGate)
            {
                if (_disposed
                    || projectRoot is null
                    || activationRevision != _latestActivationRevision
                    || _state.ProjectRoot is not { } currentRoot
                    || !string.Equals(currentRoot, projectRoot, PathComparison))
                {
                    return;
                }

                IProjectVersionControlBackend? ownedService = _state.OwnedService;
                if (ownedService is null)
                {
                    return;
                }

                service = ownedService;

                finalSnapshot =
                    _config.AutoCommitOnClose
                        ? new ProjectVersionControlFinalSnapshot(
                            CloseSnapshotMessage,
                            SnapshotKind.Close)
                        : null;

                bool visibilityHidden = ReferenceEquals(_state.VisibleService, service);
                schedulePublication = visibilityHidden
                    && TransitionStateLocked(
                        _state with
                        {
                            VisibleService = null,
                            IsTracked = false,
                        });
            }

            SchedulePublicationDrain(schedulePublication);
            await FlushPublicationDrainAsync().ConfigureAwait(false);
            closeCancellation.ThrowIfCancellationRequested();

            try
            {
                await service.RetireAsync(finalSnapshot).ConfigureAwait(false);
            }
            finally
            {
                DetachRetiredService(service);
            }
        }
        catch (OperationCanceledException) when (closeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire version control while closing the project.");
        }
    }

    public async Task CloseCurrentProjectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        using NonTransactionalOperationLease operation =
            BeginNonTransactionalOperation(cancellationToken);
        CancellationToken operationCancellation = operation.CancellationToken;
        IProjectVersionControlBackend service = GetTrackedBackend();
        try
        {
            return await service.CommitAllAsync(
                message.Trim(),
                SnapshotKind.Manual,
                operationCancellation);
        }
        catch (GitIdentityRequiredException)
        {
            GitIdentity? identity = await RequestIdentityAsync(operationCancellation);
            if (identity is null)
            {
                throw;
            }

            operationCancellation.ThrowIfCancellationRequested();
            await service.SetLocalIdentityAsync(identity, operationCancellation);
            return await service.CommitAllAsync(
                message.Trim(),
                SnapshotKind.Manual,
                operationCancellation);
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

    public async Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using NonTransactionalOperationLease operation =
            BeginNonTransactionalOperation(cancellationToken);
        await GetTrackedBackend().SetRemoteAsync(url.Trim(), operation.CancellationToken);
    }

    public async Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        using NonTransactionalOperationLease operation =
            BeginNonTransactionalOperation(cancellationToken);
        await GetTrackedBackend().SetLocalIdentityAsync(identity, operation.CancellationToken);
    }

    public async Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        using NonTransactionalOperationLease operation =
            BeginNonTransactionalOperation(cancellationToken);
        return await GetTrackedBackend().PushAsync(progress, operation.CancellationToken);
    }

    public Task<RemoteOpResult> PullAsync(CancellationToken cancellationToken = default)
    {
        return RunPullCycleAsync(cancellationToken);
    }

    public void Dispose()
    {
        BeginDisposal();
        StartDisposalCompletion();
    }

    public ValueTask DisposeAsync()
    {
        BeginDisposal();
        StartDisposalCompletion();
        return new ValueTask(_asyncDisposalCompletion.Task);
    }

    private void StartDisposalCompletion()
    {
        if (Interlocked.CompareExchange(ref _asyncDisposalStarted, 1, 0) == 0)
        {
            _ = CompleteDisposalAsync();
            _ = ObserveDisposalCompletionAsync();
        }
    }

    private async Task ObserveDisposalCompletionAsync()
    {
        try
        {
            await _asyncDisposalCompletion.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete version-control coordinator disposal.");
        }
    }

    private void BeginDisposal()
    {
        bool clearProjectState;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            clearProjectState = _closeBarrierUsers == 0
                                && _lifecycleUsers == 0
                                && _operationUsers == 0;
        }

        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A cancellation callback failed while disposing version control.");
        }
        _config.ConfigurationChanged -= OnVersionControlConfigChanged;
        _projectService.Opening -= WarnBeforeOpeningConflictedProjectAsync;
        _projectService.ClosingFinalizing -= NotifyProjectClosingAsync;
        _projectSubscription.Dispose();
        if (ReferenceEquals(_editorService.ProjectVersionControlCoordinator, this))
        {
            _editorService.ProjectVersionControlCoordinator = null;
        }

        if (clearProjectState)
        {
            ClearProjectState();
        }
        else
        {
            SetVisibleService(null);
        }

        DisposePublishedProperties();
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            await WaitForAvailabilityQuiescenceAsync().ConfigureAwait(false);
            await WaitForOperationQuiescenceAsync().ConfigureAwait(false);
            await WaitForCloseBarrierQuiescenceAsync().ConfigureAwait(false);
            await WaitForLifecycleQuiescenceAsync().ConfigureAwait(false);
            await WaitForActivationSetupQuiescenceAsync().ConfigureAwait(false);
            ClearProjectState();
            await WaitForLockRecoveryQuiescenceAsync().ConfigureAwait(false);
            await WaitForNotificationQuiescenceAsync().ConfigureAwait(false);
            await FlushPublicationDrainAsync();
            await _propertiesDisposedCompletion.Task.ConfigureAwait(false);
            await WaitForRetirementQuiescenceAsync().ConfigureAwait(false);
            DisposeOperationEpochCancellation();
            _lifetimeCancellation.Dispose();
            _asyncDisposalCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            _asyncDisposalCompletion.TrySetException(ex);
        }
    }

    private void DisposeOperationEpochCancellation()
    {
        CancellationTokenSource? operationEpochCancellation;
        lock (_stateGate)
        {
            operationEpochCancellation = _operationEpochCancellation;
            _operationEpochCancellation = null;
        }

        operationEpochCancellation?.Dispose();
    }

    private Task WaitForAvailabilityQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_availabilityUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_availabilityQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForActivationSetupQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_activationSetupUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_activationSetupsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForLifecycleQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_lifecycleUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_lifecycleQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForCloseBarrierQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_closeBarrierUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_closeBarriersQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForOperationQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_operationUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_operationsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForLockRecoveryQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_lockRecoveryUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_lockRecoveryQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForRetirementQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_retirementUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_retirementsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForNotificationQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_notificationUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_notificationsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private async Task FlushPublicationDrainAsync()
    {
        Task? runningDrain;
        lock (_stateGate)
        {
            runningDrain = _publicationDrainRunning
                ? (_publicationDrainQuiesced ??= CreateCompletionSource()).Task
                : null;
        }

        if (runningDrain is not null)
        {
            await runningDrain.ConfigureAwait(false);
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            DrainStatePublications();
        }
        else
        {
            await _dispatcher.InvokeAsync(DrainStatePublications);
        }
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task<bool> RunBranchCycleAsync(
        string branchName,
        bool create,
        CancellationToken cancellationToken)
    {
        BeginLifecycleOperation();
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
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
        BeginLifecycleOperation();
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
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
        BeginLifecycleOperation();
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
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
                        PublishNotification(() =>
                            NotificationService.ShowWarning(
                                Strings.VersionControl,
                                Strings.VersionControl_ConflictGuidance));
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
                            PublishNotification(() =>
                                NotificationService.ShowError(
                                    Strings.VersionControl_ErrorTitle,
                                    string.Format(
                                        Strings.VersionControl_RecoveryFailed,
                                        GetErrorText(ex),
                                        GetErrorText(recoveryFailure))));
                            return false;
                        }

                        if (ex is OperationCanceledException
                            && cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        _logger.LogError(ex, "Failed to restore project version {Commit}.", sha);
                        PublishNotification(() =>
                            NotificationService.ShowError(
                                Strings.VersionControl_ErrorTitle,
                                ex is GitOperationException { Stderr.Length: > 0 } gitException
                                    ? gitException.Stderr
                                    : ex.Message));
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
            FinishLifecycleOperation(gateEntered);
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
        IProjectVersionControlBackend service = GetOperationReadyBackend()
                                                ?? throw new InvalidOperationException(
                                                    "Version control is not available.");
        if (service.Repository is null)
        {
            throw new InvalidOperationException(
                "The open project is not tracked with Git.");
        }

        return service;
    }

    private IProjectVersionControlBackend? GetOperationReadyBackend()
    {
        lock (_stateGate)
        {
            return ReferenceEquals(_state.OwnedService, _state.VisibleService)
                ? _state.OwnedService
                : null;
        }
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

        PublishNotification(() =>
            NotificationService.ShowWarning(
                Strings.VersionControl,
                Strings.VersionControl_ExportInProgress));
        return null;
    }

    private bool EnsureRepositoryIsNotConflicted(WorkspaceStatus status)
    {
        if (!status.HasConflicts)
        {
            return true;
        }

        PublishNotification(() =>
            NotificationService.ShowWarning(
                Strings.VersionControl,
                Strings.VersionControl_ConflictGuidance));
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
            PublishNotification(() =>
                NotificationService.ShowError(
                    Strings.VersionControl_ErrorTitle,
                    string.Format(
                        Strings.VersionControl_RecoveryFailed,
                        GetErrorText(exception),
                        GetErrorText(recoveryFailure))));
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
        PublishNotification(() =>
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                GetErrorText(exception)));
        return false;
    }

    private void PublishNotification(Action notification)
    {
        lock (_stateGate)
        {
            if (_disposed && _lifecycleUsers == 0)
            {
                return;
            }

            if (!_dispatcher.CheckAccess())
            {
                _notificationUsers++;
                _ = PublishNotificationAsync(notification);
                return;
            }
        }

        TryPublishNotification(notification);
    }

    private async Task PublishNotificationAsync(Action notification)
    {
        try
        {
            await _dispatcher.InvokeAsync(() => TryPublishNotification(notification));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch a version-control notification.");
        }
        finally
        {
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                _notificationUsers--;
                if (_notificationUsers == 0 && _disposed)
                {
                    quiesced = _notificationsQuiesced;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    private void TryPublishNotification(Action notification)
    {
        if (_dispatcher.CheckAccess())
        {
            try
            {
                notification();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish a version-control notification.");
            }
        }
        else
        {
            throw new InvalidOperationException(
                "Version-control notifications must be published on the captured dispatcher.");
        }
    }

    private void FinishInternalTransition()
    {
        if (_projectService.CurrentProject.Value is null)
        {
            ClearProjectState();
        }
    }

    private void BeginLifecycleOperation()
    {
        lock (_stateGate)
        {
            ThrowIfLifecycleOperationUnavailableLocked();
            _lifecycleUsers++;
        }
    }

    private void ThrowIfLifecycleOperationUnavailable()
    {
        lock (_stateGate)
        {
            ThrowIfLifecycleOperationUnavailableLocked();
        }
    }

    private void ThrowIfLifecycleOperationUnavailableLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_operationCloseBarrierActive)
        {
            throw new InvalidOperationException(
                "Lifecycle version-control operations cannot run while the project is closing.");
        }
    }

    private NonTransactionalOperationLease BeginNonTransactionalOperation(
        CancellationToken cancellationToken)
    {
        CancellationToken operationEpochCancellation;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_operationCloseBarrierActive)
            {
                throw new InvalidOperationException(
                    "Version-control operations cannot start while the project is closing.");
            }

            operationEpochCancellation = (_operationEpochCancellation
                                          ?? throw new ObjectDisposedException(nameof(VersionControlCoordinator)))
                .Token;
            _operationUsers++;
        }

        try
        {
            return new NonTransactionalOperationLease(
                this,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellation.Token,
                    operationEpochCancellation));
        }
        catch
        {
            FinishNonTransactionalOperation();
            throw;
        }
    }

    private NonTransactionalOperationLease? TryBeginNonTransactionalOperation(
        CancellationToken cancellationToken)
    {
        CancellationToken operationEpochCancellation;
        lock (_stateGate)
        {
            if (_disposed || _operationCloseBarrierActive)
            {
                return null;
            }

            operationEpochCancellation = (_operationEpochCancellation
                                          ?? throw new ObjectDisposedException(nameof(VersionControlCoordinator)))
                .Token;
            _operationUsers++;
        }

        try
        {
            return new NonTransactionalOperationLease(
                this,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellation.Token,
                    operationEpochCancellation));
        }
        catch
        {
            FinishNonTransactionalOperation();
            throw;
        }
    }

    private void FinishNonTransactionalOperation()
    {
        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        lock (_stateGate)
        {
            _operationUsers--;
            if (_operationUsers == 0)
            {
                quiesced = _operationsQuiesced;
                _operationsQuiesced = null;
                clearProjectState = _disposed
                                    && _closeBarrierUsers == 0
                                    && _lifecycleUsers == 0;
            }
        }

        try
        {
            if (clearProjectState)
            {
                ClearProjectState();
            }
        }
        finally
        {
            quiesced?.TrySetResult();
        }
    }

    private async Task<NonTransactionalCloseBarrier?>
        TryBeginNonTransactionalCloseBarrierAsync(CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return null;
            }

            _closeBarrierUsers++;
        }

        CancellationTokenSource? closeCancellation = null;
        CancellationTokenSource? operationEpochCancellation = null;
        bool gateEntered = false;
        bool barrierEntered = false;
        try
        {
            closeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
            await _operationCloseGate.WaitAsync(closeCancellation.Token).ConfigureAwait(false);
            gateEntered = true;

            Task operationsQuiesced;
            bool disposed;
            lock (_stateGate)
            {
                disposed = _disposed;
                if (!disposed)
                {
                    _operationCloseBarrierActive = true;
                    operationEpochCancellation = _operationEpochCancellation
                        ?? new CancellationTokenSource();
                    _operationEpochCancellation = operationEpochCancellation;
                    operationsQuiesced = _operationUsers == 0
                        ? Task.CompletedTask
                        : (_operationsQuiesced ??= CreateCompletionSource()).Task;
                    barrierEntered = true;
                }
                else
                {
                    operationsQuiesced = Task.CompletedTask;
                }
            }

            if (disposed)
            {
                closeCancellation.Dispose();
                FinishNonTransactionalCloseBarrierWaiter(gateEntered);
                return null;
            }

            Exception? cancellationFailure = null;
            try
            {
                operationEpochCancellation!.Cancel();
            }
            catch (Exception ex)
            {
                cancellationFailure = ex;
            }

            await operationsQuiesced.ConfigureAwait(false);
            if (cancellationFailure is not null)
            {
                _logger.LogError(
                    cancellationFailure,
                    "An operation cancellation callback failed while closing the project.");
            }

            closeCancellation.Token.ThrowIfCancellationRequested();
            return new NonTransactionalCloseBarrier(
                this,
                closeCancellation,
                operationEpochCancellation!);
        }
        catch
        {
            closeCancellation?.Dispose();
            if (barrierEntered)
            {
                FinishNonTransactionalCloseBarrier(
                    operationEpochCancellation!);
            }
            else
            {
                FinishNonTransactionalCloseBarrierWaiter(gateEntered);
            }

            throw;
        }
    }

    private void FinishNonTransactionalCloseBarrier(
        CancellationTokenSource operationEpochCancellation)
    {
        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        lock (_stateGate)
        {
            if (ReferenceEquals(_operationEpochCancellation, operationEpochCancellation))
            {
                _operationEpochCancellation = _disposed
                    ? null
                    : new CancellationTokenSource();
            }

            _operationCloseBarrierActive = false;
            _closeBarrierUsers--;
            if (_closeBarrierUsers == 0)
            {
                quiesced = _closeBarriersQuiesced;
                _closeBarriersQuiesced = null;
                clearProjectState = _disposed
                                    && _lifecycleUsers == 0
                                    && _operationUsers == 0;
            }
        }

        try
        {
            operationEpochCancellation.Dispose();
        }
        finally
        {
            _operationCloseGate.Release();
            try
            {
                if (clearProjectState)
                {
                    ClearProjectState();
                }
            }
            finally
            {
                quiesced?.TrySetResult();
            }
        }
    }

    private Task CompleteNonTransactionalCloseBarrierAsync(
        CancellationTokenSource operationEpochCancellation)
    {
        FinishNonTransactionalCloseBarrier(operationEpochCancellation);
        return Task.CompletedTask;
    }

    private void FinishNonTransactionalCloseBarrierWaiter(bool gateEntered)
    {
        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        lock (_stateGate)
        {
            _closeBarrierUsers--;
            if (_closeBarrierUsers == 0)
            {
                quiesced = _closeBarriersQuiesced;
                _closeBarriersQuiesced = null;
                clearProjectState = _disposed
                                    && _lifecycleUsers == 0
                                    && _operationUsers == 0;
            }
        }

        if (gateEntered)
        {
            _operationCloseGate.Release();
        }

        try
        {
            if (clearProjectState)
            {
                ClearProjectState();
            }
        }
        finally
        {
            quiesced?.TrySetResult();
        }
    }

    private void FinishLifecycleOperation(bool gateEntered)
    {
        if (gateEntered)
        {
            _lifecycleGate.Release();
        }

        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        lock (_stateGate)
        {
            _lifecycleUsers--;
            if (_lifecycleUsers == 0 && _disposed)
            {
                clearProjectState = _closeBarrierUsers == 0 && _operationUsers == 0;
                quiesced = _lifecycleQuiesced;
            }
        }

        try
        {
            if (clearProjectState)
            {
                ClearProjectState();
            }
        }
        finally
        {
            quiesced?.TrySetResult();
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

    private Task<bool> ShowRestoreConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Restore,
            Strings.VersionControl_RestoreConfirmation,
            cancellationToken);
    }

    private Task<bool> ShowSwitchBranchConfirmationAsync(
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

    private Task<bool> ShowPullConfirmationAsync(
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl_Pull,
            Strings.VersionControl_PullConfirmation,
            cancellationToken);
    }

    private Task<bool> ShowEnclosingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            $"{Strings.VersionControl_EnclosingRepositoryFound}\n\n{repository.RepoRoot}",
            cancellationToken);
    }

    private Task<bool> ShowStaleLockConfirmationAsync(
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
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(CancellationToken.None);
        if (operation is null)
        {
            return;
        }

        CancellationToken operationCancellation = operation.CancellationToken;
        string? markerFile = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            operationCancellation);
        if (markerFile is not null)
        {
            operationCancellation.ThrowIfCancellationRequested();
            await WarnConflictMarkersAsync(markerFile);
            operationCancellation.ThrowIfCancellationRequested();
        }
    }

    private async Task ShowConflictMarkerWarningAsync(string markerFile)
    {
        await _dispatcher.InvokeAsync(
            () => NotificationService.ShowWarning(
                Strings.VersionControl_ConflictMarkerWarningTitle,
                string.Format(
                    Strings.VersionControl_ConflictMarkerWarning,
                    markerFile)));
    }

    private async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VersionControlPickerFlyout? flyout = null;
        Task<bool>? confirmation = null;
        await _dispatcher.InvokeAsync(() =>
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
            () => _dispatcher.Post(flyout.Hide));
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

    private async Task ShowPolicyNoticeAsync(
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

        await _dispatcher.InvokeAsync(() =>
            NotificationService.ShowWarning(Strings.VersionControl, message));
    }

    private async Task CommitSnapshotAsync(
        bool enabled,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        IProjectVersionControlBackend? service = GetOperationReadyBackend();
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

    internal void OnProjectChanged(Project? project)
    {
        bool internalTransition = IsInternalVersionControlTransition();
        if (!TryBeginActivationSetup(internalTransition, out long activationRevision))
        {
            return;
        }

        _ = OnProjectChangedAsync(project, internalTransition, activationRevision);
    }

    private async Task OnProjectChangedAsync(
        Project? project,
        bool internalTransition,
        long activationRevision)
    {
        try
        {
            if (internalTransition)
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

            if (internalTransition && !TryPromoteActivationRevision(activationRevision))
            {
                return;
            }

            if (project is null)
            {
                ClearProjectState(activationRevision);
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
                activationRevision,
                projectRoot,
                service);
            if (BeginActivation(activation, out bool cleanupRejectedService))
            {
                _ = ActivateRepositoryAsync(activation);
            }
            else
            {
                await CompleteRejectedActivationAsync(activation, cleanupRejectedService)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ClearProjectState(activationRevision);
        }
        finally
        {
            FinishActivationSetup();
        }
    }

    private bool TryBeginActivationSetup(
        bool internalTransition,
        out long activationRevision)
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                activationRevision = 0;
                return false;
            }

            _activationSetupUsers++;
            activationRevision = ++_nextActivationRevision;
            if (!internalTransition)
            {
                _latestActivationRevision = activationRevision;
            }

            return true;
        }
    }

    private bool TryPromoteActivationRevision(long activationRevision)
    {
        lock (_stateGate)
        {
            if (_disposed || activationRevision < _latestActivationRevision)
            {
                return false;
            }

            _latestActivationRevision = activationRevision;
            return true;
        }
    }

    private void FinishActivationSetup()
    {
        TaskCompletionSource? quiesced = null;
        lock (_stateGate)
        {
            _activationSetupUsers--;
            if (_activationSetupUsers == 0 && _disposed)
            {
                quiesced = _activationSetupsQuiesced;
            }
        }

        quiesced?.TrySetResult();
    }

    private async Task CompleteRejectedActivationAsync(
        ActivationContext activation,
        bool cleanupService)
    {
        try
        {
            CancelActivation(activation);
            activation.Complete();
            await activation.CancellationQuiesced.ConfigureAwait(false);
            if (cleanupService)
            {
                await RetireDiscardedServiceAsync(
                        activation,
                        activation.Service,
                        cleanupAlreadyClaimed: true)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            activation.Finish();
        }
    }

    private async Task ActivateRepositoryAsync(ActivationContext activation)
    {
        IProjectVersionControlBackend? candidateService = null;
        IProjectVersionControlBackend? pendingCleanup = null;
        try
        {
            await activation.PredecessorsCompleted.ConfigureAwait(false);
            activation.CancellationToken.ThrowIfCancellationRequested();
            if (!TryPublishActivationServiceIfCurrent(activation))
            {
                return;
            }

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
            candidateService = trackedService;
            if (!TryRegisterCandidateService(activation, trackedService))
            {
                pendingCleanup = trackedService;
                return;
            }

            await activation.PredecessorsCompleted.ConfigureAwait(false);
            activation.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                await trackedService.EnsureRepositoryHygieneAsync(
                    activation.CancellationToken);
            }
            catch
            {
                if (activation.OwnsService(trackedService))
                {
                    ClearProjectState(activation.Revision);
                }
                else
                {
                    pendingCleanup = trackedService;
                }

                throw;
            }

            if (!CompleteActivation(activation, trackedService)
                && !activation.OwnsService(trackedService))
            {
                pendingCleanup = trackedService;
            }
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
            try
            {
                activation.Complete();
                await activation.CancellationQuiesced.ConfigureAwait(false);
                await activation.PredecessorsCompleted.ConfigureAwait(false);
                if (pendingCleanup is not null)
                {
                    await RetireDiscardedServiceAsync(activation, pendingCleanup)
                        .ConfigureAwait(false);
                }
                else if (candidateService is not null)
                {
                    if (activation.OwnsService(candidateService))
                    {
                        UnregisterCandidateService(activation, candidateService);
                    }
                    else
                    {
                        await RetireDiscardedServiceAsync(activation, candidateService)
                            .ConfigureAwait(false);
                    }
                }

                bool stillOwned;
                lock (_stateGate)
                {
                    if (ReferenceEquals(_activation, activation))
                    {
                        _activation = null;
                    }

                    stillOwned = ReferenceEquals(_state.OwnedService, activation.Service);
                }

                if (!stillOwned)
                {
                    await RetireDiscardedServiceAsync(activation, activation.Service)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                activation.Finish();
            }
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
        StartAvailabilityRefresh();
    }

    private void StartAvailabilityRefresh()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _availabilityUsers++;
        }

        _ = RefreshAvailabilityAsync();
    }

    private async Task RefreshAvailabilityAsync()
    {
        try
        {
            await GetAvailabilityAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            return;
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
        finally
        {
            FinishAvailabilityOperation();
        }
    }

    private void FinishAvailabilityOperation()
    {
        TaskCompletionSource? quiesced = null;
        lock (_stateGate)
        {
            _availabilityUsers--;
            if (_availabilityUsers == 0 && _disposed)
            {
                quiesced = _availabilityQuiesced;
            }
        }

        quiesced?.TrySetResult();
    }

    private bool BeginActivation(
        ActivationContext activation,
        out bool cleanupRejectedService)
    {
        ActivationContext? previousActivation;
        bool schedulePublication = false;
        bool waitsForPredecessors = false;
        bool rejected;
        lock (_stateGate)
        {
            rejected = _disposed
                       || activation.Revision != Volatile.Read(ref _latestActivationRevision)
                       || !CanAdoptServiceLocked(activation.Service);
            if (rejected)
            {
                previousActivation = null;
                cleanupRejectedService = TryClaimRejectedServiceCleanupLocked(
                    activation.Service);
            }
            else
            {
                previousActivation = _activation;
                LinkServiceUsersLocked(activation, activation.Service);
                _activation = activation;
                cleanupRejectedService = false;
                waitsForPredecessors =
                    !activation.PredecessorsCompleted.IsCompletedSuccessfully;
                schedulePublication = TransitionOwnedServiceLocked(
                    activation.Service,
                    !waitsForPredecessors
                        ? activation.Service
                        : null,
                    activation.ProjectRoot,
                    previousActivation,
                    out _);
            }
        }

        if (rejected)
        {
            return false;
        }

        SchedulePublicationDrain(schedulePublication);
        CancelActivation(previousActivation);

        return true;
    }

    private bool TryPublishActivationServiceIfCurrent(ActivationContext activation)
    {
        bool schedulePublication = false;
        bool accepted;
        lock (_stateGate)
        {
            accepted = IsCurrentActivationLocked(activation);
            if (accepted
                && (!activation.HasPredecessors
                    || activation.Service.Repository is null))
            {
                schedulePublication = TransitionStateLocked(
                    _state with
                    {
                        VisibleService = activation.Service,
                        IsTracked = activation.Service.Repository is not null,
                    });
            }
        }

        SchedulePublicationDrain(schedulePublication);
        return accepted;
    }

    private bool TryRegisterCandidateService(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        lock (_stateGate)
        {
            if (!IsCurrentActivationLocked(activation) || !CanAdoptServiceLocked(service))
            {
                if (IsServiceOwnedOrClaimedLocked(service))
                {
                    activation.MarkServiceCleanupDelegated(service);
                }

                return false;
            }

            LinkServiceUsersLocked(activation, service);
            if (!_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
            {
                users = [];
                _candidateServiceUsers.Add(service, users);
            }

            users.Add(activation);
            return true;
        }
    }

    private void LinkServiceUsersLocked(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        var predecessors = new HashSet<ActivationContext>();
        if (_activation is { } current
            && !ReferenceEquals(current, activation)
            && current.Revision < activation.Revision
            && current.OwnsService(service))
        {
            predecessors.Add(current);
        }

        if (_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
        {
            foreach (ActivationContext user in users)
            {
                if (!ReferenceEquals(user, activation)
                    && user.Revision < activation.Revision)
                {
                    predecessors.Add(user);
                }
            }
        }

        foreach (ActivationContext predecessor in predecessors)
        {
            activation.AddCompletionDependency(predecessor.Completion);
            predecessor.MarkServiceCleanupDelegated(service);
        }
    }

    private bool CanAdoptServiceLocked(IProjectVersionControlBackend service)
    {
        return !_managedServices.Contains(service)
               || ReferenceEquals(_state.OwnedService, service);
    }

    private bool IsServiceOwnedOrClaimedLocked(IProjectVersionControlBackend service)
    {
        return ReferenceEquals(_state.OwnedService, service)
               || _managedServices.Contains(service)
               || _candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users)
               && users.Count > 0;
    }

    private bool TryClaimRejectedServiceCleanupLocked(
        IProjectVersionControlBackend service)
    {
        return !IsServiceOwnedOrClaimedLocked(service) && _managedServices.Add(service);
    }

    private bool CompleteActivation(
        ActivationContext activation,
        IProjectVersionControlBackend trackedService)
    {
        bool accepted;
        bool schedulePublication = false;
        lock (_stateGate)
        {
            accepted = !_disposed
                       && ReferenceEquals(_activation, activation)
                       && activation.Revision == Volatile.Read(ref _latestActivationRevision)
                       && ReferenceEquals(_state.OwnedService, activation.Service)
                       && _state.ProjectRoot is { } projectRoot
                       && string.Equals(projectRoot, activation.ProjectRoot, PathComparison)
                       && !activation.CancellationToken.IsCancellationRequested
                       && CanAdoptServiceLocked(trackedService);
            if (accepted)
            {
                schedulePublication = TransitionOwnedServiceLocked(
                    trackedService,
                    trackedService,
                    activation.ProjectRoot,
                    activation,
                    out _);
                activation.TransferOwnership(trackedService);
            }
        }

        if (!accepted)
        {
            return false;
        }

        SchedulePublicationDrain(schedulePublication);
        return true;
    }

    private bool IsCurrentActivation(ActivationContext activation)
    {
        lock (_stateGate)
        {
            return IsCurrentActivationLocked(activation);
        }
    }

    private bool IsCurrentActivationLocked(ActivationContext activation)
    {
        return !_disposed
               && ReferenceEquals(_activation, activation)
               && activation.Revision == Volatile.Read(ref _latestActivationRevision)
               && ReferenceEquals(_state.OwnedService, activation.Service)
               && _state.ProjectRoot is { } projectRoot
               && string.Equals(projectRoot, activation.ProjectRoot, PathComparison)
               && !activation.CancellationToken.IsCancellationRequested;
    }

    private void ClearProjectState(long? expectedActivationRevision = null)
    {
        ActivationContext? activation;
        bool schedulePublication;
        lock (_stateGate)
        {
            if (expectedActivationRevision is { } expected
                && expected != Volatile.Read(ref _latestActivationRevision))
            {
                return;
            }

            activation = _activation;
            _activation = null;
            schedulePublication = TransitionOwnedServiceLocked(
                ownedService: null,
                visibleService: null,
                projectRoot: null,
                activation,
                out _);
        }

        SchedulePublicationDrain(schedulePublication);
        CancelActivation(activation);
    }

    private void CancelActivation(ActivationContext? activation)
    {
        if (activation is null)
        {
            return;
        }

        try
        {
            activation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An activation cancellation callback failed while version control state was transitioning.");
        }
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
        if (ownedService is not null)
        {
            _managedServices.Add(ownedService);
        }

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
        bool retirementWaitsForActivation = false;
        if (previous is not null && !ReferenceEquals(previous, ownedService))
        {
            retirementWaitsForActivation = retiringActivation?.OwnsService(previous) == true;
            Task activationReady = retirementWaitsForActivation
                ? retiringActivation!.Completion
                : Task.CompletedTask;
            retirement = new ServiceRetirement(previous, activationReady);
        }
        if (retirement is not null && retirementWaitsForActivation)
        {
            retiringActivation!.MarkServiceCleanupDelegated(previous!);
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

        if (_dispatcher.CheckAccess())
        {
            DrainStatePublications();
        }
        else
        {
            _dispatcher.Post(DrainStatePublications);
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
        TaskCompletionSource? drainQuiesced = null;
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
                    drainQuiesced = _publicationDrainQuiesced;
                    _publicationDrainQuiesced = null;
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

            try
            {
                if (disposeProperties)
                {
                    try
                    {
                        _isGitAvailable.Dispose();
                        _isTracked.Dispose();
                    }
                    finally
                    {
                        _propertiesDisposedCompletion.TrySetResult();
                    }
                }

                if (reschedule)
                {
                    _dispatcher.Post(DrainStatePublications);
                }
            }
            finally
            {
                drainQuiesced?.TrySetResult();
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

        if (_dispatcher.CheckAccess())
        {
            DisposeOnUiThread();
        }
        else
        {
            _dispatcher.Post(DisposeOnUiThread);
        }
    }

    private void RetireService(ServiceRetirement retirement)
    {
        lock (_stateGate)
        {
            _retirementUsers++;
        }

        _ = RetireServiceAsync(retirement);
    }

    private async Task RetireServiceAsync(ServiceRetirement retirement)
    {
        try
        {
            await retirement.ActivationReady.ConfigureAwait(false);
            await retirement.Service.RetireAsync(finalSnapshot: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a project version-control service.");
        }
        finally
        {
            DisposeService(retirement.Service);
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                _retirementUsers--;
                if (_retirementUsers == 0 && _disposed)
                {
                    quiesced = _retirementsQuiesced;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    private void UnregisterCandidateService(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        lock (_stateGate)
        {
            UnregisterCandidateServiceLocked(activation, service);
        }
    }

    private void UnregisterCandidateServiceLocked(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        if (_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
        {
            users.Remove(activation);
            if (users.Count == 0)
            {
                _candidateServiceUsers.Remove(service);
            }
        }
    }

    private async Task RetireDiscardedServiceAsync(
        ActivationContext activation,
        IProjectVersionControlBackend service,
        bool cleanupAlreadyClaimed = false)
    {
        bool cleanupService;
        lock (_stateGate)
        {
            UnregisterCandidateServiceLocked(activation, service);
            cleanupService = cleanupAlreadyClaimed
                             || !activation.IsServiceCleanupDelegated(service)
                             && !IsServiceOwnedOrClaimedLocked(service)
                             && _managedServices.Add(service);
            activation.MarkServiceCleanupDelegated(service);
        }

        if (!cleanupService)
        {
            return;
        }

        try
        {
            await service.RetireAsync(finalSnapshot: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a discarded project version-control service.");
        }
        finally
        {
            DisposeService(service);
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

    private void DetachRetiredService(IProjectVersionControlBackend service)
    {
        bool detached = false;
        bool schedulePublication = false;
        lock (_stateGate)
        {
            if (ReferenceEquals(_state.OwnedService, service))
            {
                if (service is IRepositoryLockRecoveryService recovery)
                {
                    recovery.RecoverableLockAvailable -= OnRecoverableLockAvailable;
                }

                detached = true;
                schedulePublication = TransitionStateLocked(
                    _state with
                    {
                        OwnedService = null,
                        VisibleService = null,
                        IsTracked = false,
                    });
            }
        }

        SchedulePublicationDrain(schedulePublication);
        if (detached)
        {
            DisposeService(service);
        }
    }

    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _lockRecoveryUsers++;
        }

        _ = RunLockRecoveryAsync(sender, lockInfo);
    }

    private async Task RunLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        try
        {
            if (_dispatcher.CheckAccess())
            {
                await OfferLockRecoveryAsync(sender, lockInfo);
            }
            else
            {
                await _dispatcher.InvokeAsync(
                    () => OfferLockRecoveryAsync(sender, lockInfo));
            }
        }
        finally
        {
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                _lockRecoveryUsers--;
                if (_lockRecoveryUsers == 0 && _disposed)
                {
                    quiesced = _lockRecoveryQuiesced;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    private async Task OfferLockRecoveryAsync(object? sender, RepositoryLockInfo lockInfo)
    {
        bool gateEntered = false;
        try
        {
            await _lockRecoveryGate.WaitAsync(_lifetimeCancellation.Token);
            gateEntered = true;
            if (_disposed
                || sender is not IRepositoryLockRecoveryService recovery
                || !ReferenceEquals(CurrentService, sender)
                || !Equals(recovery.RecoverableLock, lockInfo))
            {
                return;
            }

            if (!await ConfirmRemoveStaleLockAsync(lockInfo, _lifetimeCancellation.Token)
                || _disposed
                || !ReferenceEquals(CurrentService, sender)
                || !Equals(recovery.RecoverableLock, lockInfo))
            {
                return;
            }

            if (await recovery.RemoveRecoverableLockAsync(_lifetimeCancellation.Token))
            {
                _logger.LogWarning(
                    "Removed stale Git repository lock with user consent. Lock: {LockPath}, LastWriteTimeUtc: {LastWriteTimeUtc}",
                    lockInfo.LockPath,
                    lockInfo.LastWriteTimeUtc);
                await _dispatcher.InvokeAsync(() =>
                    NotificationService.ShowInformation(
                        Strings.VersionControl,
                        Strings.VersionControl_StaleLockRemoved));
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover a stale Git repository lock.");
        }
        finally
        {
            if (gateEntered)
            {
                _lockRecoveryGate.Release();
            }
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
        Task ActivationReady);

    private sealed class NonTransactionalCloseBarrier
    {
        private VersionControlCoordinator? _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationTokenSource _operationEpochCancellation;

        public NonTransactionalCloseBarrier(
            VersionControlCoordinator owner,
            CancellationTokenSource cancellation,
            CancellationTokenSource operationEpochCancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
            _operationEpochCancellation = operationEpochCancellation;
        }

        public CancellationToken CancellationToken => _cancellation.Token;

        public async Task CompleteAsync(bool projectClosed)
        {
            VersionControlCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            try
            {
                _cancellation.Dispose();
            }
            finally
            {
                await owner.CompleteNonTransactionalCloseBarrierAsync(
                        _operationEpochCancellation)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed class NonTransactionalOperationLease : IDisposable
    {
        private VersionControlCoordinator? _owner;
        private readonly CancellationTokenSource _cancellation;

        public NonTransactionalOperationLease(
            VersionControlCoordinator owner,
            CancellationTokenSource cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
        }

        public CancellationToken CancellationToken => _cancellation.Token;

        public void Dispose()
        {
            VersionControlCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            _cancellation.Dispose();
            owner.FinishNonTransactionalOperation();
        }
    }

    private sealed class ActivationContext
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource _cancellationQuiesced = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IProjectVersionControlBackend> _cleanupDelegatedServices = new(
            ReferenceEqualityComparer.Instance);
        private Task _completionDependency = Task.CompletedTask;
        private IProjectVersionControlBackend _ownedService;
        private int _activeCancellations;
        private bool _cleanupStarted;
        private bool _completionRequested;
        private bool _hasPredecessors;

        public ActivationContext(
            long revision,
            string projectRoot,
            IProjectVersionControlBackend service)
        {
            Revision = revision;
            ProjectRoot = projectRoot;
            Service = service;
            _ownedService = service;
        }

        public long Revision { get; }

        public string ProjectRoot { get; }

        public IProjectVersionControlBackend Service { get; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public Task CancellationQuiesced => _cancellationQuiesced.Task;

        public Task Completion => _completion.Task;

        public bool HasPredecessors
        {
            get
            {
                lock (_gate)
                {
                    return _hasPredecessors;
                }
            }
        }

        public Task PredecessorsCompleted
        {
            get
            {
                lock (_gate)
                {
                    return _completionDependency;
                }
            }
        }

        public bool OwnsService(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                return ReferenceEquals(_ownedService, service);
            }
        }

        public void TransferOwnership(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                _ownedService = service;
            }
        }

        public void AddCompletionDependency(Task completion)
        {
            lock (_gate)
            {
                _hasPredecessors = true;
                _completionDependency = Task.WhenAll(_completionDependency, completion);
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_cleanupStarted)
                {
                    return;
                }

                _activeCancellations++;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                bool cleanup;
                lock (_gate)
                {
                    _activeCancellations--;
                    cleanup = TryBeginCleanupLocked();
                }

                if (cleanup)
                {
                    FinishCleanup();
                }
            }
        }

        public void Complete()
        {
            bool cleanup;
            lock (_gate)
            {
                _completionRequested = true;
                cleanup = TryBeginCleanupLocked();
            }

            if (cleanup)
            {
                FinishCleanup();
            }
        }

        public bool IsServiceCleanupDelegated(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                return _cleanupDelegatedServices.Contains(service);
            }
        }

        public void MarkServiceCleanupDelegated(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                _cleanupDelegatedServices.Add(service);
            }
        }

        public void Finish()
        {
            _completion.TrySetResult();
        }

        private bool TryBeginCleanupLocked()
        {
            if (_cleanupStarted || !_completionRequested || _activeCancellations != 0)
            {
                return false;
            }

            _cleanupStarted = true;
            return true;
        }

        private void FinishCleanup()
        {
            try
            {
                _cancellation.Dispose();
            }
            finally
            {
                _cancellationQuiesced.TrySetResult();
            }
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
