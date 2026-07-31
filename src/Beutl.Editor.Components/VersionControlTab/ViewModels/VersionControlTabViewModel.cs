using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Resources;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

public sealed class VersionControlTabViewModel : IToolContext
{
    internal const int HistoryPageSize = 50;
    private static readonly Uri s_gitDownloadsUri = new("https://git-scm.com/downloads");

    private readonly IEditorContext _editorContext;
    private readonly IProjectVersionControlCoordinator? _versionControlCoordinator;
    private readonly Action<Action> _postToUi;
    private readonly VersionControlRelativeTimeFormatter _relativeTimeFormatter;
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private readonly ReactivePropertySlim<bool> _showingDetail;
    private readonly ReactivePropertySlim<VersionControlPrimaryAction> _primaryAction;
    private readonly ReactiveCommandSlim _disabledPrimaryActionCommand;
    private readonly ReactivePropertySlim<bool> _isPrimaryActionEnabled;
    private ICommand? _observedPrimaryActionCommand;
    private IProjectVersionControlService? _service;
    private IRepositoryLockRecoveryService? _lockRecoveryService;
    private CancellationTokenSource? _serviceBindingCancellation;
    private CancellationTokenSource? _selectionCancellation;
    private CancellationTokenSource? _remoteOperationCancellation;
    private int _serviceRevision;
    private int _nextHistoryOffset;
    private int _aheadCount;
    private int _behindCount;
    private int _restoreRequestActive;
    private bool _hasUncommittedChanges;
    private bool _disposed;

    public VersionControlTabViewModel(
        ToolTabExtension extension,
        IEditorContext editorContext)
        : this(
            extension,
            editorContext,
            editorContext.GetService(
                    typeof(IReadOnlyReactiveProperty<IProjectVersionControlService?>))
                as IReadOnlyReactiveProperty<IProjectVersionControlService?>
                ?? throw new InvalidOperationException(
                    "The editor context does not provide the version-control service observable."),
            editorContext.GetService(typeof(IProjectVersionControlCoordinator))
                as IProjectVersionControlCoordinator,
            PostToUiThread,
            timeProvider: null,
            culture: null)
    {
    }

    internal VersionControlTabViewModel(
        ToolTabExtension extension,
        IEditorContext editorContext,
        IReadOnlyReactiveProperty<IProjectVersionControlService?> serviceSource,
        IProjectVersionControlCoordinator? versionControlCoordinator,
        Action<Action> postToUi,
        TimeProvider? timeProvider = null,
        CultureInfo? culture = null)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        _editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        ArgumentNullException.ThrowIfNull(serviceSource);
        IProjectVersionControlService? service = serviceSource.Value;
        _versionControlCoordinator = versionControlCoordinator;
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        _relativeTimeFormatter = new VersionControlRelativeTimeFormatter(
            timeProvider ?? TimeProvider.System,
            culture ?? CultureInfo.CurrentUICulture);

        IsTracked = new ReactivePropertySlim<bool>(service?.Repository is not null)
            .DisposeWith(_disposables);
        IsGitAvailable = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsUnavailable = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsConflicted = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasBlockingGuidance = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasRecoverableLock = new ReactivePropertySlim<bool>(
                _lockRecoveryService?.RecoverableLock is not null)
            .DisposeWith(_disposables);
        DirtySummary = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        StatusMessage = new ReactivePropertySlim<string>(
                IsTracked.Value
                    ? string.Empty
                    : Strings.VersionControl_NoRepository)
            .DisposeWith(_disposables);
        IsLoading = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasMoreHistory = new ReactivePropertySlim<bool>(IsTracked.Value)
            .DisposeWith(_disposables);
        IsHistoryEmpty = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        _showingDetail = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        ShowingDetail = _showingDetail
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        SelectedCommit = new ReactivePropertySlim<VersionControlCommitViewModel?>()
            .DisposeWith(_disposables);
        SelectedFile = new ReactivePropertySlim<VersionControlFileChangeViewModel?>()
            .DisposeWith(_disposables);
        HasSelectedCommit = SelectedCommit
            .Select(static commit => commit is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        HasSelectedFile = SelectedFile
            .Select(static file => file is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        CommitMessage = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        RemoteUrl = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        HasRemote = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        RemoteProgress = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        IsRemoteOperationRunning = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsNestedRepository = new ReactivePropertySlim<bool>(
                service?.Repository?.IsNestedInForeignRepo == true)
            .DisposeWith(_disposables);
        RepositoryScopeText = new ReactivePropertySlim<string>(
                service?.Repository is { IsNestedInForeignRepo: true } repository
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.VersionControl_EnclosingRepositoryScopeFormat,
                        repository.RepoRoot)
                    : string.Empty)
            .DisposeWith(_disposables);
        CanEnableVersionControl = IsGitAvailable.CombineLatest(
                IsTracked,
                static (available, tracked) => available && !tracked)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LoadMoreCommand = new AsyncReactiveCommand()
            .WithSubscribe(LoadMoreAsync)
            .DisposeWith(_disposables);
        BackToHistoryCommand = new ReactiveCommandSlim(ShowingDetail)
            .WithSubscribe(ShowHistory)
            .DisposeWith(_disposables);
        EnableVersionControlCommand = new AsyncReactiveCommand(CanEnableVersionControl)
            .WithSubscribe(EnableVersionControlAsync)
            .DisposeWith(_disposables);
        DownloadGitCommand = new AsyncReactiveCommand(IsUnavailable)
            .WithSubscribe(DownloadGitAsync)
            .DisposeWith(_disposables);
        RemoveStaleLockCommand = new AsyncReactiveCommand(HasRecoverableLock)
            .WithSubscribe(RemoveStaleLockAsync)
            .DisposeWith(_disposables);
        IObservable<bool> canMutate = IsTracked.CombineLatest(
            HasBlockingGuidance,
            IsRemoteOperationRunning,
            static (tracked, blocked, isRunning) => tracked && !blocked && !isRunning);
        CommitCommand = new AsyncReactiveCommand(
                canMutate.CombineLatest(
                    CommitMessage.Select(static message => !string.IsNullOrWhiteSpace(message)),
                    static (canRun, hasMessage) => canRun && hasMessage))
            .WithSubscribe(CommitManualAsync)
            .DisposeWith(_disposables);
        SetRemoteCommand = new AsyncReactiveCommand(canMutate)
            .WithSubscribe(SetRemoteAsync)
            .DisposeWith(_disposables);
        PublishBranchCommand = new AsyncReactiveCommand(
                canMutate.CombineLatest(
                    HasRemote,
                    static (canRun, hasRemote) => canRun && !hasRemote))
            .WithSubscribe(PublishBranchAsync)
            .DisposeWith(_disposables);
        IObservable<bool> canRunRemoteOperation = canMutate.CombineLatest(
            HasRemote,
            IsRemoteOperationRunning,
            static (canRun, hasRemote, isRunning) => canRun && hasRemote && !isRunning);
        PushCommand = new AsyncReactiveCommand(canRunRemoteOperation)
            .WithSubscribe(PushAsync)
            .DisposeWith(_disposables);
        PullCommand = new AsyncReactiveCommand(canRunRemoteOperation)
            .WithSubscribe(PullAsync)
            .DisposeWith(_disposables);
        CancelRemoteOperationCommand = new ReactiveCommandSlim(
                IsRemoteOperationRunning)
            .WithSubscribe(CancelRemoteOperation)
            .DisposeWith(_disposables);
        _disabledPrimaryActionCommand = new ReactiveCommandSlim(Observable.Return(false))
            .DisposeWith(_disposables);
        _primaryAction = new ReactivePropertySlim<VersionControlPrimaryAction>(
                new(
                    VersionControlPrimaryActionKind.UpToDate,
                    Strings.VersionControl_UpToDate,
                    _disabledPrimaryActionCommand))
            .DisposeWith(_disposables);
        PrimaryAction = _primaryAction
            .ToReadOnlyReactivePropertySlim(_primaryAction.Value)!
            .DisposeWith(_disposables);
        _isPrimaryActionEnabled = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsPrimaryActionEnabled = _isPrimaryActionEnabled
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        InvokePrimaryActionCommand = new ReactiveCommandSlim()
            .WithSubscribe(InvokePrimaryAction)
            .DisposeWith(_disposables);
        RequestBranchNameAsync = static _ => Task.FromResult<string?>(null);
        RequestRemoteUrlAsync = static _ => Task.FromResult<string?>(null);
        ShowRemoteResultAsync = ShowRemoteResultNotificationAsync;
        RequestEnableVersionControlAsync = static () => Task.CompletedTask;
        LaunchUriAsync = static _ => Task.FromResult(false);
        IsRemoteOperationRunning
            .Subscribe(_ => UpdatePrimaryAction())
            .DisposeWith(_disposables);
        HasRemote
            .Subscribe(_ => UpdatePrimaryAction())
            .DisposeWith(_disposables);

        Initialization = RebindServiceAsync(service);
        serviceSource
            .Subscribe(publishedService =>
            {
                if (!ReferenceEquals(publishedService, _service))
                {
                    OnServicePublished(publishedService);
                }
            })
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.VersionControl;

    public ReactivePropertySlim<bool> IsTracked { get; }

    public ReactivePropertySlim<bool> IsGitAvailable { get; }

    public ReactivePropertySlim<bool> IsUnavailable { get; }

    public ReactivePropertySlim<bool> IsConflicted { get; }

    public ReactivePropertySlim<bool> HasBlockingGuidance { get; }

    public ReactivePropertySlim<bool> HasRecoverableLock { get; }

    public ReactivePropertySlim<string> DirtySummary { get; }

    public ReactivePropertySlim<string> StatusMessage { get; }

    public ReactivePropertySlim<bool> IsLoading { get; }

    public ReactivePropertySlim<bool> HasMoreHistory { get; }

    public ReactivePropertySlim<bool> IsHistoryEmpty { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowingDetail { get; }

    public ObservableCollection<VersionControlCommitViewModel> Commits { get; } = [];

    public ObservableCollection<VersionControlFileChangeViewModel> ChangedFiles { get; } = [];

    public ObservableCollection<VersionControlDiffLineViewModel> DiffLines { get; } = [];

    public ReactivePropertySlim<VersionControlCommitViewModel?> SelectedCommit { get; }

    public ReactivePropertySlim<VersionControlFileChangeViewModel?> SelectedFile { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSelectedCommit { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSelectedFile { get; }

    public ReactivePropertySlim<string> CommitMessage { get; }

    public ReactivePropertySlim<string> RemoteUrl { get; }

    public ReactivePropertySlim<bool> HasRemote { get; }

    public ReactivePropertySlim<string> RemoteProgress { get; }

    public ReactivePropertySlim<bool> IsRemoteOperationRunning { get; }

    public ReactivePropertySlim<bool> IsNestedRepository { get; }

    public ReactivePropertySlim<string> RepositoryScopeText { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEnableVersionControl { get; }

    public AsyncReactiveCommand LoadMoreCommand { get; }

    public ReactiveCommandSlim BackToHistoryCommand { get; }

    public AsyncReactiveCommand EnableVersionControlCommand { get; }

    public AsyncReactiveCommand DownloadGitCommand { get; }

    public AsyncReactiveCommand RemoveStaleLockCommand { get; }

    public AsyncReactiveCommand CommitCommand { get; }

    public AsyncReactiveCommand SetRemoteCommand { get; }

    public AsyncReactiveCommand PublishBranchCommand { get; }

    public AsyncReactiveCommand PushCommand { get; }

    public AsyncReactiveCommand PullCommand { get; }

    public ReactiveCommandSlim CancelRemoteOperationCommand { get; }

    internal ReadOnlyReactivePropertySlim<VersionControlPrimaryAction> PrimaryAction { get; }

    internal ReadOnlyReactivePropertySlim<bool> IsPrimaryActionEnabled { get; }

    internal ReactiveCommandSlim InvokePrimaryActionCommand { get; }

    public Task Initialization { get; private set; }

    public Func<CommitInfo, Task<string?>> RequestBranchNameAsync { get; set; }

    public Func<string?, Task<string?>> RequestRemoteUrlAsync { get; set; }

    public Func<RemoteOpResult, Task> ShowRemoteResultAsync { get; set; }

    public Func<Task> RequestEnableVersionControlAsync { get; set; }

    public Func<Uri, Task<bool>> LaunchUriAsync { get; set; }

    public async Task EnableVersionControlAsync()
    {
        if (!CanEnableVersionControl.Value)
        {
            return;
        }

        await RequestEnableVersionControlAsync();
        if (_service?.Repository is not null)
        {
            IsTracked.Value = true;
        }
    }

    public async Task DownloadGitAsync()
    {
        if (IsUnavailable.Value)
        {
            await LaunchUriAsync(s_gitDownloadsUri);
        }
    }

    public async Task LoadMoreAsync()
    {
        IProjectVersionControlService? service = _service;
        if (service?.Repository is null || !HasMoreHistory.Value)
        {
            return;
        }

        CancellationToken cancellationToken =
            _serviceBindingCancellation?.Token ?? CancellationToken.None;
        try
        {
            await _historyGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await LoadNextPageCoreAsync(service, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task CommitManualAsync()
    {
        if (_versionControlCoordinator is null || string.IsNullOrWhiteSpace(CommitMessage.Value))
        {
            return;
        }

        try
        {
            CommitResult result = await _versionControlCoordinator.CommitManualAsync(
                CommitMessage.Value.Trim(),
                CancellationToken.None);
            switch (result)
            {
                case CommitResult.NoChanges:
                    StatusMessage.Value = Strings.VersionControl_NothingToCommit;
                    break;
                case CommitResult.Committed:
                    CommitMessage.Value = string.Empty;
                    StatusMessage.Value = Strings.VersionControl_CommitCreated;
                    break;
            }
        }
        catch (GitIdentityRequiredException)
        {
        }
    }

    public async Task SetRemoteAsync()
    {
        await ConfigureRemoteAsync();
    }

    public async Task PublishBranchAsync()
    {
        if (await ConfigureRemoteAsync())
        {
            await PushAsync();
        }
    }

    private async Task<bool> ConfigureRemoteAsync()
    {
        if (_versionControlCoordinator is null || IsRemoteOperationRunning.Value)
        {
            return false;
        }

        string? remoteUrl = await RequestRemoteUrlAsync(
            HasRemote.Value ? RemoteUrl.Value : null);
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        string normalizedUrl = remoteUrl.Trim();
        await _versionControlCoordinator.SetRemoteAsync(
            normalizedUrl,
            CancellationToken.None);
        await RefreshRemotesAsync();
        RemoteUrl.Value = normalizedUrl;
        HasRemote.Value = true;
        StatusMessage.Value = Strings.VersionControl_RemoteConnected;
        return true;
    }

    public Task PushAsync()
    {
        return RunRemoteOperationAsync(
            (progress, cancellationToken) => _versionControlCoordinator!.PushAsync(
                progress,
                cancellationToken),
            Strings.VersionControl_Pushing);
    }

    public Task PullAsync()
    {
        return RunRemoteOperationAsync(
            (_, cancellationToken) => _versionControlCoordinator!.PullAsync(cancellationToken),
            Strings.VersionControl_Pulling);
    }

    public async Task SelectCommitAsync(VersionControlCommitViewModel? commit)
    {
        SelectedCommit.Value = commit;
        if (commit is null)
        {
            _showingDetail.Value = false;
        }

        SelectedFile.Value = null;
        ChangedFiles.Clear();
        DiffLines.Clear();
        CancellationToken cancellationToken = ReplaceSelectionCancellation();
        if (_service is null || commit is null)
        {
            return;
        }

        IReadOnlyList<FileChange> files;
        try
        {
            files = await _service.GetCommitFilesAsync(
                commit.Commit.Sha,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (FileChange file in files)
        {
            ChangedFiles.Add(new VersionControlFileChangeViewModel(file));
        }
    }

    internal async Task OpenCommitDetailAsync(VersionControlCommitViewModel commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        _showingDetail.Value = true;
        await SelectCommitAsync(commit);
    }

    internal void ShowSelectedCommitDetail()
    {
        if (SelectedCommit.Value is not null)
        {
            _showingDetail.Value = true;
        }
    }

    private void ShowHistory()
    {
        _showingDetail.Value = false;
    }

    public async Task SelectFileAsync(VersionControlFileChangeViewModel? file)
    {
        SelectedFile.Value = file;
        DiffLines.Clear();
        CancellationToken cancellationToken = ReplaceSelectionCancellation();
        if (_service is null || SelectedCommit.Value is not { } commit || file is null)
        {
            return;
        }

        string diff;
        try
        {
            diff = await _service.GetDiffAsync(
                commit.Commit.Sha,
                file.Change.Path,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (VersionControlDiffLineViewModel line in VersionControlDiffLineViewModel.Parse(diff))
        {
            DiffLines.Add(line);
        }
    }

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachServiceEvents();
        _serviceBindingCancellation?.Cancel();
        _serviceBindingCancellation?.Dispose();

        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _remoteOperationCancellation?.Cancel();
        _remoteOperationCancellation?.Dispose();
        if (_observedPrimaryActionCommand is not null)
        {
            _observedPrimaryActionCommand.CanExecuteChanged -=
                OnPrimaryActionCanExecuteChanged;
            _observedPrimaryActionCommand = null;
        }

        foreach (VersionControlCommitViewModel commit in Commits)
        {
            commit.Dispose();
        }

        Commits.Clear();
        ChangedFiles.Clear();
        DiffLines.Clear();
        IsSelected.Dispose();
        _disposables.Dispose();
    }

    internal Task<bool> RestoreAsync(CommitInfo commit)
    {
        if (_versionControlCoordinator is null)
        {
            return Task.FromResult(false);
        }

        return RunRestoreRequestAsync(
            () => _versionControlCoordinator.RestoreAsync(
                commit.Sha,
                CancellationToken.None));
    }

    internal Task<bool> RestoreToNewBranchAsync(CommitInfo commit)
    {
        if (_versionControlCoordinator is null)
        {
            return Task.FromResult(false);
        }

        return RunRestoreRequestAsync(async () =>
        {
            string? branchName = await RequestBranchNameAsync(commit);
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return false;
            }

            return await _versionControlCoordinator.RestoreToNewBranchAsync(
                commit.Sha,
                branchName.Trim(),
                CancellationToken.None);
        });
    }

    internal async Task RemoveStaleLockAsync()
    {
        if (_lockRecoveryService is null)
        {
            return;
        }

        await _lockRecoveryService.RemoveRecoverableLockAsync(CancellationToken.None);
        HasRecoverableLock.Value = _lockRecoveryService.RecoverableLock is not null;
    }

    private async Task<bool> RunRestoreRequestAsync(Func<Task<bool>> operation)
    {
        if (Interlocked.CompareExchange(ref _restoreRequestActive, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            return await operation();
        }
        finally
        {
            Volatile.Write(ref _restoreRequestActive, 0);
        }
    }

    private void OnServicePublished(IProjectVersionControlService? service)
    {
        _serviceBindingCancellation?.Cancel();
        _postToUi(() =>
        {
            if (!_disposed)
            {
                Initialization = RebindServiceAsync(service);
            }
        });
    }

    private Task RebindServiceAsync(IProjectVersionControlService? service)
    {
        _serviceBindingCancellation?.Cancel();
        _serviceBindingCancellation?.Dispose();
        _serviceBindingCancellation = new CancellationTokenSource();
        int revision = ++_serviceRevision;

        DetachServiceEvents();
        _service = service;
        _lockRecoveryService = service as IRepositoryLockRecoveryService;
        if (_service is not null)
        {
            _service.StatusChanged += OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable += OnRecoverableLockAvailable;
        }

        ResetRepositoryState();
        return InitializeAsync(
            service,
            revision,
            _serviceBindingCancellation.Token);
    }

    private void DetachServiceEvents()
    {
        if (_service is not null)
        {
            _service.StatusChanged -= OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable -= OnRecoverableLockAvailable;
        }
    }

    private void ResetRepositoryState()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        _remoteOperationCancellation?.Cancel();

        foreach (VersionControlCommitViewModel commit in Commits)
        {
            commit.Dispose();
        }

        Commits.Clear();
        ChangedFiles.Clear();
        DiffLines.Clear();
        SelectedCommit.Value = null;
        SelectedFile.Value = null;
        _showingDetail.Value = false;
        _nextHistoryOffset = 0;
        _aheadCount = 0;
        _behindCount = 0;
        _hasUncommittedChanges = false;

        bool isTracked = _service?.Repository is not null;
        IsTracked.Value = isTracked;
        IsGitAvailable.Value = false;
        IsUnavailable.Value = false;
        IsConflicted.Value = false;
        HasBlockingGuidance.Value = false;
        HasRecoverableLock.Value = _lockRecoveryService?.RecoverableLock is not null;
        DirtySummary.Value = string.Empty;
        StatusMessage.Value = isTracked
            ? string.Empty
            : Strings.VersionControl_NoRepository;
        IsLoading.Value = false;
        HasMoreHistory.Value = isTracked;
        IsHistoryEmpty.Value = true;
        CommitMessage.Value = string.Empty;
        RemoteUrl.Value = string.Empty;
        HasRemote.Value = false;
        RemoteProgress.Value = string.Empty;
        IsNestedRepository.Value = _service?.Repository?.IsNestedInForeignRepo == true;
        RepositoryScopeText.Value =
            _service?.Repository is { IsNestedInForeignRepo: true } repository
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.VersionControl_EnclosingRepositoryScopeFormat,
                    repository.RepoRoot)
                : string.Empty;
        UpdatePrimaryAction();
    }

    private async Task InitializeAsync(
        IProjectVersionControlService? service,
        int revision,
        CancellationToken cancellationToken)
    {
        if (service is null)
        {
            return;
        }

        GitAvailability availability;
        try
        {
            availability = await service.GetAvailabilityAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!IsCurrentService(service, revision, cancellationToken))
        {
            return;
        }

        IsGitAvailable.Value = availability.State == GitAvailabilityState.Installed;
        if (availability.State != GitAvailabilityState.Installed)
        {
            IsUnavailable.Value = true;
            HasBlockingGuidance.Value = true;
            IsTracked.Value = false;
            HasMoreHistory.Value = false;
            StatusMessage.Value = GetAvailabilityMessage(availability);
            return;
        }

        if (service.Repository is null)
        {
            StatusMessage.Value = Strings.VersionControl_NoRepository;
            return;
        }

        WorkspaceStatus status;
        try
        {
            status = await service.GetStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!IsCurrentService(service, revision, cancellationToken))
        {
            return;
        }

        ApplyStatus(status);
        if (!status.HasConflicts)
        {
            try
            {
                await RefreshRemotesAsync(service, cancellationToken);
                await RefreshHistoryAsync(service, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private bool IsCurrentService(
        IProjectVersionControlService service,
        int revision,
        CancellationToken cancellationToken)
    {
        return !_disposed
               && !cancellationToken.IsCancellationRequested
               && revision == _serviceRevision
               && ReferenceEquals(service, _service);
    }

    internal static string GetAvailabilityMessage(GitAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        string stateMessage = availability.State switch
        {
            GitAvailabilityState.VersionTooOld => string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_GitTooOldFormat,
                availability.Version?.ToString() ?? "—"),
            _ => Strings.VersionControl_GitNotInstalled,
        };
        string installMessage = OperatingSystem.IsWindows()
            ? Strings.VersionControl_InstallGitWindows
            : OperatingSystem.IsMacOS()
                ? Strings.VersionControl_InstallGitMacOS
                : Strings.VersionControl_InstallGitLinux;
        return $"{stateMessage}\n\n{installMessage}";
    }

    private async Task RefreshHistoryAsync(
        IProjectVersionControlService? expectedService = null,
        CancellationToken cancellationToken = default)
    {
        IProjectVersionControlService? service = expectedService ?? _service;
        if (service?.Repository is null)
        {
            return;
        }

        if (expectedService is null && !cancellationToken.CanBeCanceled)
        {
            cancellationToken =
                _serviceBindingCancellation?.Token ?? CancellationToken.None;
        }

        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            if (cancellationToken.IsCancellationRequested
                || !ReferenceEquals(service, _service))
            {
                return;
            }

            string? selectedSha = SelectedCommit.Value?.Commit.Sha;
            foreach (VersionControlCommitViewModel commit in Commits)
            {
                commit.Dispose();
            }

            Commits.Clear();
            ChangedFiles.Clear();
            DiffLines.Clear();
            IsHistoryEmpty.Value = true;
            SelectedCommit.Value = null;
            SelectedFile.Value = null;
            _nextHistoryOffset = 0;
            HasMoreHistory.Value = true;
            await LoadNextPageCoreAsync(service, cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !ReferenceEquals(service, _service))
            {
                return;
            }

            if (selectedSha is not null)
            {
                VersionControlCommitViewModel? restoredCommit = Commits.FirstOrDefault(
                    item => string.Equals(
                        item.Commit.Sha,
                        selectedSha,
                        StringComparison.Ordinal));
                if (restoredCommit is null)
                {
                    _showingDetail.Value = false;
                }
                else
                {
                    await SelectCommitAsync(restoredCommit);
                }
            }
            else
            {
                _showingDetail.Value = false;
            }
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private async Task RefreshRemotesAsync()
    {
        IProjectVersionControlService? service = _service;
        if (service?.Repository is null)
        {
            return;
        }

        CancellationToken cancellationToken =
            _serviceBindingCancellation?.Token ?? CancellationToken.None;
        await RefreshRemotesAsync(service, cancellationToken);
    }

    private async Task RefreshRemotesAsync(
        IProjectVersionControlService service,
        CancellationToken cancellationToken)
    {
        RemoteInfo? remote = (await service.GetRemotesAsync(cancellationToken))
            .FirstOrDefault();
        if (cancellationToken.IsCancellationRequested
            || !ReferenceEquals(service, _service))
        {
            return;
        }

        HasRemote.Value = remote is not null;
        RemoteUrl.Value = remote?.Url ?? string.Empty;
    }

    private async Task LoadNextPageCoreAsync(
        IProjectVersionControlService service,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(service, _service))
        {
            return;
        }

        IsLoading.Value = true;
        try
        {
            IReadOnlyList<CommitInfo> page = await service.GetHistoryAsync(
                _nextHistoryOffset,
                HistoryPageSize,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !ReferenceEquals(service, _service))
            {
                return;
            }

            foreach (CommitInfo commit in page)
            {
                Commits.Add(new VersionControlCommitViewModel(
                    this,
                    commit,
                    _relativeTimeFormatter));
            }

            _nextHistoryOffset += page.Count;
            HasMoreHistory.Value = page.Count == HistoryPageSize;
            IsHistoryEmpty.Value = Commits.Count == 0;
            StatusMessage.Value = Commits.Count == 0
                ? Strings.VersionControl_HistoryEmptyHint
                : string.Empty;
        }
        finally
        {
            IsLoading.Value = false;
        }
    }

    private void OnStatusChanged(object? sender, WorkspaceStatus status)
    {
        if (sender is not IProjectVersionControlService eventService
            || !ReferenceEquals(eventService, _service))
        {
            return;
        }

        _postToUi(() =>
        {
            if (_disposed || !ReferenceEquals(eventService, _service))
            {
                return;
            }

            ApplyStatus(status);
            if (!status.HasConflicts)
            {
                CancellationToken cancellationToken =
                    _serviceBindingCancellation?.Token ?? CancellationToken.None;
                _ = RefreshAfterStatusChangedAsync(
                    eventService,
                    cancellationToken);
            }
        });
    }

    private async Task RefreshAfterStatusChangedAsync(
        IProjectVersionControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await RefreshRemotesAsync(service, cancellationToken);
            await RefreshHistoryAsync(service, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        if (!ReferenceEquals(sender, _service))
        {
            return;
        }

        _postToUi(() =>
        {
            if (!_disposed
                && ReferenceEquals(sender, _service)
                && Equals(_lockRecoveryService?.RecoverableLock, lockInfo))
            {
                HasRecoverableLock.Value = true;
            }
        });
    }

    private void ApplyStatus(WorkspaceStatus status)
    {
        IsTracked.Value = _service?.Repository is not null;
        IsConflicted.Value = status.HasConflicts;
        HasBlockingGuidance.Value = IsUnavailable.Value || status.HasConflicts;
        if (status.HasConflicts)
        {
            StatusMessage.Value = Strings.VersionControl_ConflictGuidance;
            HasMoreHistory.Value = false;
        }

        _aheadCount = status.Ahead;
        _behindCount = status.Behind;
        _hasUncommittedChanges = !status.IsClean;
        DirtySummary.Value = status.IsClean
            ? Strings.VersionControl_WorktreeClean
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_DirtySummaryFormat,
                status.Changes.Count);
        UpdatePrimaryAction();
    }

    private void UpdatePrimaryAction()
    {
        VersionControlPrimaryAction action = IsRemoteOperationRunning.Value
            ? new(
                VersionControlPrimaryActionKind.Cancel,
                Strings.Cancel,
                CancelRemoteOperationCommand)
            : _hasUncommittedChanges
                ? new(
                    VersionControlPrimaryActionKind.Commit,
                    Strings.VersionControl_CommitNow,
                    CommitCommand)
                : _behindCount > 0
                    ? new(
                        VersionControlPrimaryActionKind.Pull,
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.VersionControl_PullCountFormat,
                            _behindCount),
                        PullCommand)
                    : _aheadCount > 0
                        ? new(
                            VersionControlPrimaryActionKind.Push,
                            string.Format(
                                CultureInfo.CurrentCulture,
                                Strings.VersionControl_PushCountFormat,
                                _aheadCount),
                            PushCommand)
                        : HasRemote.Value
                            ? new(
                                VersionControlPrimaryActionKind.UpToDate,
                                Strings.VersionControl_UpToDate,
                                _disabledPrimaryActionCommand)
                            : new(
                                VersionControlPrimaryActionKind.PublishBranch,
                                Strings.VersionControl_PublishBranch,
                                PublishBranchCommand);
        ObservePrimaryAction(action);
    }

    private void ObservePrimaryAction(VersionControlPrimaryAction action)
    {
        if (_observedPrimaryActionCommand is not null)
        {
            _observedPrimaryActionCommand.CanExecuteChanged -=
                OnPrimaryActionCanExecuteChanged;
        }

        _primaryAction.Value = action;
        _observedPrimaryActionCommand = action.Command;
        _observedPrimaryActionCommand.CanExecuteChanged +=
            OnPrimaryActionCanExecuteChanged;
        UpdatePrimaryActionCanExecute();
    }

    private void OnPrimaryActionCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdatePrimaryActionCanExecute();
    }

    private void UpdatePrimaryActionCanExecute()
    {
        _isPrimaryActionEnabled.Value =
            PrimaryAction.Value.Command.CanExecute(null);
    }

    private void InvokePrimaryAction()
    {
        VersionControlPrimaryAction action = PrimaryAction.Value;
        if (action.Command.CanExecute(null))
        {
            action.Command.Execute(null);
        }
    }

    private CancellationToken ReplaceSelectionCancellation()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = new CancellationTokenSource();
        return _selectionCancellation.Token;
    }

    private async Task RunRemoteOperationAsync(
        Func<IProgress<string>, CancellationToken, Task<RemoteOpResult>> operation,
        string initialProgress)
    {
        if (_versionControlCoordinator is null || IsRemoteOperationRunning.Value)
        {
            return;
        }

        _remoteOperationCancellation?.Dispose();
        _remoteOperationCancellation = new CancellationTokenSource();
        IsRemoteOperationRunning.Value = true;
        RemoteProgress.Value = initialProgress;
        var progress = new CallbackProgress<string>(value =>
            _postToUi(() => RemoteProgress.Value = value));
        try
        {
            RemoteOpResult result = await operation(
                progress,
                _remoteOperationCancellation.Token);
            if (result is RemoteOpResult.Success)
            {
                StatusMessage.Value = Strings.VersionControl_RemoteOperationSucceeded;
                await RefreshRemotesAsync();
            }
            else if (result is not RemoteOpResult.Failed { Stderr.Length: 0 })
            {
                await ShowRemoteResultAsync(result);
            }
        }
        catch (OperationCanceledException) when (_remoteOperationCancellation.IsCancellationRequested)
        {
            StatusMessage.Value = Strings.VersionControl_RemoteOperationCanceled;
        }
        finally
        {
            IsRemoteOperationRunning.Value = false;
            _remoteOperationCancellation.Dispose();
            _remoteOperationCancellation = null;
        }
    }

    private void CancelRemoteOperation()
    {
        _remoteOperationCancellation?.Cancel();
    }

    private static void PostToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    internal static string GetRemoteResultMessage(RemoteOpResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            RemoteOpResult.AuthFailed authFailed => authFailed.Guidance,
            RemoteOpResult.Diverged => Strings.VersionControl_Diverged,
            RemoteOpResult.Offline => Strings.VersionControl_Offline,
            RemoteOpResult.Failed failed => failed.Stderr,
            _ => string.Empty,
        };
    }

    private static Task ShowRemoteResultNotificationAsync(RemoteOpResult result)
    {
        string message = GetRemoteResultMessage(result);
        if (!string.IsNullOrWhiteSpace(message))
        {
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                message);
        }

        return Task.CompletedTask;
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value)
        {
            callback(value);
        }
    }
}

internal sealed class VersionControlRelativeTimeFormatter
{
    private static readonly ResourceManager s_resourceManager =
        new("Beutl.Language.Strings", typeof(Strings).Assembly);

    private readonly TimeProvider _timeProvider;
    private readonly CultureInfo _culture;

    public VersionControlRelativeTimeFormatter(
        TimeProvider timeProvider,
        CultureInfo culture)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
    }

    public string Format(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = _timeProvider.GetUtcNow() - timestamp.ToUniversalTime();
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return GetString("VersionControl_TimeJustNow");
        }

        int minutes = (int)Math.Floor(elapsed.TotalMinutes);
        if (minutes < 60)
        {
            return minutes == 1
                ? GetString("VersionControl_TimeMinuteAgo")
                : FormatCount("VersionControl_TimeMinutesAgoFormat", minutes);
        }

        int hours = (int)Math.Floor(elapsed.TotalHours);
        if (hours < 24)
        {
            return hours == 1
                ? GetString("VersionControl_TimeHourAgo")
                : FormatCount("VersionControl_TimeHoursAgoFormat", hours);
        }

        int days = (int)Math.Floor(elapsed.TotalDays);
        return days == 1
            ? GetString("VersionControl_TimeDayAgo")
            : FormatCount("VersionControl_TimeDaysAgoFormat", days);
    }

    public string FormatAbsoluteLocal(DateTimeOffset timestamp)
    {
        return TimeZoneInfo.ConvertTime(timestamp, _timeProvider.LocalTimeZone)
            .ToString("g", _culture);
    }

    private string FormatCount(string key, int value)
    {
        return string.Format(_culture, GetString(key), value);
    }

    private string GetString(string key)
    {
        return s_resourceManager.GetString(key, _culture)
               ?? throw new MissingManifestResourceException(
                   $"The localized resource '{key}' is missing.");
    }
}

public sealed class VersionControlCommitViewModel : IDisposable
{
    private readonly VersionControlTabViewModel _owner;

    internal VersionControlCommitViewModel(
        VersionControlTabViewModel owner,
        CommitInfo commit,
        VersionControlRelativeTimeFormatter relativeTimeFormatter)
    {
        _owner = owner;
        Commit = commit;
        KindText = GetKindText(commit.Kind);
        DisplayMessage = commit.Subject;
        AuthorAndRelativeDate = string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1}",
            commit.AuthorName,
            relativeTimeFormatter.Format(commit.AuthorDate));
        AbsoluteLocalDate = relativeTimeFormatter.FormatAbsoluteLocal(commit.AuthorDate);
        RestoreCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RestoreAsync(Commit));
        RestoreToNewBranchCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RestoreToNewBranchAsync(Commit));
    }

    public CommitInfo Commit { get; }

    public string KindText { get; }

    public bool IsManual => Commit.Kind == SnapshotKind.Manual;

    public bool IsSave => Commit.Kind == SnapshotKind.Save;

    public bool IsClose => Commit.Kind == SnapshotKind.Close;

    public bool IsSafety => Commit.Kind == SnapshotKind.Safety;

    public bool IsRestore => Commit.Kind == SnapshotKind.Restore;

    public bool IsInit => Commit.Kind == SnapshotKind.Init;

    public string DisplayMessage { get; }

    public string AuthorAndRelativeDate { get; }

    public string AbsoluteLocalDate { get; }

    public AsyncReactiveCommand RestoreCommand { get; }

    public AsyncReactiveCommand RestoreToNewBranchCommand { get; }

    public void Dispose()
    {
        RestoreCommand.Dispose();
        RestoreToNewBranchCommand.Dispose();
    }

    private static string GetKindText(SnapshotKind kind)
    {
        return kind switch
        {
            SnapshotKind.Save => Strings.VersionControl_SnapshotSave,
            SnapshotKind.Close => Strings.VersionControl_SnapshotClose,
            SnapshotKind.Safety => Strings.VersionControl_SnapshotSafety,
            SnapshotKind.Restore => Strings.VersionControl_SnapshotRestore,
            SnapshotKind.Init => Strings.VersionControl_SnapshotInit,
            _ => Strings.VersionControl_SnapshotManual,
        };
    }
}

public sealed class VersionControlFileChangeViewModel
{
    public VersionControlFileChangeViewModel(FileChange change)
    {
        Change = change;
    }

    public FileChange Change { get; }

    public string StatusText => Change.Status switch
    {
        FileChangeStatus.Added => "A",
        FileChangeStatus.Deleted => "D",
        FileChangeStatus.Renamed => "R",
        _ => "M",
    };

    public string PathText => Change.OldPath is null
        ? Change.Path
        : $"{Change.OldPath} → {Change.Path}";
}

public enum VersionControlDiffLineKind
{
    Context,
    Added,
    Removed,
    Header,
}

public sealed record VersionControlDiffLineViewModel(
    string Text,
    VersionControlDiffLineKind Kind)
{
    public bool IsAdded => Kind == VersionControlDiffLineKind.Added;

    public bool IsRemoved => Kind == VersionControlDiffLineKind.Removed;

    public bool IsHeader => Kind == VersionControlDiffLineKind.Header;

    public static IReadOnlyList<VersionControlDiffLineViewModel> Parse(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return diff.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => new VersionControlDiffLineViewModel(line, GetKind(line)))
            .ToArray();
    }

    private static VersionControlDiffLineKind GetKind(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal)
            || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("@@", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Header;
        }

        if (line.StartsWith("+", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Added;
        }

        if (line.StartsWith("-", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Removed;
        }

        return VersionControlDiffLineKind.Context;
    }
}
