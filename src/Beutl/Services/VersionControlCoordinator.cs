using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class VersionControlCoordinator : IProjectVersionControlCoordinator, IDisposable
{
    private const string SaveSnapshotMessage = "beutl: snapshot on save";
    private const string CloseSnapshotMessage = "beutl: snapshot on close";
    private const string RestoreSafetySnapshotMessage = "beutl: safety snapshot before restore";
    private const string SwitchSafetySnapshotMessage = "beutl: safety snapshot before switch";
    private const string PullSafetySnapshotMessage = "beutl: safety snapshot before pull";

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlConfig _config;
    private readonly GitInstallationLocator _installationLocator;
    private readonly IDisposable _projectSubscription;
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinator>();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _lockRecoveryGate = new(1, 1);
    private readonly ReactivePropertySlim<bool> _isGitAvailable = new();
    private readonly ReactivePropertySlim<bool> _isTracked = new();
    private CancellationTokenSource? _activationCancellation;
    private Task _activationTask = Task.CompletedTask;
    private IProjectVersionControlService? _currentService;
    private int _activationRevision;
    private int _availabilityRevision;
    private int _lifecycleUsers;
    private bool _preserveServiceAcrossClose;
    private bool _disposed;

    public VersionControlCoordinator(
        ProjectService projectService,
        EditorService editorService)
        : this(
            projectService,
            editorService,
            GlobalConfiguration.Instance.VersionControlConfig,
            installationLocator: null)
    {
    }

    internal VersionControlCoordinator(
        ProjectService projectService,
        EditorService editorService,
        VersionControlConfig config,
        GitInstallationLocator? installationLocator)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _installationLocator = installationLocator ?? new GitInstallationLocator(config);
        ConfirmRestoreAsync = ShowRestoreConfirmationAsync;
        ConfirmSwitchBranchAsync = ShowSwitchBranchConfirmationAsync;
        ConfirmPullAsync = ShowPullConfirmationAsync;
        ConfirmUseEnclosingRepositoryAsync = ShowEnclosingRepositoryConfirmationAsync;
        ConfirmRemoveStaleLockAsync = ShowStaleLockConfirmationAsync;
        WarnConflictMarkersAsync = ShowConflictMarkerWarningAsync;
        RequestIdentityAsync = static _ => Task.FromResult(false);
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

    public IProjectVersionControlService? CurrentService => _currentService;

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

    internal Func<IProjectVersionControlService, Task<bool>> RequestIdentityAsync { get; set; }

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
        if (!_disposed && revision == Volatile.Read(ref _availabilityRevision))
        {
            _isGitAvailable.Value = availability.State == GitAvailabilityState.Installed;
        }

        return availability;
    }

    public async Task<bool> InitializeCurrentProjectAsync(
        Func<IProjectVersionControlService, Task<bool>> requestIdentityAsync,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestIdentityAsync);

        Project project = _projectService.CurrentProject.Value
                          ?? throw new InvalidOperationException("No project is open.");
        await _activationTask.WaitAsync(cancellationToken);
        IProjectVersionControlService service = _currentService
                                                ?? throw new InvalidOperationException(
                                                    "The version control service is not available.");
        string projectRoot = GetProjectRoot(project);
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
                if (!await requestIdentityAsync(service))
                {
                    return false;
                }

                await service.InitializeAsync(options, cancellationToken);
            }
        }
        catch (VersionControlConflictedException ex)
        {
            NotificationService.ShowWarning(Strings.VersionControl, ex.Guidance);
            return false;
        }

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

    public Task NotifyClosingAsync(CancellationToken cancellationToken = default)
    {
        return CommitSnapshotAsync(
            _config.AutoCommitOnClose,
            CloseSnapshotMessage,
            SnapshotKind.Close,
            cancellationToken);
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
        IProjectVersionControlService service = GetTrackedService();
        try
        {
            return await service.CommitAllAsync(
                message.Trim(),
                SnapshotKind.Manual,
                cancellationToken);
        }
        catch (GitIdentityRequiredException)
        {
            if (!await RequestIdentityAsync(service))
            {
                throw;
            }

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
        return GetTrackedService().SetRemoteAsync(url.Trim(), cancellationToken);
    }

    public Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        return GetTrackedService().PushAsync(progress, cancellationToken);
    }

    public Task<RemoteOpResult> PullAsync(CancellationToken cancellationToken = default)
    {
        return RunPullCycleAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
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
            ReplaceService(null);
        }

        _isGitAvailable.Dispose();
        _isTracked.Dispose();
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
            if (!EnsureWorktreeOperationAllowed())
            {
                return false;
            }

            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlService service = GetTrackedService();
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

            if (!status.IsClean)
            {
                CommitResult result = await service.CommitAllAsync(
                    SwitchSafetySnapshotMessage,
                    SnapshotKind.Safety,
                    cancellationToken);
                EnsureAutomaticSnapshotWasNotSkipped(result);
            }

            CommitInfo originalHead = await GetHeadAsync(service, cancellationToken);
            string? originalBranch = status.Branch;
            bool projectClosed = false;
            _preserveServiceAcrossClose = true;
            try
            {
                await CloseProjectForOperationAsync(cancellationToken);
                projectClosed = true;
                if (create)
                {
                    await service.CreateBranchAsync(
                        branchName,
                        "HEAD",
                        cancellationToken);
                }
                else
                {
                    await service.SwitchBranchAsync(branchName, cancellationToken);
                }

                await ReopenProjectAsync(projectFile);
                return true;
            }
            catch (Exception ex)
            {
                Exception? recoveryFailure = projectClosed
                    ? await TryRestoreOriginalStateAsync(
                        service,
                        originalHead.Sha,
                        originalBranch,
                        branchMayHaveChanged: true,
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
                FinishPreservedClose();
            }
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
            if (!EnsureWorktreeOperationAllowed())
            {
                return new RemoteOpResult.Failed(Strings.VersionControl_ExportInProgress);
            }

            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlService service = GetTrackedService();
            WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
            if (!EnsureRepositoryIsNotConflicted(status))
            {
                return new RemoteOpResult.Failed(Strings.VersionControl_ConflictGuidance);
            }

            if (!await ConfirmPullAsync(cancellationToken))
            {
                return new RemoteOpResult.Failed(string.Empty);
            }

            if (!status.IsClean)
            {
                CommitResult result = await service.CommitAllAsync(
                    PullSafetySnapshotMessage,
                    SnapshotKind.Safety,
                    cancellationToken);
                EnsureAutomaticSnapshotWasNotSkipped(result);
            }

            CommitInfo originalHead = await GetHeadAsync(service, cancellationToken);
            string? originalBranch = status.Branch;
            bool projectClosed = false;
            _preserveServiceAcrossClose = true;
            try
            {
                await CloseProjectForOperationAsync(cancellationToken);
                projectClosed = true;
                RemoteOpResult result = await service.PullFastForwardAsync(cancellationToken);
                await ReopenProjectAsync(projectFile);
                return result;
            }
            catch (Exception ex)
            {
                Exception? recoveryFailure = projectClosed
                    ? await TryRestoreOriginalStateAsync(
                        service,
                        originalHead.Sha,
                        originalBranch,
                        branchMayHaveChanged: false,
                        projectFile)
                    : null;
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
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
                FinishPreservedClose();
            }
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
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

            if (_editorService.IsExportRunning)
            {
                NotificationService.ShowWarning(
                    Strings.VersionControl,
                    Strings.VersionControl_ExportInProgress);
                return false;
            }

            Project project = _projectService.CurrentProject.Value
                              ?? throw new InvalidOperationException("No project is open.");
            string projectFile = project.Uri?.LocalPath
                                 ?? throw new InvalidOperationException(
                                     "The project has no file path.");
            IProjectVersionControlService service = _currentService
                                                    ?? throw new InvalidOperationException(
                                                        "Version control is not available.");
            if (service.Repository is null)
            {
                throw new InvalidOperationException(
                    "The open project is not tracked with Git.");
            }

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

            if (!status.IsClean)
            {
                CommitResult result = await service.CommitAllAsync(
                    RestoreSafetySnapshotMessage,
                    SnapshotKind.Safety,
                    cancellationToken);
                EnsureAutomaticSnapshotWasNotSkipped(result);
            }

            CommitInfo originalHead = (await service.GetHistoryAsync(
                    0,
                    1,
                    cancellationToken))
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "The repository does not contain a version to preserve.");
            string? originalBranch = status.Branch;
            bool projectClosed = false;
            _preserveServiceAcrossClose = true;
            try
            {
                await _projectService.CloseProject(cancellationToken);
                projectClosed = _projectService.CurrentProject.Value is null;
                if (!projectClosed)
                {
                    throw new InvalidOperationException(
                        "The project could not be closed before restoring files.");
                }

                if (branchName is null)
                {
                    await service.RestoreWorktreeFromAsync(sha, cancellationToken);
                    CommitResult restoreResult = await service.CommitAllAsync(
                        $"beutl: restore project state from {GetShortSha(sha)}",
                        SnapshotKind.Restore,
                        cancellationToken);
                    EnsureAutomaticSnapshotWasNotSkipped(restoreResult);
                }
                else
                {
                    await service.CreateBranchAsync(branchName, sha, cancellationToken);
                }

                await _projectService.OpenProject(projectFile);
                EnsureProjectReopened(projectFile);
                return true;
            }
            catch (Exception ex)
            {
                Exception? recoveryFailure = null;
                if (projectClosed)
                {
                    recoveryFailure = await TryRestoreOriginalStateAsync(
                        service,
                        originalHead.Sha,
                        originalBranch,
                        branchName is not null,
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

                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
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
                _preserveServiceAcrossClose = false;
                if (_projectService.CurrentProject.Value is null)
                {
                    ReplaceService(null);
                }
            }
        }
        finally
        {
            if (gateEntered)
            {
                _lifecycleGate.Release();
            }

            if (Interlocked.Decrement(ref _lifecycleUsers) == 0 && _disposed)
            {
                ReplaceService(null);
            }
        }
    }

    private async Task<Exception?> TryRestoreOriginalStateAsync(
        IProjectVersionControlService service,
        string originalSha,
        string? originalBranch,
        bool branchMayHaveChanged,
        string projectFile)
    {
        try
        {
            if (branchMayHaveChanged && originalBranch is not null)
            {
                WorkspaceStatus currentStatus = await service.GetStatusAsync(CancellationToken.None);
                if (!string.Equals(currentStatus.Branch, originalBranch, StringComparison.Ordinal))
                {
                    await service.SwitchBranchAsync(originalBranch, CancellationToken.None);
                }
            }

            await service.RestoreWorktreeFromAsync(originalSha, CancellationToken.None);
        }
        catch (Exception recoveryException)
        {
            return recoveryException;
        }

        try
        {
            await _projectService.OpenProject(projectFile);
            EnsureProjectReopened(projectFile);
            return null;
        }
        catch (Exception reopenException)
        {
            return reopenException;
        }
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

    private IProjectVersionControlService GetTrackedService()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IProjectVersionControlService service = _currentService
                                                ?? throw new InvalidOperationException(
                                                    "Version control is not available.");
        if (service.Repository is null)
        {
            throw new InvalidOperationException(
                "The open project is not tracked with Git.");
        }

        return service;
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

    private bool EnsureWorktreeOperationAllowed()
    {
        if (!_editorService.IsExportRunning)
        {
            return true;
        }

        NotificationService.ShowWarning(
            Strings.VersionControl,
            Strings.VersionControl_ExportInProgress);
        return false;
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

    private static async Task<CommitInfo> GetHeadAsync(
        IProjectVersionControlService service,
        CancellationToken cancellationToken)
    {
        return (await service.GetHistoryAsync(0, 1, cancellationToken)).FirstOrDefault()
               ?? throw new InvalidOperationException(
                   "The repository does not contain a version to preserve.");
    }

    private async Task CloseProjectForOperationAsync(CancellationToken cancellationToken)
    {
        await _projectService.CloseProject(cancellationToken);
        if (_projectService.CurrentProject.Value is not null)
        {
            throw new InvalidOperationException(
                "The project could not be closed before changing version-controlled files.");
        }
    }

    private async Task ReopenProjectAsync(string projectFile)
    {
        await _projectService.OpenProject(projectFile);
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

    private void FinishPreservedClose()
    {
        _preserveServiceAcrossClose = false;
        if (_projectService.CurrentProject.Value is null)
        {
            ReplaceService(null);
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
            ReplaceService(null);
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

    private static async Task<bool> ShowRestoreConfirmationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_Restore,
            Content = Strings.VersionControl_RestoreConfirmation,
            PrimaryButtonText = Strings.VersionControl_Restore,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static async Task<bool> ShowSwitchBranchConfirmationAsync(
        string branchName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_SwitchBranch,
            Content = string.Format(
                Strings.VersionControl_SwitchBranchConfirmation,
                branchName),
            PrimaryButtonText = Strings.VersionControl_SwitchBranch,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static async Task<bool> ShowPullConfirmationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_Pull,
            Content = Strings.VersionControl_PullConfirmation,
            PrimaryButtonText = Strings.VersionControl_Pull,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static async Task<bool> ShowEnclosingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl,
            Content = $"{Strings.VersionControl_EnclosingRepositoryFound}\n\n{repository.RepoRoot}",
            PrimaryButtonText = Strings.VersionControl_UseEnclosingRepository,
            CloseButtonText = Strings.VersionControl_LeaveUnmanaged,
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static async Task<bool> ShowStaleLockConfirmationAsync(
        RepositoryLockInfo lockInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl,
            Content = $"{Strings.VersionControl_StaleLockConfirmation}\n\n{lockInfo.LockPath}",
            PrimaryButtonText = Strings.VersionControl_RemoveStaleLock,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
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
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_ConflictMarkerWarningTitle,
            Content = string.Format(
                Strings.VersionControl_ConflictMarkerWarning,
                markerFile),
            CloseButtonText = Strings.Close,
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static async Task ShowPolicyNoticeAsync(
        VersionControlPolicyNotice notice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string message = notice.Kind switch
        {
            VersionControlPolicyNoticeKind.LfsRemoteQuota
                => Strings.VersionControl_LfsQuotaNotice,
            VersionControlPolicyNoticeKind.LargeMediaWithoutLfs
                => string.Format(
                    Strings.VersionControl_LargeMediaWarningFormat,
                    notice.Path ?? string.Empty),
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
        IProjectVersionControlService? service = _currentService;
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
            if (_preserveServiceAcrossClose)
            {
                if (project is null)
                {
                    _editorService.ProjectVersionControlService = null;
                    return;
                }

                string preservedRoot = GetProjectRoot(project);
                if (_currentService?.Repository is { } preservedRepository
                    && string.Equals(
                        preservedRepository.ProjectRoot,
                        preservedRoot,
                        PathComparison))
                {
                    _editorService.ProjectVersionControlService = _currentService;
                    return;
                }
            }

            if (project is null)
            {
                CancelActivation();
                ReplaceService(null);
                return;
            }

            string projectRoot = GetProjectRoot(project);
            CancelActivation();
            var service = new GitCliVersionControlService(
                _installationLocator,
                repository: null,
                () => _projectService.CurrentProject.Value is null,
                PresentPolicyNoticeAsync);
            ReplaceService(service);
            int revision = ++_activationRevision;
            var activationCancellation = new CancellationTokenSource();
            _activationCancellation = activationCancellation;
            _activationTask = ActivateRepositoryAsync(
                service,
                projectRoot,
                revision,
                activationCancellation.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ReplaceService(null);
        }
    }

    private async Task ActivateRepositoryAsync(
        GitCliVersionControlService service,
        string projectRoot,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            GitAvailability availability = await service.GetAvailabilityAsync(cancellationToken);
            if (availability.State != GitAvailabilityState.Installed)
            {
                return;
            }

            RepositoryInfo? repository = await service.DiscoverRepositoryAsync(
                projectRoot,
                cancellationToken);
            if (repository is null)
            {
                return;
            }

            if (repository.IsNestedInForeignRepo
                && !await ConfirmUseEnclosingRepositoryAsync(repository, cancellationToken))
            {
                return;
            }

            if (revision != _activationRevision
                || !ReferenceEquals(_currentService, service)
                || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ReplaceService(new GitCliVersionControlService(
                _installationLocator,
                repository,
                () => _projectService.CurrentProject.Value is null,
                PresentPolicyNoticeAsync));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover version control for the open project.");
        }
    }

    private async Task<RepositoryInfo?> SelectRepositoryForInitializationAsync(
        IProjectVersionControlService service,
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

    private void CancelActivation()
    {
        _activationRevision++;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        _activationTask = Task.CompletedTask;
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
        catch (Exception ex) when (!_disposed)
        {
            _isGitAvailable.Value = false;
            _logger.LogWarning(ex, "Failed to refresh Git availability.");
        }
        catch (Exception) when (_disposed)
        {
        }
    }

    private void ReplaceService(IProjectVersionControlService? service)
    {
        IProjectVersionControlService? previous = _currentService;
        if (previous is IRepositoryLockRecoveryService previousRecovery)
        {
            previousRecovery.RecoverableLockAvailable -= OnRecoverableLockAvailable;
        }

        _currentService = service;
        _isTracked.Value = service?.Repository is not null;
        _editorService.ProjectVersionControlService = service;
        if (service is IRepositoryLockRecoveryService recovery)
        {
            recovery.RecoverableLockAvailable += OnRecoverableLockAvailable;
        }

        try
        {
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose the previous project version control service.");
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
                || !ReferenceEquals(_currentService, sender)
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
