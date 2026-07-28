using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Resources;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

public sealed class VersionControlTabViewModel : IToolContext
{
    internal const int HistoryPageSize = 50;
    private static readonly Uri s_gitDownloadsUri = new("https://git-scm.com/downloads");

    private readonly IEditorContext _editorContext;
    private readonly IProjectVersionControlService? _service;
    private readonly IRepositoryLockRecoveryService? _lockRecoveryService;
    private readonly IProjectVersionControlCoordinator? _versionControlCoordinator;
    private readonly Action<Action> _postToUi;
    private readonly VersionControlRelativeTimeFormatter _relativeTimeFormatter;
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private CancellationTokenSource? _selectionCancellation;
    private CancellationTokenSource? _remoteOperationCancellation;
    private int _nextHistoryOffset;
    private bool _disposed;

    public VersionControlTabViewModel(
        ToolTabExtension extension,
        IEditorContext editorContext)
        : this(
            extension,
            editorContext,
            editorContext.GetService(typeof(IProjectVersionControlService))
                as IProjectVersionControlService,
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
        IProjectVersionControlService? service,
        IProjectVersionControlCoordinator? versionControlCoordinator,
        Action<Action> postToUi,
        TimeProvider? timeProvider = null,
        CultureInfo? culture = null)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        _editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        _service = service;
        _lockRecoveryService = service as IRepositoryLockRecoveryService;
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
        BranchText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        AheadBehindText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        AheadBadgeText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        BehindBadgeText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        HasAhead = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasBehind = new ReactivePropertySlim<bool>()
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
        CurrentBranch = new ReactivePropertySlim<BranchInfo?>()
            .DisposeWith(_disposables);
        SelectedBranch = new ReactivePropertySlim<BranchInfo?>()
            .DisposeWith(_disposables);
        IsBranchSwitchPending = SelectedBranch.CombineLatest(
                CurrentBranch,
                static (selected, current) =>
                    selected is not null
                    && current is not null
                    && !string.Equals(selected.Name, current.Name, StringComparison.Ordinal))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        RemoteUrl = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        HasRemote = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        RemoteProgress = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        IsRemoteOperationRunning = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsRemoteExpanded = new ReactivePropertySlim<bool>()
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
            static (tracked, blocked) => tracked && !blocked);
        CommitCommand = new AsyncReactiveCommand(
                canMutate.CombineLatest(
                    CommitMessage.Select(static message => !string.IsNullOrWhiteSpace(message)),
                    static (canRun, hasMessage) => canRun && hasMessage))
            .WithSubscribe(CommitManualAsync)
            .DisposeWith(_disposables);
        CreateBranchCommand = new AsyncReactiveCommand(canMutate)
            .WithSubscribe(CreateBranchAsync)
            .DisposeWith(_disposables);
        SwitchBranchCommand = new AsyncReactiveCommand(
                canMutate.CombineLatest(
                    IsBranchSwitchPending,
                    static (canRun, pending) => canRun && pending))
            .WithSubscribe(SwitchSelectedBranchAsync)
            .DisposeWith(_disposables);
        SetRemoteCommand = new AsyncReactiveCommand(
                canMutate.CombineLatest(
                    RemoteUrl.Select(static url => !string.IsNullOrWhiteSpace(url)),
                    static (canRun, hasUrl) => canRun && hasUrl))
            .WithSubscribe(SetRemoteAsync)
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
        RequestBranchNameAsync = ShowBranchNameDialogAsync;
        RequestNewBranchNameAsync = ShowNewBranchDialogAsync;
        ShowRemoteResultAsync = ShowRemoteResultDialogAsync;
        RequestEnableVersionControlAsync = static () => Task.CompletedTask;
        LaunchUriAsync = static _ => Task.FromResult(false);
        IsRemoteOperationRunning
            .Where(static running => running)
            .Subscribe(_ => IsRemoteExpanded.Value = true)
            .DisposeWith(_disposables);
        IsRemoteExpanded
            .Where(static expanded => !expanded)
            .Subscribe(_ =>
            {
                if (IsRemoteOperationRunning.Value)
                {
                    IsRemoteExpanded.Value = true;
                }
            })
            .DisposeWith(_disposables);

        if (_service is not null)
        {
            _service.StatusChanged += OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable += OnRecoverableLockAvailable;
        }

        Initialization = InitializeAsync();
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

    public ReactivePropertySlim<string> BranchText { get; }

    public ReactivePropertySlim<string> AheadBehindText { get; }

    public ReactivePropertySlim<string> AheadBadgeText { get; }

    public ReactivePropertySlim<string> BehindBadgeText { get; }

    public ReactivePropertySlim<bool> HasAhead { get; }

    public ReactivePropertySlim<bool> HasBehind { get; }

    public ReactivePropertySlim<string> DirtySummary { get; }

    public ReactivePropertySlim<string> StatusMessage { get; }

    public ReactivePropertySlim<bool> IsLoading { get; }

    public ReactivePropertySlim<bool> HasMoreHistory { get; }

    public ReactivePropertySlim<bool> IsHistoryEmpty { get; }

    public ObservableCollection<VersionControlCommitViewModel> Commits { get; } = [];

    public ObservableCollection<VersionControlFileChangeViewModel> ChangedFiles { get; } = [];

    public ObservableCollection<VersionControlDiffLineViewModel> DiffLines { get; } = [];

    public ObservableCollection<BranchInfo> Branches { get; } = [];

    public ReactivePropertySlim<VersionControlCommitViewModel?> SelectedCommit { get; }

    public ReactivePropertySlim<VersionControlFileChangeViewModel?> SelectedFile { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSelectedCommit { get; }

    public ReadOnlyReactivePropertySlim<bool> HasSelectedFile { get; }

    public ReactivePropertySlim<string> CommitMessage { get; }

    public ReactivePropertySlim<BranchInfo?> CurrentBranch { get; }

    public ReactivePropertySlim<BranchInfo?> SelectedBranch { get; }

    public ReadOnlyReactivePropertySlim<bool> IsBranchSwitchPending { get; }

    public ReactivePropertySlim<string> RemoteUrl { get; }

    public ReactivePropertySlim<bool> HasRemote { get; }

    public ReactivePropertySlim<string> RemoteProgress { get; }

    public ReactivePropertySlim<bool> IsRemoteOperationRunning { get; }

    public ReactivePropertySlim<bool> IsRemoteExpanded { get; }

    public ReactivePropertySlim<bool> IsNestedRepository { get; }

    public ReactivePropertySlim<string> RepositoryScopeText { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEnableVersionControl { get; }

    public AsyncReactiveCommand LoadMoreCommand { get; }

    public AsyncReactiveCommand EnableVersionControlCommand { get; }

    public AsyncReactiveCommand DownloadGitCommand { get; }

    public AsyncReactiveCommand RemoveStaleLockCommand { get; }

    public AsyncReactiveCommand CommitCommand { get; }

    public AsyncReactiveCommand CreateBranchCommand { get; }

    public AsyncReactiveCommand SwitchBranchCommand { get; }

    public AsyncReactiveCommand SetRemoteCommand { get; }

    public AsyncReactiveCommand PushCommand { get; }

    public AsyncReactiveCommand PullCommand { get; }

    public ReactiveCommandSlim CancelRemoteOperationCommand { get; }

    public Task Initialization { get; }

    public Func<CommitInfo, Task<string?>> RequestBranchNameAsync { get; set; }

    public Func<Task<string?>> RequestNewBranchNameAsync { get; set; }

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
        if (_service?.Repository is null || !HasMoreHistory.Value)
        {
            return;
        }

        await _historyGate.WaitAsync();
        try
        {
            await LoadNextPageCoreAsync();
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

    public async Task SwitchSelectedBranchAsync()
    {
        BranchInfo? branch = SelectedBranch.Value;
        if (_versionControlCoordinator is null
            || branch is null
            || !IsBranchSwitchPending.Value)
        {
            return;
        }

        try
        {
            bool switched = await _versionControlCoordinator.SwitchBranchAsync(
                branch.Name,
                CancellationToken.None);
            if (switched)
            {
                await RefreshRepositoryMetadataAsync();
            }
            else
            {
                RevertBranchSelection();
            }
        }
        catch
        {
            RevertBranchSelection();
            throw;
        }
    }

    public async Task CreateBranchAsync()
    {
        if (_versionControlCoordinator is null)
        {
            return;
        }

        string? branchName = await RequestNewBranchNameAsync();
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        if (await _versionControlCoordinator.CreateBranchAsync(
                branchName.Trim(),
                CancellationToken.None))
        {
            await RefreshRepositoryMetadataAsync();
        }
    }

    public async Task SetRemoteAsync()
    {
        if (_versionControlCoordinator is null || string.IsNullOrWhiteSpace(RemoteUrl.Value))
        {
            return;
        }

        await _versionControlCoordinator.SetRemoteAsync(
            RemoteUrl.Value.Trim(),
            CancellationToken.None);
        await RefreshRemotesAsync();
        StatusMessage.Value = Strings.VersionControl_RemoteConnected;
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
        SelectedFile.Value = null;
        ChangedFiles.Clear();
        DiffLines.Clear();
        CancellationToken cancellationToken = ReplaceSelectionCancellation();
        if (_service is null || commit is null)
        {
            return;
        }

        IReadOnlyList<FileChange> files = await _service.GetCommitFilesAsync(
            commit.Commit.Sha,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (FileChange file in files)
        {
            ChangedFiles.Add(new VersionControlFileChangeViewModel(file));
        }
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

        string diff = await _service.GetDiffAsync(
            commit.Commit.Sha,
            file.Change.Path,
            cancellationToken);
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
        if (_service is not null)
        {
            _service.StatusChanged -= OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable -= OnRecoverableLockAvailable;
        }

        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _remoteOperationCancellation?.Cancel();
        _remoteOperationCancellation?.Dispose();
        foreach (VersionControlCommitViewModel commit in Commits)
        {
            commit.Dispose();
        }

        Commits.Clear();
        ChangedFiles.Clear();
        DiffLines.Clear();
        Branches.Clear();
        IsSelected.Dispose();
        _disposables.Dispose();
    }

    internal async Task<bool> RestoreAsync(CommitInfo commit)
    {
        if (_versionControlCoordinator is null)
        {
            return false;
        }

        return await _versionControlCoordinator.RestoreAsync(
            commit.Sha,
            CancellationToken.None);
    }

    internal async Task<bool> RestoreToNewBranchAsync(CommitInfo commit)
    {
        if (_versionControlCoordinator is null)
        {
            return false;
        }

        string? branchName = await RequestBranchNameAsync(commit);
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return false;
        }

        return await _versionControlCoordinator.RestoreToNewBranchAsync(
            commit.Sha,
            branchName.Trim(),
            CancellationToken.None);
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

    private async Task InitializeAsync()
    {
        if (_service is null)
        {
            return;
        }

        GitAvailability availability = await _service.GetAvailabilityAsync(CancellationToken.None);
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

        if (_service.Repository is null)
        {
            StatusMessage.Value = Strings.VersionControl_NoRepository;
            return;
        }

        WorkspaceStatus status = await _service.GetStatusAsync(CancellationToken.None);
        ApplyStatus(status);
        if (!status.HasConflicts)
        {
            await RefreshRepositoryMetadataAsync();
            await RefreshHistoryAsync();
        }
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

    private async Task RefreshHistoryAsync()
    {
        if (_service?.Repository is null)
        {
            return;
        }

        await _historyGate.WaitAsync();
        try
        {
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
            await LoadNextPageCoreAsync();
            if (selectedSha is not null)
            {
                SelectedCommit.Value = Commits.FirstOrDefault(
                    item => string.Equals(
                        item.Commit.Sha,
                        selectedSha,
                        StringComparison.Ordinal));
            }
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private async Task RefreshRepositoryMetadataAsync()
    {
        if (_service?.Repository is null)
        {
            return;
        }

        string? pendingBranchName = IsBranchSwitchPending.Value
            ? SelectedBranch.Value?.Name
            : null;
        IReadOnlyList<BranchInfo> branches = await _service.GetBranchesAsync(
            CancellationToken.None);
        Branches.Clear();
        foreach (BranchInfo branch in branches)
        {
            Branches.Add(branch);
        }

        BranchInfo? currentBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
        CurrentBranch.Value = currentBranch;
        SelectedBranch.Value = pendingBranchName is null
            ? currentBranch
            : Branches.FirstOrDefault(branch =>
                string.Equals(branch.Name, pendingBranchName, StringComparison.Ordinal))
              ?? currentBranch;
        await RefreshRemotesAsync();
    }

    private async Task RefreshRemotesAsync()
    {
        if (_service?.Repository is null)
        {
            return;
        }

        RemoteInfo? remote = (await _service.GetRemotesAsync(CancellationToken.None))
            .FirstOrDefault();
        HasRemote.Value = remote is not null;
        if (remote is not null)
        {
            RemoteUrl.Value = remote.Url;
        }
    }

    private async Task LoadNextPageCoreAsync()
    {
        if (_service is null)
        {
            return;
        }

        IsLoading.Value = true;
        try
        {
            IReadOnlyList<CommitInfo> page = await _service.GetHistoryAsync(
                _nextHistoryOffset,
                HistoryPageSize,
                CancellationToken.None);
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
        _postToUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            ApplyStatus(status);
            if (!status.HasConflicts)
            {
                _ = RefreshRepositoryMetadataAsync();
                _ = RefreshHistoryAsync();
            }
        });
    }

    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        _postToUi(() =>
        {
            if (!_disposed
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

        BranchText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_BranchFormat,
            status.Branch ?? "—");
        AheadBehindText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_AheadBehindFormat,
            status.Ahead,
            status.Behind);
        HasAhead.Value = status.Ahead > 0;
        HasBehind.Value = status.Behind > 0;
        AheadBadgeText.Value = $"↑{status.Ahead.ToString(CultureInfo.CurrentCulture)}";
        BehindBadgeText.Value = $"↓{status.Behind.ToString(CultureInfo.CurrentCulture)}";
        DirtySummary.Value = status.IsClean
            ? Strings.VersionControl_WorktreeClean
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_DirtySummaryFormat,
                status.Changes.Count);
    }

    private void RevertBranchSelection()
    {
        SelectedBranch.Value = CurrentBranch.Value;
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
                await RefreshRepositoryMetadataAsync();
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

    private static async Task<string?> ShowBranchNameDialogAsync(CommitInfo commit)
    {
        var textBox = new TextBox
        {
            Watermark = Strings.VersionControl_BranchName,
            Text = $"restore-{commit.ShortSha}",
        };
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_CreateBranchTitle,
            Content = textBox,
            PrimaryButtonText = Strings.VersionControl_RestoreToNewBranch,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private static async Task<string?> ShowNewBranchDialogAsync()
    {
        var textBox = new TextBox
        {
            Watermark = Strings.VersionControl_BranchName,
        };
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_NewBranch,
            Content = textBox,
            PrimaryButtonText = Strings.VersionControl_CreateBranch,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private static async Task ShowRemoteResultDialogAsync(RemoteOpResult result)
    {
        string message = GetRemoteResultMessage(result);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_ErrorTitle,
            Content = message,
            CloseButtonText = Strings.Close,
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
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
