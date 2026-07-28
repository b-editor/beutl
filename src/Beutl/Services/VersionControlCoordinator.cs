using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed class VersionControlCoordinator : IDisposable
{
    private const string SaveSnapshotMessage = "beutl: snapshot on save";
    private const string CloseSnapshotMessage = "beutl: snapshot on close";

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlConfig _config;
    private readonly GitInstallationLocator _installationLocator;
    private readonly IDisposable _projectSubscription;
    private readonly ILogger _logger = Log.CreateLogger<VersionControlCoordinator>();
    private IProjectVersionControlService? _currentService;
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
        _projectService.Closing += NotifyClosingAsync;
        _projectSubscription = _projectService.ProjectObservable.Subscribe(
            change => OnProjectChanged(change.New));

        if (_projectService.CurrentProject.Value is { } project)
        {
            OnProjectChanged(project);
        }
    }

    public IProjectVersionControlService? CurrentService => _currentService;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _projectService.Closing -= NotifyClosingAsync;
        _projectSubscription.Dispose();
        ReplaceService(null);
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
            if (project is null)
            {
                ReplaceService(null);
                return;
            }

            string projectRoot = GetProjectRoot(project);
            RepositoryInfo? repository = Directory.Exists(Path.Combine(projectRoot, ".git"))
                ? new RepositoryInfo(projectRoot, projectRoot)
                : null;
            ReplaceService(new GitCliVersionControlService(_installationLocator, repository));
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
}
