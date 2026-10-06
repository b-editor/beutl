using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

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
    private const string PullSafetySnapshotMessage = "beutl: safety snapshot before pull";
    private const string RestoreRecoveryMessage =
        "beutl: recover original project state after failed restore";

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlConfig _config;
    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<RepositoryInfo?, IProjectVersionControlBackend>? _serviceFactory;
    private readonly Dispatcher _dispatcher;
    private readonly VersionControlConfirmationPresenter _prompts;
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
    private readonly HashSet<ProjectService.ProjectCloseContext> _closesWithoutSnapshot = new();
    private readonly Dictionary<IProjectVersionControlBackend, HashSet<ActivationContext>>
        _candidateServiceUsers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IProjectVersionControlBackend> _managedServices = new(
        ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _offeredPendingRecoveryIds = new(StringComparer.Ordinal);
    // Ordinal, because GetOpeningRecoveryKey already resolved the casing the filesystem merges:
    // a case-insensitive comparer on top of that would fold two genuinely distinct directories
    // together on a case-sensitive volume.
    private readonly Dictionary<string, PendingOpeningPullRecovery> _openingPullRecoveries =
        new(StringComparer.Ordinal);
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
    // A project created with tracking gets its repository before it opens. The initialized backend
    // waits here until the activation of that same creation adopts it.
    private PreparedNewProject? _preparedNewProject;
    private PendingOpeningRepositoryDecision? _pendingOpeningRepositoryDecision;
    private RepositoryAdoptionRequest? _pendingRepositoryAdoption;
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
        _prompts = new VersionControlConfirmationPresenter(_dispatcher);
        ConfirmRestoreAsync = _prompts.ShowRestoreConfirmationAsync;
        ConfirmSwitchBranchAsync = ShowSwitchBranchConfirmationAsync;
        ConfirmPullAsync = _prompts.ShowPullConfirmationAsync;
        ConfirmPendingPullRecoveryAsync = _prompts.ShowPendingPullRecoveryConfirmationAsync;
        ConfirmUseEnclosingRepositoryAsync = _prompts.ShowEnclosingRepositoryConfirmationAsync;
        ConfirmAdoptExistingRepositoryAsync = ShowAdoptExistingRepositoryConfirmationAsync;
        ConfirmRemoveStaleLockAsync = _prompts.ShowStaleLockConfirmationAsync;
        ConfirmUntrackReservedPathsAsync = _prompts.ShowUntrackReservedPathsConfirmationAsync;
        WarnConflictMarkersAsync = _prompts.ShowConflictMarkerWarningAsync;
        RequestIdentityAsync = static _ => Task.FromResult<GitIdentity?>(null);
        ConfirmCloseWithoutSnapshotAsync = _prompts.ShowCloseWithoutSnapshotConfirmationAsync;
        PresentPolicyNoticeAsync = _prompts.ShowPolicyNoticeAsync;
        _config.ConfigurationChanged += OnVersionControlConfigChanged;
        _projectService.OpeningPreflight += PrepareProjectOpeningAsync;
        _projectService.Opening += InspectProjectOpeningAsync;
        _projectService.ClosingPreparing += PrepareProjectClosingAsync;
        _projectService.ClosingFinalizing += NotifyProjectClosingAsync;
        _projectService.TransitionCommitted += OnProjectChanged;
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

    public event EventHandler? RepositoryAdoptionChanged;

    public RepositoryAdoptionRequest? PendingRepositoryAdoption
    {
        get
        {
            lock (_stateGate)
            {
                return _pendingRepositoryAdoption;
            }
        }
    }

    internal Func<CancellationToken, Task<bool>> ConfirmRestoreAsync { get; set; }

    internal Func<string, CancellationToken, Task<bool>> ConfirmSwitchBranchAsync { get; set; }

    internal Func<CancellationToken, Task<bool>> ConfirmPullAsync { get; set; }

    internal Func<ProjectRecoveryInfo, CancellationToken, Task<bool>>
        ConfirmPendingPullRecoveryAsync
    { get; set; }

    internal Func<RepositoryInfo, CancellationToken, Task<bool>>
        ConfirmUseEnclosingRepositoryAsync
    { get; set; }

    internal Func<RepositoryInfo, CancellationToken, Task<bool>>
        ConfirmAdoptExistingRepositoryAsync
    { get; set; }

    internal Func<RepositoryLockInfo, CancellationToken, Task<bool>>
        ConfirmRemoveStaleLockAsync
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
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _availabilityUsers++;
        }

        return GetAvailabilityTrackedAsync(cancellationToken);
    }

    public INewProjectVersionControlSetup BeginNewProject(
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync)
    {
        ArgumentNullException.ThrowIfNull(requestIdentityAsync);
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        return new NewProjectSetup(
            this,
            requestIdentityAsync,
            _editorService.BeginLifecycleActivity(ProjectLifecycleActivity.CreatingProject));
    }

    public async Task NotifySavedAsync(
        IProjectFileWriteLease? completedWrite = null,
        CancellationToken cancellationToken = default)
    {
        using NonTransactionalOperationLease operation =
            await BeginNonTransactionalOperationAsync(cancellationToken);
        await CommitSnapshotAsync(
            _config.AutoCommitOnSave,
            SaveSnapshotMessage,
            SnapshotKind.Save,
            completedWrite,
            operation.CancellationToken);
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
}
