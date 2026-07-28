using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed class VersionControlCoordinator : IVersionControlRestoreCoordinator, IDisposable
{
    private const string SaveSnapshotMessage = "beutl: snapshot on save";
    private const string CloseSnapshotMessage = "beutl: snapshot on close";
    private const string RestoreSafetySnapshotMessage = "beutl: safety snapshot before restore";

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlConfig _config;
    private readonly GitInstallationLocator _installationLocator;
    private readonly IDisposable _projectSubscription;
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinator>();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IProjectVersionControlService? _currentService;
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
        _projectService.Closing += NotifyClosingAsync;
        _projectSubscription = _projectService.ProjectObservable.Subscribe(
            change => OnProjectChanged(change.New));
        _editorService.VersionControlRestoreCoordinator = this;

        if (_projectService.CurrentProject.Value is { } project)
        {
            OnProjectChanged(project);
        }
    }

    public IProjectVersionControlService? CurrentService => _currentService;

    internal Func<CancellationToken, Task<bool>> ConfirmRestoreAsync { get; set; }

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _installationLocator.LocateAsync(cancellationToken);
    }

    public async Task<bool> InitializeCurrentProjectAsync(
        Func<IProjectVersionControlService, Task<bool>> requestIdentityAsync,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestIdentityAsync);

        Project project = _projectService.CurrentProject.Value
                          ?? throw new InvalidOperationException("No project is open.");
        IProjectVersionControlService service = _currentService
                                                ?? throw new InvalidOperationException(
                                                    "The version control service is not available.");
        string projectRoot = GetProjectRoot(project);
        var options = new InitOptions(projectRoot, _config.UseLfsWhenAvailable);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _projectService.Closing -= NotifyClosingAsync;
        _projectSubscription.Dispose();
        if (ReferenceEquals(_editorService.VersionControlRestoreCoordinator, this))
        {
            _editorService.VersionControlRestoreCoordinator = null;
        }

        if (Volatile.Read(ref _lifecycleUsers) == 0)
        {
            ReplaceService(null);
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

            if (!await ConfirmRestoreAsync(cancellationToken))
            {
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
                    await service.CreateBranchFromAsync(branchName, sha, cancellationToken);
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
                ReplaceService(null);
                return;
            }

            string projectRoot = GetProjectRoot(project);
            RepositoryInfo? repository = Directory.Exists(Path.Combine(projectRoot, ".git"))
                ? new RepositoryInfo(projectRoot, projectRoot)
                : null;
            ReplaceService(new GitCliVersionControlService(
                _installationLocator,
                repository,
                () => _projectService.CurrentProject.Value is null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate version control for the open project.");
            ReplaceService(null);
        }
    }

    private void ReplaceService(IProjectVersionControlService? service)
    {
        IProjectVersionControlService? previous = _currentService;
        _currentService = service;
        _editorService.ProjectVersionControlService = service;
        try
        {
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose the previous project version control service.");
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
