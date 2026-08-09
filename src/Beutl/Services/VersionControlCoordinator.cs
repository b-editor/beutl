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
    IProjectVersionControlSession,
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
    private readonly Dictionary<ProjectService.ProjectCloseContext, NonTransactionalCloseBarrier>
        _preparedCloseBarriers = new();
    private readonly Dictionary<IProjectVersionControlBackend, HashSet<ActivationContext>>
        _candidateServiceUsers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IProjectVersionControlBackend> _managedServices = new(
        ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _offeredPendingRecoveryIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingOpeningPullRecovery> _openingPullRecoveries =
        new(PathComparer);
    private readonly TaskCompletionSource _propertiesDisposedCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _asyncDisposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private CoordinatorState _state = CoordinatorState.Empty;
    private ActivationContext? _activation;
    private TaskCompletionSource? _activationSetupsQuiesced;
    private TaskCompletionSource? _availabilityQuiesced;
    private TaskCompletionSource? _closeBarriersQuiesced;
    private TaskCompletionSource? _configurationActivationQuiesced;
    private TaskCompletionSource? _lifecycleQuiesced;
    private TaskCompletionSource? _lockRecoveryQuiesced;
    private TaskCompletionSource? _notificationsQuiesced;
    private TaskCompletionSource? _pendingRecoveryOffersQuiesced;
    private TaskCompletionSource? _operationsQuiesced;
    private TaskCompletionSource? _publicationDrainQuiesced;
    private TaskCompletionSource? _retirementsQuiesced;
    private CancellationTokenSource? _operationEpochCancellation = new();
    private CancellationTokenSource? _projectServiceEpochCancellation = new();
    private PendingRecoveryOfferContext? _pendingRecoveryOffer;
    private PendingOpeningRepositoryDecision? _pendingOpeningRepositoryDecision;
    private CancellationTokenSource? _configurationActivationCancellation;
    private ConfigurationActivationRequest? _pendingConfigurationActivation;
    private long _nextActivationRevision;
    private long _latestActivationRevision;
    private long _nextStateRevision;
    private long _nextConfigurationActivationRevision;
    private long _lastPublishedRevision;
    private int _availabilityRevision;
    private int _activationSetupUsers;
    private int _availabilityUsers;
    private int _closeBarrierUsers;
    private int _lifecycleUsers;
    private int _lockRecoveryUsers;
    private int _notificationUsers;
    private int _pendingRecoveryOfferUsers;
    private int _operationUsers;
    private int _retirementUsers;
    private int _asyncDisposalStarted;
    private Project? _lastProjectNotification;
    private bool _hasProjectNotification;
    private string? _observedGitExecutablePath;
    private bool _observedUseLfsWhenAvailable;
    private bool _publicationDrainScheduled;
    private bool _publicationDrainRunning;
    private bool _disposePropertiesRequested;
    private bool _configurationActivationActive;
    private bool _operationCloseBarrierActive;
    private bool _repositoryHygieneConfigurationDirty;
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
        _observedGitExecutablePath = NormalizeGitExecutablePath(config.GitExecutablePath);
        _observedUseLfsWhenAvailable = config.UseLfsWhenAvailable;
        _installationLocator = installationLocator ?? new GitInstallationLocator(config);
        _serviceFactory = serviceFactory;
        _dispatcher = Dispatcher.UIThread;
        ConfirmRestoreAsync = ShowRestoreConfirmationAsync;
        ConfirmSwitchBranchAsync = ShowSwitchBranchConfirmationAsync;
        ConfirmPullAsync = ShowPullConfirmationAsync;
        ConfirmPendingPullRecoveryAsync = ShowPendingPullRecoveryConfirmationAsync;
        ConfirmUseEnclosingRepositoryAsync = ShowEnclosingRepositoryConfirmationAsync;
        ConfirmRemoveStaleLockAsync = ShowStaleLockConfirmationAsync;
        WarnConflictMarkersAsync = ShowConflictMarkerWarningAsync;
        RequestIdentityAsync = static _ => Task.FromResult<GitIdentity?>(null);
        PresentPolicyNoticeAsync = ShowPolicyNoticeAsync;
        _config.ConfigurationChanged += OnVersionControlConfigChanged;
        _projectService.OpeningPreflight += PrepareProjectOpeningAsync;
        _projectService.Opening += InspectProjectOpeningAsync;
        _projectService.Closing += PrepareProjectClosingAsync;
        _projectService.ClosingFinalizing += NotifyProjectClosingAsync;
        _projectSubscription = _projectService.ProjectObservable.Subscribe(
            change => OnProjectChanged(change.New));
        _editorService.ProjectVersionControlCoordinator = this;
        ObserveCurrentProjectSnapshot();
        StartAvailabilityRefresh();
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

    public event EventHandler? PendingPullRecoveriesChanged;

    internal Func<CancellationToken, Task<bool>> ConfirmRestoreAsync { get; set; }

    internal Func<string, CancellationToken, Task<bool>> ConfirmSwitchBranchAsync { get; set; }

    internal Func<CancellationToken, Task<bool>> ConfirmPullAsync { get; set; }

    internal Func<ProjectRecoveryInfo, CancellationToken, Task<bool>>
        ConfirmPendingPullRecoveryAsync
    { get; set; }

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
            await BeginNonTransactionalOperationAsync(cancellationToken);
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
                await service.InitializeAsync(
                    options with { Identity = identity },
                    operationCancellation);
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
            await BeginNonTransactionalOperationAsync(cancellationToken);
        await CommitSnapshotAsync(
            _config.AutoCommitOnSave,
            SaveSnapshotMessage,
            SnapshotKind.Save,
            operation.CancellationToken);
    }

    private async Task PrepareProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (IsInternalVersionControlTransition())
        {
            AdvanceProjectServiceEpoch();
            return;
        }

        CancelPendingPullRecoveryOffer();
        NonTransactionalCloseBarrier? closeBarrier =
            await TryBeginNonTransactionalCloseBarrierAsync(cancellationToken)
                .ConfigureAwait(false);
        if (closeBarrier is null)
        {
            return;
        }

        AdvanceProjectServiceEpoch();

        bool completionRegistered = false;
        try
        {
            lock (_stateGate)
            {
                _preparedCloseBarriers.Add(closeContext, closeBarrier);
            }

            closeContext.RegisterCompletion(
                projectClosed => CompletePreparedCloseBarrierAsync(
                    closeContext,
                    closeBarrier,
                    projectClosed));
            completionRegistered = true;
        }
        finally
        {
            if (!completionRegistered)
            {
                lock (_stateGate)
                {
                    _preparedCloseBarriers.Remove(closeContext);
                }

                await closeBarrier.CompleteAsync(projectClosed: false).ConfigureAwait(false);
            }
        }
    }

    private async Task CompletePreparedCloseBarrierAsync(
        ProjectService.ProjectCloseContext closeContext,
        NonTransactionalCloseBarrier closeBarrier,
        bool projectClosed)
    {
        lock (_stateGate)
        {
            _preparedCloseBarriers.Remove(closeContext);
        }

        await closeBarrier.CompleteAsync(projectClosed).ConfigureAwait(false);
    }

    private async Task NotifyProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (IsInternalVersionControlTransition())
        {
            return;
        }

        NonTransactionalCloseBarrier? closeBarrier;
        lock (_stateGate)
        {
            _preparedCloseBarriers.TryGetValue(closeContext, out closeBarrier);
        }

        if (closeBarrier is not null)
        {
            await NotifyClosingCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task NotifyClosingCoreAsync(CancellationToken closeCancellation)
    {
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
            bool finalSnapshotRequested;
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
                finalSnapshotRequested = _config.AutoCommitOnClose;
            }

            bool snapshotRequiresReservation =
                finalSnapshotRequested && service.Repository is not null;
            using IDisposable? snapshotMutation = snapshotRequiresReservation
                ? TryBeginWorktreeMutation()
                : null;
            bool snapshotReserved = !snapshotRequiresReservation || snapshotMutation is not null;
            if (!snapshotReserved)
            {
                _logger.LogInformation(
                    "Skipped the {SnapshotKind} project snapshot because output is active.",
                    SnapshotKind.Close);
            }

            ProjectVersionControlFinalSnapshot? finalSnapshot;
            bool schedulePublication;
            lock (_stateGate)
            {
                if (_disposed
                    || projectRoot is null
                    || activationRevision != _latestActivationRevision
                    || _state.ProjectRoot is not { } currentRoot
                    || !string.Equals(currentRoot, projectRoot, PathComparison)
                    || !ReferenceEquals(_state.OwnedService, service))
                {
                    return;
                }

                finalSnapshot =
                    finalSnapshotRequested && snapshotReserved
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

    public Task<bool> RestoreAsync(
        string sha,
        CancellationToken cancellationToken = default)
    {
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        CancelPendingPullRecoveryOffer();
        return RunRestoreCycleAsync(sha, branchName: null, cancellationToken);
    }

    public Task<bool> RestoreToNewBranchAsync(
        string sha,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        CancelPendingPullRecoveryOffer();
        return RunRestoreCycleAsync(sha, branchName, cancellationToken);
    }

    public async Task<CommitResult> CommitManualAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
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
        CancelPendingPullRecoveryOffer();
        return RunBranchCycleAsync(branchName.Trim(), create: true, cancellationToken);
    }

    public Task<bool> SwitchBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        CancelPendingPullRecoveryOffer();
        return RunBranchCycleAsync(branchName.Trim(), create: false, cancellationToken);
    }

    public async Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        await GetTrackedBackend().SetRemoteAsync(url.Trim(), operation.CancellationToken);
    }

    public async Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        await GetTrackedBackend().SetLocalIdentityAsync(identity, operation.CancellationToken);
    }

    public async Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        return await GetTrackedBackend().PushAsync(progress, operation.CancellationToken);
    }

    public Task<RemoteOpResult> PullAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingPullRecoveryOffer();
        return RunPullCycleAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectRecoveryInfo>> GetPendingPullRecoveriesAsync(
        CancellationToken cancellationToken = default)
    {
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        IProjectVersionControlBackend service = GetTrackedBackend();
        IReadOnlyList<PendingPullRecovery> recoveries =
            await service.ExecuteExclusiveAsync(
                transaction => transaction.GetPendingPullRecoveriesAsync(
                    operation.CancellationToken),
                operation.CancellationToken);
        ReconcileOfferedPendingRecoveryIds(recoveries);
        return recoveries.Select(ToRecoveryInfo).ToArray();
    }

    public Task<ProjectRecoveryResult> RecoverPendingPullAsync(
        string recoveryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryId);
        CancelPendingPullRecoveryOffer();
        return RunPendingPullRecoveryCycleAsync(
            recoveryId,
            requireConfirmation: true,
            cancellationToken);
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
        CancellationTokenSource? configurationActivationCancellation;
        CancellationTokenSource? projectServiceEpochCancellation;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingConfigurationActivation = null;
            _pendingOpeningRepositoryDecision = null;
            _openingPullRecoveries.Clear();
            configurationActivationCancellation = _configurationActivationCancellation;
            projectServiceEpochCancellation = _projectServiceEpochCancellation;
            _projectServiceEpochCancellation = null;
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
        CancelConfigurationActivation(configurationActivationCancellation);
        CancelProjectServiceEpoch(projectServiceEpochCancellation);
        _config.ConfigurationChanged -= OnVersionControlConfigChanged;
        _projectService.OpeningPreflight -= PrepareProjectOpeningAsync;
        _projectService.Opening -= InspectProjectOpeningAsync;
        _projectService.Closing -= PrepareProjectClosingAsync;
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
            await WaitForPendingRecoveryOfferQuiescenceAsync().ConfigureAwait(false);
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

    private Task WaitForPendingRecoveryOfferQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_pendingRecoveryOfferUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_pendingRecoveryOffersQuiesced ??= CreateCompletionSource()).Task;
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
        await BeginLifecycleOperationAsync(cancellationToken);
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
                    if (create
                        && !await CanCreateBranchAsync(
                            service,
                            branchName,
                            cancellationToken))
                    {
                        return false;
                    }

                    if (!create
                        && !await LocalBranchExistsAsync(
                            service,
                            branchName,
                            cancellationToken))
                    {
                        return false;
                    }

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
                    if (create
                        && !await CanCreateBranchAsync(
                            service,
                            branchName,
                            CancellationToken.None))
                    {
                        return false;
                    }

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

                    if (create
                        && !await CanCreateBranchAsync(
                            service,
                            branchName,
                            CancellationToken.None))
                    {
                        return false;
                    }

                    if (!create
                        && !await LocalBranchExistsAsync(
                            service,
                            branchName,
                            CancellationToken.None))
                    {
                        return false;
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

    private static async Task<bool> LocalBranchExistsAsync(
        IProjectVersionControlTransaction service,
        string branchName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(cancellationToken);
        return branches.Any(branch =>
            string.Equals(branch.Name, branchName, StringComparison.Ordinal));
    }

    private static Task<bool> CanCreateBranchAsync(
        IProjectVersionControlTransaction service,
        string branchName,
        CancellationToken cancellationToken)
        => service.CanCreateBranchAsync(branchName, cancellationToken);

    private async Task<RemoteOpResult> RunPullCycleAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? confirmationCancellation = null;
        try
        {
            confirmationCancellation =
                CreateProjectServiceEpochCancellation(cancellationToken);
            RemoteOpResult? preliminaryResult =
                await RunPullPreflightCycleAsync(confirmationCancellation.Token);
            if (preliminaryResult is not null)
            {
                return preliminaryResult;
            }

            if (!await ConfirmPullAsync(confirmationCancellation.Token).ConfigureAwait(false))
            {
                return new RemoteOpResult.Failed(string.Empty);
            }
        }
        catch (OperationCanceledException)
            when (confirmationCancellation?.IsCancellationRequested == true
                  && !cancellationToken.IsCancellationRequested)
        {
            return new RemoteOpResult.Failed(
                "The open project changed while the pull was awaiting confirmation.");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because its project/service epoch was unavailable before confirmation.");
            return new RemoteOpResult.Failed(
                "The open project changed while the pull was being prepared.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because the project lifecycle changed before confirmation.");
            return new RemoteOpResult.Failed(
                "The open project changed while the pull was being prepared.");
        }
        finally
        {
            confirmationCancellation?.Dispose();
        }

        PullMutationOutcome outcome;
        try
        {
            outcome = await RunPullMutationCycleAsync(cancellationToken);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because its backend retired after confirmation.");
            return new RemoteOpResult.Failed(
                "The open project changed while the pull was being prepared.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because the project lifecycle changed after confirmation.");
            return new RemoteOpResult.Failed(
                "The open project changed while the pull was being prepared.");
        }

        if (outcome.Recovery is not null)
        {
            await OfferUncertainPullRecoveryAsync(
                    outcome.Recovery,
                    outcome.ProjectFile)
                .ConfigureAwait(false);
        }

        return outcome.Result;
    }

    private async Task<RemoteOpResult?> RunPullPreflightCycleAsync(
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            return await ownedService.ExecuteExclusiveAsync(
                async service =>
                {
                    WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                    if (!EnsureRepositoryIsNotConflicted(status))
                    {
                        return new RemoteOpResult.Failed(
                            Strings.VersionControl_ConflictGuidance);
                    }

                    CheckedOutBranchTip originalHead =
                        await service.GetCheckedOutBranchTipAsync(cancellationToken);
                    PullPreflightResult preflight = await service.PreflightPullAsync(
                        originalHead,
                        cancellationToken);
                    return preflight.Result is RemoteOpResult.Success
                           && preflight.RequiresTransition
                        ? null
                        : preflight.Result;
                },
                cancellationToken);
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private async Task<PullMutationOutcome> RunPullMutationCycleAsync(
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            cancellationToken.ThrowIfCancellationRequested();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(
                    this,
                    cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return new PullMutationOutcome(
                    new RemoteOpResult.Failed(Strings.VersionControl_ExportInProgress),
                    null,
                    projectFile);
            }

            try
            {
                if (!ReferenceEquals(_projectService.CurrentProject.Value, project)
                    || !ReferenceEquals(GetOwnedBackend(), ownedService))
                {
                    return new PullMutationOutcome(
                        new RemoteOpResult.Failed(
                            "The open project changed while the pull was being prepared."),
                        null,
                        projectFile);
                }

                PendingPullRecovery? recoveryToOffer = null;
                RemoteOpResult result = await ownedService.ExecuteExclusiveAsync(
                    async service =>
                    {
                        WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                        if (!EnsureRepositoryIsNotConflicted(status))
                        {
                            return new RemoteOpResult.Failed(
                                Strings.VersionControl_ConflictGuidance);
                        }

                        CheckedOutBranchTip originalHead =
                            await service.GetCheckedOutBranchTipAsync(cancellationToken);
                        PullPreflightResult preflight = await service.PreflightPullAsync(
                            originalHead,
                            cancellationToken);
                        if (preflight.Result is not RemoteOpResult.Success
                            || !preflight.RequiresTransition)
                        {
                            return preflight.Result;
                        }

                        ProjectCheckpoint? checkpoint = status.IsClean
                            ? null
                            : await service.CreateProjectCheckpointAsync(
                                PullSafetySnapshotMessage,
                                CancellationToken.None);
                        bool projectClosed = false;
                        CheckedOutBranchTip expectedCurrentHead = originalHead;
                        PendingPullRecovery? pendingRecovery = null;
                        PullTransitionState pullTransitionState = PullTransitionState.Unchanged;
                        try
                        {
                            await CloseProjectForOperationAsync(transition, CancellationToken.None);
                            projectClosed = true;
                            FastForwardPullResult pull = await service.PullFastForwardAsync(
                                originalHead,
                                checkpoint,
                                projectFile,
                                CancellationToken.None);

                            RemoteOpResult result = pull.Result;
                            expectedCurrentHead = pull.Tip;
                            pendingRecovery = pull.Recovery;
                            if (pendingRecovery is not null)
                            {
                                PublishPendingPullRecoveriesChanged();
                            }
                            pullTransitionState = pull.TransitionState;
                            if (pullTransitionState is PullTransitionState.OwnershipLost
                                or PullTransitionState.RecoveryFailed)
                            {
                                recoveryToOffer = pendingRecovery;
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
                                    _logger.LogError(
                                        recoveryFailure,
                                        "Failed to recover a pull after {PullError}.",
                                        GetRemoteOperationError(result));
                                    recoveryToOffer = pendingRecovery;
                                    return new RemoteOpResult.Failed(
                                        Strings.VersionControl_PullTransitionUncertain);
                                }

                                await TryCompletePullRecoveryAsync(
                                    service,
                                    pendingRecovery,
                                    checkpoint);
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
                            await TryCompletePullRecoveryAsync(
                                service,
                                pendingRecovery,
                                checkpoint);
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
                                recoveryToOffer = pendingRecovery;
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
                                await TryCompletePullRecoveryAsync(
                                    service,
                                    pendingRecovery,
                                    checkpoint);
                            }

                            if (recoveryFailure is not null)
                            {
                                _logger.LogError(
                                    recoveryFailure,
                                    "Failed to recover a pull after {PullError}.",
                                    GetErrorText(ex));
                                recoveryToOffer = pendingRecovery;
                                return new RemoteOpResult.Failed(
                                    Strings.VersionControl_PullTransitionUncertain);
                            }

                            _logger.LogError(ex, "Failed to pull project versions.");
                            return new RemoteOpResult.Failed(GetErrorText(ex));
                        }
                    },
                    cancellationToken);
                return new PullMutationOutcome(result, recoveryToOffer, projectFile);
            }
            finally
            {
                FinishInternalTransition();
            }
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private async Task OfferUncertainPullRecoveryAsync(
        PendingPullRecovery recovery,
        string projectFile)
    {
        bool recovered;
        using (NonTransactionalOperationLease? operation =
               TryBeginNonTransactionalOperation(CancellationToken.None))
        {
            if (operation is null)
            {
                return;
            }

            recovered = await TryRecoverPendingPullBeforeOpeningAsync(
                    projectFile,
                    operation.CancellationToken,
                    recovery.Id)
                .ConfigureAwait(false);
        }

        if (!recovered || !File.Exists(projectFile))
        {
            return;
        }

        try
        {
            await _projectService.OpenProject(projectFile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The pending pull state {RecoveryId} was recovered, but its project could not be opened.",
                recovery.Id);
        }
    }

    private async Task<ProjectRecoveryResult> RunPendingPullRecoveryCycleAsync(
        string recoveryId,
        bool requireConfirmation,
        CancellationToken cancellationToken,
        PendingPullRecovery? confirmedRecovery = null)
    {
        CancellationTokenSource? confirmationCancellation = null;
        CancellationToken lookupCancellation = default;
        try
        {
            if (requireConfirmation)
            {
                confirmationCancellation =
                    CreateProjectServiceEpochCancellation(cancellationToken);
                using (NonTransactionalOperationLease operation =
                       await BeginNonTransactionalOperationAsync(
                           confirmationCancellation.Token))
                {
                    lookupCancellation = operation.CancellationToken;
                    Project project = GetOpenProject();
                    string projectFile = GetProjectFile(project);
                    IProjectVersionControlBackend service = GetTrackedBackend();
                    confirmedRecovery = await service.ExecuteExclusiveAsync(
                        async transaction =>
                            (await transaction.GetPendingPullRecoveriesAsync(
                                operation.CancellationToken))
                            .SingleOrDefault(candidate => string.Equals(
                                candidate.Id,
                                recoveryId,
                                StringComparison.Ordinal)),
                        operation.CancellationToken);
                    if (confirmedRecovery is null
                        || !RecoveryProjectPathsEqual(
                            projectFile,
                            confirmedRecovery.ProjectFile))
                    {
                        return new ProjectRecoveryResult.NotFoundOrChanged();
                    }
                }

                if (!await ConfirmPendingPullRecoveryAsync(
                        ToRecoveryInfo(confirmedRecovery),
                        confirmationCancellation.Token)
                    .ConfigureAwait(false))
                {
                    return new ProjectRecoveryResult.Declined();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (confirmationCancellation?.IsCancellationRequested == true
                  || lookupCancellation.IsCancellationRequested
                  || _lifetimeCancellation.IsCancellationRequested)
        {
            return new ProjectRecoveryResult.Unavailable();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because its backend retired before confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because it became unavailable before confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
        finally
        {
            confirmationCancellation?.Dispose();
        }

        try
        {
            return await RunPendingPullRecoveryMutationCycleAsync(
                    recoveryId,
                    confirmedRecovery,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because its backend retired after confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pending pull recovery because the project lifecycle changed after confirmation.");
            return new ProjectRecoveryResult.Unavailable();
        }
    }

    private async Task<ProjectRecoveryResult> RunPendingPullRecoveryMutationCycleAsync(
        string recoveryId,
        PendingPullRecovery? confirmedRecovery,
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            Project project = GetOpenProject();
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            PendingPullRecovery? offeredRecovery = await ownedService.ExecuteExclusiveAsync(
                async service => (await service.GetPendingPullRecoveriesAsync(cancellationToken))
                    .SingleOrDefault(candidate => string.Equals(
                        candidate.Id,
                        recoveryId,
                        StringComparison.Ordinal)),
                cancellationToken);
            if (offeredRecovery is null)
            {
                return new ProjectRecoveryResult.NotFoundOrChanged();
            }

            string openProjectFile = GetProjectFile(project);
            if (!RecoveryProjectPathsEqual(
                    openProjectFile,
                    offeredRecovery.ProjectFile)
                || confirmedRecovery is not null
                && !PendingPullRecoveriesMatch(confirmedRecovery, offeredRecovery))
            {
                return new ProjectRecoveryResult.NotFoundOrChanged();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(
                    this,
                    cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
            if (!ReferenceEquals(_projectService.CurrentProject.Value, project)
                || !ReferenceEquals(GetOwnedBackend(), ownedService))
            {
                return new ProjectRecoveryResult.Unavailable();
            }

            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return new ProjectRecoveryResult.Unavailable();
            }

            try
            {
                return await ownedService.ExecuteExclusiveAsync(
                    async service =>
                    {
                        PendingPullRecovery? recovery =
                            (await service.GetPendingPullRecoveriesAsync(cancellationToken))
                            .SingleOrDefault(candidate => string.Equals(
                                candidate.Id,
                                recoveryId,
                                StringComparison.Ordinal));
                        if (recovery is null
                            || !PendingPullRecoveriesMatch(offeredRecovery, recovery)
                            || !RecoveryProjectPathsEqual(
                                openProjectFile,
                                recovery.ProjectFile))
                        {
                            return new ProjectRecoveryResult.NotFoundOrChanged();
                        }

                        try
                        {
                            await CloseProjectForOperationAsync(
                                transition,
                                CancellationToken.None);
                            PendingPullRecoveryOutcome outcome =
                                await service.RecoverPendingPullRecoveryAsync(
                                recovery,
                                CancellationToken.None);
                            await ReopenProjectAsync(transition, openProjectFile);
                            await service.CompletePendingPullRecoveryAsync(
                                recovery,
                                CancellationToken.None);
                            CompletePendingPullRecoveryPublication(recovery.Id);
                            PublishRecoveryOutcomeNotification(recovery, outcome);
                            return ToProjectRecoveryResult(recovery, outcome);
                        }
                        catch (PendingPullRecoveryPreservedException ex)
                        {
                            PublishPreservedRecoveryBranchNotification(ex.RecoveryReference);
                            return new ProjectRecoveryResult.FailedPreserved(
                                ex.RecoveryReference);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Failed to recover pending pull state {RecoveryId}; its retained-reference state could not be verified.",
                                recovery.Id);
                            PublishNotification(() =>
                                NotificationService.ShowError(
                                    Strings.VersionControl_ErrorTitle,
                                    string.Format(
                                        Strings.VersionControl_RecoveryFailed,
                                        Strings.VersionControl_PullTransitionUncertain,
                                        GetErrorText(ex))));
                            return new ProjectRecoveryResult.FailedUncertain();
                        }
                    },
                    cancellationToken);
            }
            finally
            {
                FinishInternalTransition();
            }
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private static ProjectRecoveryInfo ToRecoveryInfo(PendingPullRecovery recovery)
    {
        return new ProjectRecoveryInfo(
            recovery.Id,
            Path.GetFileName(recovery.ProjectFile),
            recovery.CreatedAt);
    }

    private static ProjectRecoveryResult ToProjectRecoveryResult(
        PendingPullRecovery recovery,
        PendingPullRecoveryOutcome outcome)
    {
        return outcome switch
        {
            PendingPullRecoveryOutcome.RestoredOriginal
                => new ProjectRecoveryResult.RestoredOriginal(),
            PendingPullRecoveryOutcome.ReappliedCheckpoint
                => new ProjectRecoveryResult.ReappliedCheckpoint(
                    recovery.RecoveryBranchName),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
    }

    private void PublishRecoveryOutcomeNotification(
        PendingPullRecovery recovery,
        PendingPullRecoveryOutcome outcome)
    {
        PublishNotification(() => NotificationService.ShowInformation(
            Strings.VersionControl,
            outcome == PendingPullRecoveryOutcome.ReappliedCheckpoint
                ? string.Format(
                    Strings.VersionControl_CheckpointReappliedOnRecoveryBranch,
                    recovery.RecoveryBranchName)
                : Strings.VersionControl_PullRecovered));
    }

    private void PublishPreservedRecoveryBranchNotification(string recoveryReference)
    {
        PublishNotification(() => NotificationService.ShowWarning(
            Strings.VersionControl,
            string.Format(
                Strings.VersionControl_CheckpointPreservedOnRecoveryBranch,
                recoveryReference)));
    }

    private static bool PendingPullRecoveriesMatch(
        PendingPullRecovery expected,
        PendingPullRecovery actual,
        RepositoryInfo? repository = null)
    {
        return string.Equals(expected.Id, actual.Id, StringComparison.Ordinal)
               && string.Equals(
                   expected.DescriptorRef,
                   actual.DescriptorRef,
                   StringComparison.Ordinal)
               && string.Equals(
                   expected.DescriptorObject,
                   actual.DescriptorObject,
                   StringComparison.OrdinalIgnoreCase)
               && (repository is null
                   ? RecoveryProjectPathsEqual(
                       expected.ProjectFile,
                       actual.ProjectFile)
                   : RecoveryProjectPathsEqual(
                       repository,
                       expected.ProjectFile,
                       actual.ProjectFile));
    }

    private static bool PathsEqual(string left, string right)
    {
        return RepositoryPathComparer.AreEquivalent(left, right);
    }

    private static bool RecoveryProjectPathsEqual(string left, string right)
    {
        string lexicalLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        string lexicalRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        if (string.Equals(lexicalLeft, lexicalRight, PathComparison))
        {
            return true;
        }

        try
        {
            return PathsEqual(left, right);
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or ArgumentException)
        {
            return false;
        }
    }

    private static bool RecoveryProjectPathsEqual(
        RepositoryInfo repository,
        string left,
        string right)
    {
        if (TryGetRecoveryRelativePath(repository, left, out string? leftRelative)
            && TryGetRecoveryRelativePath(repository, right, out string? rightRelative)
            && string.Equals(leftRelative, rightRelative, PathComparison))
        {
            return true;
        }

        return RecoveryProjectPathsEqual(left, right);
    }

    private static bool TryGetRecoveryRelativePath(
        RepositoryInfo repository,
        string path,
        out string? relativePath)
    {
        string fullPath = Path.GetFullPath(path);
        string? ancestor = Path.GetDirectoryName(fullPath);
        while (ancestor is not null)
        {
            try
            {
                if (RepositoryPathComparer.AreEquivalent(
                        ancestor,
                        repository.ProjectRoot))
                {
                    relativePath = Path.GetRelativePath(ancestor, fullPath);
                    return relativePath != ".."
                           && !relativePath.StartsWith(
                               $"..{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !Path.IsPathRooted(relativePath);
                }
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException
                      or ArgumentException)
            {
                // A mutated child link must not prevent finding a safe lexical root ancestor.
            }

            ancestor = Path.GetDirectoryName(ancestor);
        }

        relativePath = null;
        return false;
    }

    private static string GetOpeningRecoveryKey(string projectFile)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFile));
    }

    private static void EnsurePendingRecoveryPathIsSafeForOpen(
        RepositoryInfo? repository,
        PendingPullRecovery? recovery,
        string projectFile)
    {
        if (repository is not null && recovery is not null)
        {
            EnsureProjectFileIsPhysicallyContained(repository, projectFile);
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

    private async Task TryCompletePullRecoveryAsync(
        IProjectVersionControlTransaction service,
        PendingPullRecovery? recovery,
        ProjectCheckpoint? checkpoint)
    {
        if (recovery is null)
        {
            await TryDeleteCheckpointAsync(service, checkpoint);
            return;
        }

        try
        {
            await service.CompletePendingPullRecoveryAsync(
                recovery,
                CancellationToken.None);
            CompletePendingPullRecoveryPublication(recovery.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete completed pending pull recovery {RecoveryId}.",
                recovery.Id);
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
        await BeginLifecycleOperationAsync(cancellationToken);
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
                    if (branchName is not null
                        && !await CanCreateBranchAsync(
                            service,
                            branchName,
                            cancellationToken))
                    {
                        return false;
                    }

                    if (!await service.RevisionContainsProjectFileAsync(
                            sha,
                            projectFile,
                            cancellationToken))
                    {
                        PublishNotification(() =>
                            NotificationService.ShowWarning(
                                Strings.VersionControl,
                                string.Format(
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    Strings.VersionControl_RevisionMissingProject,
                                    GetShortSha(sha))));
                        return false;
                    }

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
                    if (branchName is not null
                        && !await CanCreateBranchAsync(
                            service,
                            branchName,
                            CancellationToken.None))
                    {
                        return false;
                    }

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

                    if (branchName is not null
                        && !await CanCreateBranchAsync(
                            service,
                            branchName,
                            CancellationToken.None))
                    {
                        return false;
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
        RepositoryInfo repository = GetOwnedBackend()?.Repository
                                    ?? throw new InvalidOperationException(
                                        "The repository is unavailable before reopening the project.");
        EnsureProjectFileIsPhysicallyContained(repository, projectFile);
        await transition.OpenProjectAsync(projectFile);
        EnsureProjectReopened(projectFile);
    }

    private static void EnsureProjectFileIsPhysicallyContained(
        RepositoryInfo repository,
        string projectFile)
    {
        EnsureProjectFileIsPhysicallyContained(repository.ProjectRoot, projectFile);
    }

    private static void EnsureProjectFileIsPhysicallyContained(
        string projectRoot,
        string projectFile)
    {
        if (!RepositoryPathComparer.IsContainedWithin(projectRoot, projectFile))
        {
            throw new InvalidOperationException(
                $"The project file '{projectFile}' resolves outside the version-controlled project root.");
        }
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

    private async Task BeginLifecycleOperationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? configurationActivation;
            lock (_stateGate)
            {
                ThrowIfLifecycleOperationUnavailableLocked();
                if (_configurationActivationActive)
                {
                    configurationActivation =
                        (_configurationActivationQuiesced ??= CreateCompletionSource()).Task;
                }
                else
                {
                    _lifecycleUsers++;
                    return;
                }
            }

            await configurationActivation.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private CancellationTokenSource CreateProjectServiceEpochCancellation(
        CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            ThrowIfLifecycleOperationUnavailableLocked();
            CancellationToken projectServiceEpoch =
                (_projectServiceEpochCancellation
                 ?? throw new ObjectDisposedException(nameof(VersionControlCoordinator)))
                .Token;
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token,
                projectServiceEpoch);
        }
    }

    private void AdvanceProjectServiceEpoch()
    {
        CancellationTokenSource? previous;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _projectServiceEpochCancellation;
            _projectServiceEpochCancellation = new CancellationTokenSource();
        }

        CancelProjectServiceEpoch(previous);
    }

    private void CancelProjectServiceEpoch(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A project/service epoch cancellation callback failed.");
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async ValueTask<NonTransactionalOperationLease> BeginNonTransactionalOperationAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? configurationActivation;
            CancellationToken operationEpochCancellation = default;
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_operationCloseBarrierActive)
                {
                    throw new InvalidOperationException(
                        "Version-control operations cannot start while the project is closing.");
                }

                if (_configurationActivationActive)
                {
                    configurationActivation =
                        (_configurationActivationQuiesced ??= CreateCompletionSource()).Task;
                }
                else
                {
                    configurationActivation = null;
                    operationEpochCancellation = (_operationEpochCancellation
                                                  ?? throw new ObjectDisposedException(
                                                      nameof(VersionControlCoordinator)))
                        .Token;
                    _operationUsers++;
                }
            }

            if (configurationActivation is not null)
            {
                await configurationActivation.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
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
    }

    private NonTransactionalOperationLease? TryBeginNonTransactionalOperation(
        CancellationToken cancellationToken)
    {
        CancellationToken operationEpochCancellation;
        lock (_stateGate)
        {
            if (_disposed || _operationCloseBarrierActive || _configurationActivationActive)
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
            TryStartPendingConfigurationActivation();
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
                TryStartPendingConfigurationActivation();
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
        CancellationTokenSource operationEpochCancellation,
        bool projectClosed)
    {
        if (projectClosed)
        {
            lock (_stateGate)
            {
                _pendingConfigurationActivation = null;
            }
        }

        FinishNonTransactionalCloseBarrier(operationEpochCancellation);
        TryStartPendingConfigurationActivation();
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
            TryStartPendingConfigurationActivation();
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
            TryStartPendingConfigurationActivation();
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

    private Task<bool> ShowPendingPullRecoveryConfirmationAsync(
        ProjectRecoveryInfo recovery,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            Strings.VersionControl,
            string.Format(
                Strings.VersionControl_PendingPullRecoveryConfirmation,
                recovery.ProjectFileName,
                recovery.CreatedAt.ToLocalTime()),
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

    private async Task<ProjectService.ProjectOpenPreparation?> PrepareProjectOpeningAsync(
        ProjectService.ProjectOpenAttempt attempt,
        CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (_pendingOpeningRepositoryDecision is { } pending
                && !ReferenceEquals(pending.Attempt, attempt))
            {
                _pendingOpeningRepositoryDecision = null;
            }
        }

        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(cancellationToken);
        if (operation is null)
        {
            return new AbortProjectOpenPreparation();
        }

        OpeningRepositoryInspection? inspection = null;
        try
        {
            inspection = await DiscoverPendingPullRecoveryForOpeningAsync(
                    attempt.ProjectFile,
                    operation.CancellationToken)
                .ConfigureAwait(false);
            if (inspection is null
                || !inspection.Repository.IsNestedInForeignRepo
                && inspection.Recovery is null)
            {
                return null;
            }

            PendingPullRecoveryOpenSelection? selection = inspection.Recovery;
            bool accepted = selection is null
                            || selection.AlreadyApplied
                            || await ConfirmPendingPullRecoveryAsync(
                                ToRecoveryInfo(selection.Recovery),
                                operation.CancellationToken);
            return new VersionControlProjectOpenPreparation(
                this,
                attempt,
                inspection with
                {
                    Recovery = selection is null
                        ? null
                        : selection with { Accepted = accepted },
                });
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to inspect pending pull recovery before opening {ProjectFile}.",
                attempt.ProjectFile);
            return new AbortProjectOpenPreparation();
        }
    }

    private async Task InspectProjectOpeningAsync(string projectFile)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(CancellationToken.None);
        if (operation is null)
        {
            return;
        }

        CancellationToken cancellationToken = operation.CancellationToken;
        string? markerFile = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            cancellationToken);
        if (markerFile is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WarnConflictMarkersAsync(markerFile);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task<bool> TryRecoverPendingPullBeforeOpeningAsync(
        string projectFile,
        CancellationToken cancellationToken,
        string? requiredRecoveryId = null)
    {
        PendingPullRecoveryOpenSelection? selection = null;
        try
        {
            OpeningRepositoryInspection? inspection =
                await DiscoverPendingPullRecoveryForOpeningAsync(
                    projectFile,
                    cancellationToken,
                    requiredRecoveryId)
                .ConfigureAwait(false);
            selection = inspection?.Recovery;
            if (selection is null)
            {
                return false;
            }

            bool accepted = selection.AlreadyApplied
                            || await ConfirmPendingPullRecoveryAsync(
                                ToRecoveryInfo(selection.Recovery),
                                cancellationToken);
            selection = selection with { Accepted = accepted };
            if (!accepted)
            {
                IsPendingRecoveryPathSafeForOpen(selection);
                return false;
            }

            ProjectOpenPreparationResult result =
                await ApplyPendingPullRecoveryBeforeOpeningAsync(
                        selection,
                        cancellationToken)
                    .ConfigureAwait(false);
            return result == ProjectOpenPreparationResult.Proceed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to inspect pending pull recovery before opening {ProjectFile}.",
                projectFile);
            if (selection is not null)
            {
                IsPendingRecoveryPathSafeForOpen(selection);
            }

            return false;
        }
    }

    private async Task<OpeningRepositoryInspection?>
        DiscoverPendingPullRecoveryForOpeningAsync(
            string projectFile,
            CancellationToken cancellationToken,
            string? requiredRecoveryId = null)
    {
        string canonicalProjectFile = GetOpeningRecoveryKey(projectFile);
        PendingOpeningPullRecovery? cleanupCandidate;
        lock (_stateGate)
        {
            _openingPullRecoveries.TryGetValue(
                canonicalProjectFile,
                out cleanupCandidate);
        }

        IProjectVersionControlBackend? discoveryService = null;
        IProjectVersionControlBackend? trackedService = null;
        try
        {
            discoveryService = CreateTemporaryBackend(repository: null, projectFile);
            GitAvailability availability = await discoveryService.GetAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return null;
            }

            string projectRoot = Path.GetDirectoryName(projectFile)
                                 ?? throw new InvalidOperationException(
                                     "The project file has no parent directory.");
            RepositoryInfo? repository = await discoveryService.DiscoverRepositoryAsync(
                    projectRoot,
                    cancellationToken)
                .ConfigureAwait(false);
            if (repository is null)
            {
                return null;
            }

            bool enclosingRepositoryAccepted = !repository.IsNestedInForeignRepo
                                                || await ConfirmUseEnclosingRepositoryAsync(
                                                    repository,
                                                    cancellationToken);
            if (!enclosingRepositoryAccepted)
            {
                return new OpeningRepositoryInspection(
                    repository,
                    projectFile,
                    EnclosingRepositoryAccepted: false,
                    Recovery: null);
            }

            trackedService = CreateTemporaryBackend(repository, projectFile);
            IReadOnlyList<PendingPullRecovery> recoveries =
                await trackedService.ExecuteExclusiveAsync(
                        transaction => transaction.GetPendingPullRecoveriesAsync(cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            PendingPullRecovery? recovery = recoveries
                .Where(candidate => RecoveryProjectPathsEqual(
                                        repository,
                                        candidate.ProjectFile,
                                        projectFile)
                                    && (requiredRecoveryId is null
                                        || string.Equals(
                                            candidate.Id,
                                            requiredRecoveryId,
                                            StringComparison.Ordinal)))
                .OrderBy(static candidate => candidate.CreatedAt)
                .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (recovery is null)
            {
                if (requiredRecoveryId is null && cleanupCandidate is not null)
                {
                    lock (_stateGate)
                    {
                        if (_openingPullRecoveries.TryGetValue(
                                canonicalProjectFile,
                                out PendingOpeningPullRecovery? current)
                            && ReferenceEquals(current, cleanupCandidate))
                        {
                            _openingPullRecoveries.Remove(canonicalProjectFile);
                        }
                    }
                }

                return new OpeningRepositoryInspection(
                    repository,
                    projectFile,
                    EnclosingRepositoryAccepted: true,
                    Recovery: null);
            }

            PendingOpeningPullRecovery? appliedMarker = null;
            lock (_stateGate)
            {
                if (_openingPullRecoveries.TryGetValue(
                        canonicalProjectFile,
                        out PendingOpeningPullRecovery? liveMarker)
                    && liveMarker is not null
                    && liveMarker.Repository.Equals(repository)
                    && PendingPullRecoveriesMatch(
                        liveMarker.Recovery,
                        recovery,
                        repository))
                {
                    appliedMarker = liveMarker;
                }
            }

            return new OpeningRepositoryInspection(
                repository,
                projectFile,
                EnclosingRepositoryAccepted: true,
                Recovery: new PendingPullRecoveryOpenSelection(
                    repository,
                    recovery,
                    projectFile,
                    Accepted: false,
                    AppliedMarker: appliedMarker));
        }
        finally
        {
            if (!ReferenceEquals(trackedService, discoveryService))
            {
                DisposeService(trackedService);
            }

            DisposeService(discoveryService);
        }
    }

    private async Task<ProjectOpenPreparationResult> ApplyProjectOpeningPreparationAsync(
        ProjectService.ProjectOpenAttempt attempt,
        OpeningRepositoryInspection inspection,
        ProjectTransitionContext transition,
        CancellationToken cancellationToken)
    {
        try
        {
            if (transition.Purpose != ProjectTransitionPurpose.Normal
                || !ReferenceEquals(transition.Owner, attempt)
                || !PathsEqual(attempt.ProjectFile, inspection.ProjectFile))
            {
                return ProjectOpenPreparationResult.Abort;
            }

            if (inspection.Recovery is { } recovery)
            {
                ProjectOpenPreparationResult result =
                    await ApplyPendingPullRecoveryBeforeOpeningAsync(
                            recovery,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (result == ProjectOpenPreparationResult.Abort)
                {
                    return result;
                }
            }
            else
            {
                RepositoryInfo? current = await RevalidateOpeningRepositoryAsync(
                        inspection.ProjectFile,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (current is null || !current.Equals(inspection.Repository))
                {
                    return ProjectOpenPreparationResult.Proceed;
                }
            }

            if (!inspection.Repository.IsNestedInForeignRepo)
            {
                return ProjectOpenPreparationResult.Proceed;
            }

            return TryRecordOpeningRepositoryDecision(
                attempt,
                transition,
                inspection)
                ? ProjectOpenPreparationResult.Proceed
                : ProjectOpenPreparationResult.Abort;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ProjectOpenPreparationResult.Abort;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to revalidate enclosing-repository consent for project open {ProjectFile}.",
                inspection.ProjectFile);
            return ProjectOpenPreparationResult.Abort;
        }
    }

    private async Task<RepositoryInfo?> RevalidateOpeningRepositoryAsync(
        string projectFile,
        CancellationToken cancellationToken)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(cancellationToken);
        if (operation is null)
        {
            return null;
        }

        IProjectVersionControlBackend? discoveryService = null;
        try
        {
            discoveryService = CreateTemporaryBackend(repository: null, projectFile);
            GitAvailability availability = await discoveryService.GetAvailabilityAsync(
                    operation.CancellationToken)
                .ConfigureAwait(false);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return null;
            }

            string projectRoot = Path.GetDirectoryName(projectFile)
                                 ?? throw new InvalidOperationException(
                                     "The project file has no parent directory.");
            return await discoveryService.DiscoverRepositoryAsync(
                    projectRoot,
                    operation.CancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DisposeService(discoveryService);
        }
    }

    private bool TryRecordOpeningRepositoryDecision(
        ProjectService.ProjectOpenAttempt attempt,
        ProjectTransitionContext transition,
        OpeningRepositoryInspection inspection)
    {
        if (transition.Purpose != ProjectTransitionPurpose.Normal
            || !ReferenceEquals(transition.Owner, attempt)
            || !PathsEqual(attempt.ProjectFile, inspection.ProjectFile)
            || !RepositoryPathComparer.AreEquivalent(
                inspection.Repository.ProjectRoot,
                Path.GetDirectoryName(inspection.ProjectFile)
                ?? throw new InvalidOperationException(
                    "The project file has no parent directory.")))
        {
            return false;
        }

        lock (_stateGate)
        {
            if (_disposed)
            {
                return false;
            }

            _pendingOpeningRepositoryDecision = new PendingOpeningRepositoryDecision(
                attempt,
                attempt.Id,
                transition.Id,
                GetOpeningRecoveryKey(inspection.ProjectFile),
                inspection.Repository,
                inspection.EnclosingRepositoryAccepted);
            return true;
        }
    }

    private async Task<ProjectOpenPreparationResult>
        ApplyPendingPullRecoveryBeforeOpeningAsync(
            PendingPullRecoveryOpenSelection selection,
            CancellationToken cancellationToken)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(cancellationToken);
        if (operation is null)
        {
            return ProjectOpenPreparationResult.Abort;
        }

        using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
        if (worktreeMutation is null)
        {
            return ProjectOpenPreparationResult.Abort;
        }

        IProjectVersionControlBackend? discoveryService = null;
        IProjectVersionControlBackend? trackedService = null;
        try
        {
            CancellationToken operationCancellation = operation.CancellationToken;
            string canonicalProjectFile = GetOpeningRecoveryKey(selection.ProjectFile);
            discoveryService = CreateTemporaryBackend(repository: null, selection.ProjectFile);
            GitAvailability availability = await discoveryService.GetAvailabilityAsync(
                    operationCancellation)
                .ConfigureAwait(false);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return ProjectOpenPreparationResult.Abort;
            }

            string projectRoot = Path.GetDirectoryName(selection.ProjectFile)
                                 ?? throw new InvalidOperationException(
                                     "The project file has no parent directory.");
            RepositoryInfo? repository = await discoveryService.DiscoverRepositoryAsync(
                    projectRoot,
                    operationCancellation)
                .ConfigureAwait(false);
            if (repository is null || !repository.Equals(selection.Repository))
            {
                return ProjectOpenPreparationResult.Abort;
            }

            trackedService = CreateTemporaryBackend(repository, selection.ProjectFile);
            PendingPullRecoveryOutcome? outcome = await trackedService.ExecuteExclusiveAsync(
                    async transaction =>
                    {
                        PendingPullRecovery? current =
                            (await transaction.GetPendingPullRecoveriesAsync(operationCancellation))
                            .SingleOrDefault(candidate => string.Equals(
                                candidate.Id,
                                selection.Recovery.Id,
                                StringComparison.Ordinal));
                        if (current is null
                            || !PendingPullRecoveriesMatch(
                                selection.Recovery,
                                current,
                                repository)
                            || !RecoveryProjectPathsEqual(
                                repository,
                                current.ProjectFile,
                                selection.ProjectFile))
                        {
                            throw new PendingPullRecoveryChangedException(
                                selection.Recovery.DescriptorRef);
                        }

                        if (selection.AlreadyApplied)
                        {
                            bool markerMatches;
                            lock (_stateGate)
                            {
                                markerMatches = _openingPullRecoveries.TryGetValue(
                                                    canonicalProjectFile,
                                                    out PendingOpeningPullRecovery? liveMarker)
                                                && liveMarker is not null
                                                && ReferenceEquals(
                                                    liveMarker,
                                                    selection.AppliedMarker)
                                                && liveMarker.Repository.Equals(repository)
                                                && PendingPullRecoveriesMatch(
                                                    liveMarker.Recovery,
                                                    selection.Recovery,
                                                    repository);
                            }

                            if (!markerMatches)
                            {
                                throw new PendingPullRecoveryChangedException(
                                    selection.Recovery.DescriptorRef);
                            }
                        }

                        if (!selection.Accepted || selection.AlreadyApplied)
                        {
                            return (PendingPullRecoveryOutcome?)null;
                        }

                        return (PendingPullRecoveryOutcome?)
                            await transaction.RecoverPendingPullRecoveryAsync(
                                current,
                                CancellationToken.None);
                    },
                    operationCancellation)
                .ConfigureAwait(false);
            if (!IsPendingRecoveryPathSafeForOpen(selection))
            {
                return ProjectOpenPreparationResult.Abort;
            }

            if (outcome is null)
            {
                return ProjectOpenPreparationResult.Proceed;
            }

            lock (_stateGate)
            {
                _openingPullRecoveries[canonicalProjectFile] =
                    new PendingOpeningPullRecovery(repository, selection.Recovery);
            }

            PublishRecoveryOutcomeNotification(selection.Recovery, outcome.Value);
            return ProjectOpenPreparationResult.Proceed;
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            return ProjectOpenPreparationResult.Abort;
        }
        catch (PendingPullRecoveryPreservedException ex)
        {
            PublishPreservedRecoveryBranchNotification(ex.RecoveryReference);
            IsPendingRecoveryPathSafeForOpen(selection);
            return ProjectOpenPreparationResult.Abort;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to validate or recover a pending pull before opening {ProjectFile}; its retained-reference state could not be verified.",
                selection.ProjectFile);
            IsPendingRecoveryPathSafeForOpen(selection);
            return ProjectOpenPreparationResult.Abort;
        }
        finally
        {
            if (!ReferenceEquals(trackedService, discoveryService))
            {
                DisposeService(trackedService);
            }

            DisposeService(discoveryService);
        }
    }

    private bool IsPendingRecoveryPathSafeForOpen(
        PendingPullRecoveryOpenSelection selection)
    {
        try
        {
            EnsurePendingRecoveryPathIsSafeForOpen(
                selection.Repository,
                selection.Recovery,
                selection.ProjectFile);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The recovered project path {ProjectFile} is not safe to open.",
                selection.ProjectFile);
            return false;
        }
    }

    private async Task CompleteOpeningPullRecoveryAfterPublishedAsync(Project project)
    {
        string projectFile = GetProjectFile(project);
        string canonicalProjectFile = GetOpeningRecoveryKey(projectFile);
        PendingOpeningPullRecovery? prepared;
        lock (_stateGate)
        {
            _openingPullRecoveries.TryGetValue(canonicalProjectFile, out prepared);
        }

        if (prepared is null)
        {
            return;
        }

        IProjectVersionControlBackend? service = null;
        try
        {
            service = CreateTemporaryBackend(prepared.Repository, projectFile);
            await service.ExecuteExclusiveAsync(
                    async transaction =>
                    {
                        PendingPullRecovery? current =
                            (await transaction.GetPendingPullRecoveriesAsync(
                                CancellationToken.None))
                            .SingleOrDefault(candidate => string.Equals(
                                candidate.Id,
                                prepared.Recovery.Id,
                                StringComparison.Ordinal));
                        if (current is null
                            || !PendingPullRecoveriesMatch(
                                prepared.Recovery,
                                current,
                                prepared.Repository)
                            || !RecoveryProjectPathsEqual(
                                prepared.Repository,
                                current.ProjectFile,
                                projectFile))
                        {
                            throw new PendingPullRecoveryChangedException(
                                prepared.Recovery.DescriptorRef);
                        }

                        await transaction.CompletePendingPullRecoveryAsync(
                            current,
                            CancellationToken.None);
                        return true;
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_openingPullRecoveries.TryGetValue(
                        canonicalProjectFile,
                        out PendingOpeningPullRecovery? current)
                    && ReferenceEquals(current, prepared))
                {
                    _openingPullRecoveries.Remove(canonicalProjectFile);
                }
            }

            CompletePendingPullRecoveryPublication(prepared.Recovery.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The project opened after pending pull recovery, but its descriptor could not be completed.");
        }
        finally
        {
            DisposeService(service);
        }
    }

    private IProjectVersionControlBackend CreateTemporaryBackend(
        RepositoryInfo? repository,
        string projectFile)
    {
        return _serviceFactory?.Invoke(repository)
               ?? new GitCliVersionControlService(
                   _installationLocator,
                   repository,
                   () => _projectService.CurrentProject.Value is not { } project
                         || !PathsEqual(GetProjectFile(project), projectFile),
                   PresentPolicyNoticeAsync);
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

        using IDisposable? snapshotMutation = TryBeginWorktreeMutation();
        if (snapshotMutation is null)
        {
            _logger.LogInformation(
                "Skipped the {SnapshotKind} project snapshot because output is active.",
                kind);
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

    private PendingOpeningRepositoryDecision? TryTakeOpeningRepositoryDecision(Project project)
    {
        ProjectTransitionContext? transition = _projectService.CurrentTransition;
        string projectFile = GetProjectFile(project);
        string projectRoot = GetProjectRoot(project);
        lock (_stateGate)
        {
            if (_pendingOpeningRepositoryDecision is not { } pending)
            {
                return null;
            }

            bool matches = false;
            try
            {
                matches = transition is
                {
                    Purpose: ProjectTransitionPurpose.Normal,
                    Owner: ProjectService.ProjectOpenAttempt attempt,
                }
                && ReferenceEquals(pending.Attempt, attempt)
                && pending.AttemptId == attempt.Id
                && pending.TransitionId == transition.Id
                && PathsEqual(pending.ProjectFile, projectFile)
                && RepositoryPathComparer.AreEquivalent(
                    pending.Repository.ProjectRoot,
                    projectRoot);
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException
                      or ArgumentException)
            {
                _logger.LogWarning(
                    ex,
                    "Could not match enclosing-repository consent to the opening project {ProjectFile}.",
                    projectFile);
            }

            _pendingOpeningRepositoryDecision = null;
            return matches ? pending : null;
        }
    }

    internal void OnProjectChanged(Project? project)
    {
        bool internalTransition = IsInternalVersionControlTransition();
        PendingOpeningRepositoryDecision? openingRepositoryDecision =
            project is null || internalTransition
                ? null
                : TryTakeOpeningRepositoryDecision(project);
        CancellationTokenSource? configurationActivationCancellation;
        long activationRevision;
        lock (_stateGate)
        {
            _lastProjectNotification = project;
            _hasProjectNotification = true;
            _pendingConfigurationActivation = null;
            if (!internalTransition)
            {
                _repositoryHygieneConfigurationDirty = false;
            }

            configurationActivationCancellation = _configurationActivationCancellation;
            if (!TryBeginActivationSetupLocked(
                    internalTransition,
                    out activationRevision))
            {
                return;
            }
        }

        AdvanceProjectServiceEpoch();
        CancelConfigurationActivation(configurationActivationCancellation);
        if (!internalTransition)
        {
            CancelPendingPullRecoveryOffer();
        }
        StartProjectActivation(
            project,
            internalTransition,
            activationRevision,
            openingRepositoryDecision);
    }

    private void ObserveCurrentProjectSnapshot()
    {
        bool internalTransition = IsInternalVersionControlTransition();
        CancellationTokenSource? configurationActivationCancellation;
        Project? project;
        long activationRevision;
        lock (_stateGate)
        {
            project = _projectService.CurrentProject.Value;
            if (_hasProjectNotification
                && ReferenceEquals(_lastProjectNotification, project))
            {
                return;
            }

            _pendingConfigurationActivation = null;
            if (!internalTransition)
            {
                _repositoryHygieneConfigurationDirty = false;
            }

            configurationActivationCancellation = _configurationActivationCancellation;
            if (!TryBeginActivationSetupLocked(
                    internalTransition,
                    out activationRevision))
            {
                return;
            }
        }

        CancelConfigurationActivation(configurationActivationCancellation);
        if (!internalTransition)
        {
            CancelPendingPullRecoveryOffer();
        }
        StartProjectActivation(
            project,
            internalTransition,
            activationRevision,
            openingRepositoryDecision: null);
    }

    private void StartProjectActivation(
        Project? project,
        bool internalTransition,
        long activationRevision,
        PendingOpeningRepositoryDecision? openingRepositoryDecision)
    {
        _ = StartProjectActivationAfterOpeningRecoveryAsync(
            project,
            internalTransition,
            activationRevision,
            openingRepositoryDecision);
    }

    private async Task StartProjectActivationAfterOpeningRecoveryAsync(
        Project? project,
        bool internalTransition,
        long activationRevision,
        PendingOpeningRepositoryDecision? openingRepositoryDecision)
    {
        try
        {
            if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
            {
                return;
            }

            if (project is not null)
            {
                await CompleteOpeningPullRecoveryAfterPublishedAsync(project).ConfigureAwait(false);
                if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
                {
                    return;
                }
            }

            await OnProjectChangedAsync(
                    project,
                    internalTransition,
                    activationRevision,
                    openingRepositoryDecision,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            FinishActivationSetup();
        }
    }

    private async Task<ActivationContext?> StartProjectActivationAsync(
        Project? project,
        bool internalTransition,
        CancellationToken cancellationToken)
    {
        if (!TryBeginActivationSetup(internalTransition, out long activationRevision))
        {
            return null;
        }

        try
        {
            return await OnProjectChangedAsync(
                    project,
                    internalTransition,
                    activationRevision,
                    openingRepositoryDecision: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            FinishActivationSetup();
        }
    }

    private async Task<ActivationContext?> OnProjectChangedAsync(
        Project? project,
        bool internalTransition,
        long activationRevision,
        PendingOpeningRepositoryDecision? openingRepositoryDecision,
        CancellationToken cancellationToken)
    {
        try
        {
            if (internalTransition && !TryPromoteActivationRevision(activationRevision))
            {
                return null;
            }

            if (internalTransition)
            {
                if (project is null)
                {
                    SetVisibleService(null);
                    return null;
                }

                string preservedRoot = GetProjectRoot(project);
                IProjectVersionControlBackend? preservedService = GetOwnedBackend();
                if (preservedService?.Repository is { } preservedRepository
                    && RepositoryPathComparer.AreEquivalent(
                        preservedRepository.ProjectRoot,
                        preservedRoot))
                {
                    SetVisibleService(preservedService);
                    QueueRepositoryHygieneConfigurationIfDirty(project);
                    return null;
                }
            }

            if (project is null)
            {
                ClearProjectState(activationRevision);
                return null;
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
                service,
                openingRepositoryDecision,
                cancellationToken);
            if (BeginActivation(activation, out bool cleanupRejectedService))
            {
                _ = ActivateRepositoryAsync(activation);
                return activation;
            }

            await CompleteRejectedActivationAsync(activation, cleanupRejectedService)
                .ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ClearProjectState(activationRevision);
            return null;
        }
    }

    private bool TryBeginActivationSetup(
        bool internalTransition,
        out long activationRevision)
    {
        lock (_stateGate)
        {
            return TryBeginActivationSetupLocked(
                internalTransition,
                out activationRevision);
        }
    }

    private bool TryBeginActivationSetupLocked(
        bool internalTransition,
        out long activationRevision)
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
        TryStartPendingConfigurationActivation();
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
        IProjectVersionControlBackend? pendingRecoveryOfferService = null;
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

            if (repository.IsNestedInForeignRepo)
            {
                PendingOpeningRepositoryDecision? openingDecision =
                    activation.OpeningRepositoryDecision;
                bool matchesOpeningDecision = openingDecision is not null
                                              && openingDecision.Repository.Equals(repository)
                                              && RepositoryPathComparer.AreEquivalent(
                                                  repository.ProjectRoot,
                                                  activation.ProjectRoot);
                if (matchesOpeningDecision)
                {
                    if (!openingDecision!.Accepted)
                    {
                        return;
                    }
                }
                else if (!await ConfirmUseEnclosingRepositoryAsync(
                             repository,
                             activation.CancellationToken))
                {
                    return;
                }
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

            bool activationCompleted = CompleteActivation(activation, trackedService);
            if (!activationCompleted && !activation.OwnsService(trackedService))
            {
                pendingCleanup = trackedService;
            }

            if (activationCompleted)
            {
                pendingRecoveryOfferService = trackedService;
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
                if (pendingRecoveryOfferService is not null)
                {
                    StartPendingPullRecoveryOffer(pendingRecoveryOfferService);
                }

                TryStartPendingConfigurationActivation();
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

    private void StartPendingPullRecoveryOffer(IProjectVersionControlBackend service)
    {
        var offer = new PendingRecoveryOfferContext(
            service,
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token));
        PendingRecoveryOfferContext? previousOffer;
        lock (_stateGate)
        {
            if (_disposed
                || !ReferenceEquals(_state.OwnedService, service)
                || !ReferenceEquals(_state.VisibleService, service))
            {
                offer.Cancellation.Dispose();
                return;
            }

            previousOffer = _pendingRecoveryOffer;
            _pendingRecoveryOffer = offer;
            _pendingRecoveryOfferUsers++;
        }

        CancelPendingPullRecoveryOffer(previousOffer);
        _ = RunPendingPullRecoveryOfferAsync(offer);
    }

    private async Task RunPendingPullRecoveryOfferAsync(
        PendingRecoveryOfferContext offer)
    {
        IProjectVersionControlBackend service = offer.Service;
        CancellationToken cancellationToken = offer.Cancellation.Token;
        try
        {
            IReadOnlyList<PendingPullRecovery> recoveries;
            using (NonTransactionalOperationLease operation =
                   await BeginNonTransactionalOperationAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                if (!ReferenceEquals(GetOperationReadyBackend(), service))
                {
                    return;
                }

                recoveries = await service.ExecuteExclusiveAsync(
                        transaction => transaction.GetPendingPullRecoveriesAsync(
                            operation.CancellationToken),
                        operation.CancellationToken)
                    .ConfigureAwait(false);
            }

            var currentIds = recoveries
                .Select(static recovery => recovery.Id)
                .ToHashSet(StringComparer.Ordinal);
            PendingPullRecovery[] orderedRecoveries = recoveries
                .OrderBy(static candidate => candidate.CreatedAt)
                .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
                .ToArray();
            PendingPullRecovery? recovery = null;
            bool offerRecovery = false;
            lock (_stateGate)
            {
                _offeredPendingRecoveryIds.RemoveWhere(id => !currentIds.Contains(id));
                if (!_disposed
                    && ReferenceEquals(_pendingRecoveryOffer, offer)
                    && ReferenceEquals(_state.OwnedService, service)
                    && ReferenceEquals(_state.VisibleService, service))
                {
                    recovery = orderedRecoveries.FirstOrDefault(candidate =>
                        !_offeredPendingRecoveryIds.Contains(candidate.Id));
                    if (recovery is not null)
                    {
                        offerRecovery = _offeredPendingRecoveryIds.Add(recovery.Id);
                    }
                }
            }

            if (!offerRecovery
                || recovery is null
                || !await ConfirmPendingPullRecoveryAsync(
                    ToRecoveryInfo(recovery),
                    cancellationToken))
            {
                return;
            }

            await RunPendingPullRecoveryCycleAsync(
                    recovery.Id,
                    requireConfirmation: false,
                    cancellationToken,
                    confirmedRecovery: recovery)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped a pending pull recovery offer because the project lifecycle changed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to offer a pending pull recovery.");
        }
        finally
        {
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                if (ReferenceEquals(_pendingRecoveryOffer, offer))
                {
                    _pendingRecoveryOffer = null;
                }

                _pendingRecoveryOfferUsers--;
                if (_pendingRecoveryOfferUsers == 0 && _disposed)
                {
                    quiesced = _pendingRecoveryOffersQuiesced;
                }
            }

            offer.Cancellation.Dispose();
            quiesced?.TrySetResult();
        }
    }

    private void CancelPendingPullRecoveryOffer()
    {
        PendingRecoveryOfferContext? offer;
        lock (_stateGate)
        {
            offer = _pendingRecoveryOffer;
            _pendingRecoveryOffer = null;
        }

        CancelPendingPullRecoveryOffer(offer);
    }

    private void CancelPendingPullRecoveryOffer(PendingRecoveryOfferContext? offer)
    {
        try
        {
            offer?.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A pending pull recovery offer cancellation callback failed.");
        }
    }

    private void ReconcileOfferedPendingRecoveryIds(
        IReadOnlyList<PendingPullRecovery> recoveries)
    {
        var currentIds = recoveries
            .Select(static recovery => recovery.Id)
            .ToHashSet(StringComparer.Ordinal);
        lock (_stateGate)
        {
            _offeredPendingRecoveryIds.RemoveWhere(id => !currentIds.Contains(id));
        }
    }

    private void CompletePendingPullRecoveryPublication(string recoveryId)
    {
        lock (_stateGate)
        {
            _offeredPendingRecoveryIds.Remove(recoveryId);
        }

        PublishPendingPullRecoveriesChanged();
    }

    private void PublishPendingPullRecoveriesChanged()
    {
        EventHandler? handlers = PendingPullRecoveriesChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A pending pull recovery subscriber failed.");
            }
        }
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        bool executablePathChanged =
            TryCaptureGitExecutablePathChange(out string? executablePath);
        bool useLfsWhenAvailableChanged = TryCaptureUseLfsWhenAvailableChange(
            out bool useLfsWhenAvailable);
        if (executablePathChanged)
        {
            AdvanceProjectServiceEpoch();
        }

        if ((executablePathChanged || useLfsWhenAvailableChanged)
            && _projectService.CurrentProject.Value is { } project)
        {
            QueueConfigurationActivation(
                project,
                executablePath,
                useLfsWhenAvailable,
                rediscoverUnassociatedBackend: executablePathChanged,
                reapplyTrackedRepositoryHygiene: useLfsWhenAvailableChanged);
        }

        StartAvailabilityRefresh();
    }

    private bool TryCaptureGitExecutablePathChange(out string? executablePath)
    {
        executablePath = NormalizeGitExecutablePath(_config.GitExecutablePath);
        lock (_stateGate)
        {
            if (_disposed
                || string.Equals(
                    executablePath,
                    _observedGitExecutablePath,
                    PathComparison))
            {
                return false;
            }

            _observedGitExecutablePath = executablePath;
            return true;
        }
    }

    private bool TryCaptureUseLfsWhenAvailableChange(out bool useLfsWhenAvailable)
    {
        useLfsWhenAvailable = _config.UseLfsWhenAvailable;
        lock (_stateGate)
        {
            if (_disposed || useLfsWhenAvailable == _observedUseLfsWhenAvailable)
            {
                return false;
            }

            _observedUseLfsWhenAvailable = useLfsWhenAvailable;
            if (_state.OwnedService?.Repository is not null)
            {
                _repositoryHygieneConfigurationDirty = true;
            }

            return true;
        }
    }

    private void QueueRepositoryHygieneConfigurationIfDirty(Project project)
    {
        string? executablePath;
        bool useLfsWhenAvailable;
        lock (_stateGate)
        {
            if (_disposed
                || !_repositoryHygieneConfigurationDirty
                || !ReferenceEquals(_projectService.CurrentProject.Value, project))
            {
                return;
            }

            executablePath = _observedGitExecutablePath;
            useLfsWhenAvailable = _observedUseLfsWhenAvailable;
        }

        QueueConfigurationActivation(
            project,
            executablePath,
            useLfsWhenAvailable,
            rediscoverUnassociatedBackend: false,
            reapplyTrackedRepositoryHygiene: true);
    }

    private void QueueConfigurationActivation(
        Project project,
        string? executablePath,
        bool useLfsWhenAvailable,
        bool rediscoverUnassociatedBackend,
        bool reapplyTrackedRepositoryHygiene)
    {
        string projectRoot = GetProjectRoot(project);
        CancellationTokenSource? activeCancellation = null;
        lock (_stateGate)
        {
            if (_disposed || !ReferenceEquals(_projectService.CurrentProject.Value, project))
            {
                return;
            }

            if (_state.ProjectRoot is { } stateRoot
                && string.Equals(stateRoot, projectRoot, PathComparison)
                && _state.OwnedService?.Repository is not null
                && !reapplyTrackedRepositoryHygiene)
            {
                return;
            }

            ConfigurationActivationRequest? pending = _pendingConfigurationActivation;
            _pendingConfigurationActivation = new ConfigurationActivationRequest(
                ++_nextConfigurationActivationRevision,
                project,
                projectRoot,
                executablePath,
                useLfsWhenAvailable,
                rediscoverUnassociatedBackend
                || pending?.RediscoverUnassociatedBackend == true,
                reapplyTrackedRepositoryHygiene
                || pending?.ReapplyTrackedRepositoryHygiene == true);
            if (rediscoverUnassociatedBackend)
            {
                activeCancellation = _configurationActivationCancellation;
            }
        }

        CancelConfigurationActivation(activeCancellation);
        TryStartPendingConfigurationActivation();
    }

    private void TryStartPendingConfigurationActivation()
    {
        ConfigurationActivationStart? activationStart;
        lock (_stateGate)
        {
            activationStart = TryPreparePendingConfigurationActivationLocked();
        }

        StartConfigurationActivation(activationStart);
    }

    private ConfigurationActivationStart? TryPreparePendingConfigurationActivationLocked()
    {
        ConfigurationActivationRequest? request = _pendingConfigurationActivation;
        if (_disposed)
        {
            _pendingConfigurationActivation = null;
            return null;
        }

        if (request is null || _configurationActivationActive)
        {
            return null;
        }

        Project? currentProject = _projectService.CurrentProject.Value;
        if (!ReferenceEquals(currentProject, request.Project))
        {
            _pendingConfigurationActivation = null;
            return null;
        }

        if (_state.ProjectRoot is { } stateRoot
            && !string.Equals(stateRoot, request.ProjectRoot, PathComparison))
        {
            _pendingConfigurationActivation = null;
            return null;
        }

        if (_operationCloseBarrierActive
            || _closeBarrierUsers != 0
            || _operationUsers != 0
            || _lifecycleUsers != 0
            || _activationSetupUsers != 0
            || _activation is not null)
        {
            return null;
        }

        IProjectVersionControlBackend? trackedService = null;
        if (_state.ProjectRoot is { } trackedRoot
            && string.Equals(trackedRoot, request.ProjectRoot, PathComparison)
            && _state.OwnedService?.Repository is not null)
        {
            if (!request.ReapplyTrackedRepositoryHygiene)
            {
                _pendingConfigurationActivation = null;
                return null;
            }

            trackedService = _state.OwnedService;
        }
        else if (!request.RediscoverUnassociatedBackend)
        {
            _pendingConfigurationActivation = null;
            return null;
        }

        CancellationToken operationEpochCancellation = (_operationEpochCancellation
                                                          ?? throw new ObjectDisposedException(
                                                              nameof(VersionControlCoordinator)))
            .Token;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            operationEpochCancellation);
        _pendingConfigurationActivation = null;
        _configurationActivationActive = true;
        _configurationActivationCancellation = cancellation;
        _operationUsers++;
        return new ConfigurationActivationStart(request, cancellation, trackedService);
    }

    private void StartConfigurationActivation(ConfigurationActivationStart? activationStart)
    {
        if (activationStart is not null)
        {
            _ = RunConfigurationActivationAsync(activationStart);
        }
    }

    private async Task RunConfigurationActivationAsync(ConfigurationActivationStart activationStart)
    {
        ConfigurationActivationRequest request = activationStart.Request;
        CancellationTokenSource cancellation = activationStart.Cancellation;
        bool retry = false;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (activationStart.TrackedService is { } trackedService)
            {
                await trackedService.EnsureRepositoryHygieneAsync(cancellation.Token)
                    .ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                lock (_stateGate)
                {
                    if (ReferenceEquals(_state.OwnedService, trackedService)
                        && trackedService.Repository is not null
                        && request.UseLfsWhenAvailable == _observedUseLfsWhenAvailable)
                    {
                        _repositoryHygieneConfigurationDirty = false;
                    }
                }
            }
            else
            {
                ActivationContext? activation = await StartProjectActivationAsync(
                        request.Project,
                        internalTransition: false,
                        cancellation.Token)
                    .ConfigureAwait(false);
                if (activation is not null)
                {
                    await activation.Completion.ConfigureAwait(false);
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            retry = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply a version-control configuration change.");
        }
        finally
        {
            FinishConfigurationActivation(activationStart, retry);
        }
    }

    private void FinishConfigurationActivation(
        ConfigurationActivationStart activationStart,
        bool retry)
    {
        ConfigurationActivationRequest request = activationStart.Request;
        CancellationTokenSource cancellation = activationStart.Cancellation;
        TaskCompletionSource? configurationActivationQuiesced = null;
        TaskCompletionSource? operationsQuiesced = null;
        ConfigurationActivationStart? nextActivation = null;
        lock (_stateGate)
        {
            if (ReferenceEquals(_configurationActivationCancellation, cancellation))
            {
                _configurationActivationCancellation = null;
            }

            _configurationActivationActive = false;
            configurationActivationQuiesced = _configurationActivationQuiesced;
            _configurationActivationQuiesced = null;
            _operationUsers--;

            bool retryTargetStillCurrent = activationStart.TrackedService is { } trackedService
                ? request.ReapplyTrackedRepositoryHygiene
                  && request.UseLfsWhenAvailable == _observedUseLfsWhenAvailable
                  && ReferenceEquals(_state.OwnedService, trackedService)
                  && trackedService.Repository is not null
                : request.RediscoverUnassociatedBackend
                  && string.Equals(
                      _observedGitExecutablePath,
                      request.ExecutablePath,
                      PathComparison)
                  && _state.OwnedService?.Repository is null;
            if (retry
                && !_disposed
                && request.Revision == _nextConfigurationActivationRevision
                && _pendingConfigurationActivation is null
                && ReferenceEquals(_projectService.CurrentProject.Value, request.Project)
                && (_state.ProjectRoot is null
                    || string.Equals(
                        _state.ProjectRoot,
                        request.ProjectRoot,
                        PathComparison))
                && retryTargetStillCurrent)
            {
                _pendingConfigurationActivation = request;
            }

            nextActivation = TryPreparePendingConfigurationActivationLocked();
            if (_operationUsers == 0)
            {
                operationsQuiesced = _operationsQuiesced;
                _operationsQuiesced = null;
            }
        }

        try
        {
            cancellation.Dispose();
        }
        finally
        {
            try
            {
                StartConfigurationActivation(nextActivation);
            }
            finally
            {
                configurationActivationQuiesced?.TrySetResult();
                operationsQuiesced?.TrySetResult();
            }
        }
    }

    private void CancelConfigurationActivation(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A Git configuration activation cancellation callback failed.");
        }
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
                       || activation.CancellationToken.IsCancellationRequested
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

        CancelPendingPullRecoveryOffer();
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
            _repositoryHygieneConfigurationDirty = false;
            schedulePublication = TransitionOwnedServiceLocked(
                ownedService: null,
                visibleService: null,
                projectRoot: null,
                activation,
                out _);
        }

        CancelPendingPullRecoveryOffer();
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
            CancelPendingPullRecoveryOffer();
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

    private sealed record PendingRecoveryOfferContext(
        IProjectVersionControlBackend Service,
        CancellationTokenSource Cancellation);

    private sealed record PendingOpeningPullRecovery(
        RepositoryInfo Repository,
        PendingPullRecovery Recovery);

    private sealed record PendingOpeningRepositoryDecision(
        ProjectService.ProjectOpenAttempt Attempt,
        long AttemptId,
        long TransitionId,
        string ProjectFile,
        RepositoryInfo Repository,
        bool Accepted);

    private sealed record OpeningRepositoryInspection(
        RepositoryInfo Repository,
        string ProjectFile,
        bool EnclosingRepositoryAccepted,
        PendingPullRecoveryOpenSelection? Recovery);

    private sealed record PendingPullRecoveryOpenSelection(
        RepositoryInfo Repository,
        PendingPullRecovery Recovery,
        string ProjectFile,
        bool Accepted,
        PendingOpeningPullRecovery? AppliedMarker)
    {
        public bool AlreadyApplied => AppliedMarker is not null;
    }

    private sealed class VersionControlProjectOpenPreparation(
        VersionControlCoordinator owner,
        ProjectService.ProjectOpenAttempt attempt,
        OpeningRepositoryInspection inspection)
        : ProjectService.ProjectOpenPreparation
    {
        internal override Task<ProjectOpenPreparationResult> ApplyAsync(
            ProjectTransitionContext transition,
            CancellationToken cancellationToken)
        {
            return owner.ApplyProjectOpeningPreparationAsync(
                attempt,
                inspection,
                transition,
                cancellationToken);
        }
    }

    private sealed class AbortProjectOpenPreparation : ProjectService.ProjectOpenPreparation
    {
        internal override Task<ProjectOpenPreparationResult> ApplyAsync(
            ProjectTransitionContext transition,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProjectOpenPreparationResult.Abort);
        }
    }

    private sealed record PullMutationOutcome(
        RemoteOpResult Result,
        PendingPullRecovery? Recovery,
        string ProjectFile);

    private sealed record ConfigurationActivationRequest(
        long Revision,
        Project Project,
        string ProjectRoot,
        string? ExecutablePath,
        bool UseLfsWhenAvailable,
        bool RediscoverUnassociatedBackend,
        bool ReapplyTrackedRepositoryHygiene);

    private sealed record ConfigurationActivationStart(
        ConfigurationActivationRequest Request,
        CancellationTokenSource Cancellation,
        IProjectVersionControlBackend? TrackedService);

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
                        _operationEpochCancellation,
                        projectClosed)
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
        private readonly CancellationTokenSource _cancellation;
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
            IProjectVersionControlBackend service,
            PendingOpeningRepositoryDecision? openingRepositoryDecision = null,
            CancellationToken cancellationToken = default)
        {
            Revision = revision;
            ProjectRoot = projectRoot;
            Service = service;
            OpeningRepositoryDecision = openingRepositoryDecision;
            _ownedService = service;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public long Revision { get; }

        public string ProjectRoot { get; }

        public IProjectVersionControlBackend Service { get; }

        public PendingOpeningRepositoryDecision? OpeningRepositoryDecision { get; }

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

    private static string? NormalizeGitExecutablePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path;

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
