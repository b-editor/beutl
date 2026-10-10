using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<GitAvailability> GetAvailabilityCoreAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        // A probe that a newer one overtook leaves IsGitAvailable to the newer one.
        object probe = new();
        _latestAvailabilityProbe = probe;
        GitAvailability availability = await _installationLocator.LocateAsync(
            linkedCancellation.Token);
        linkedCancellation.Token.ThrowIfCancellationRequested();
        if (!_disposed && ReferenceEquals(_latestAvailabilityProbe, probe))
        {
            SetState(_state with
            {
                IsGitAvailable = availability.State == GitAvailabilityState.Installed,
            });
        }

        return availability;
    }

    private void StartAvailabilityRefresh()
    {
        if (!_disposed)
        {
            _ = RefreshAvailabilityAsync();
        }
    }

    private async Task RefreshAvailabilityAsync()
    {
        using RunningWork work = BeginWork();
        try
        {
            await GetAvailabilityCoreAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetState(_state with { IsGitAvailable = false });
            }

            _logger.LogWarning(ex, "Failed to refresh Git availability.");
        }
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => OnVersionControlConfigChanged(sender, e), DispatcherPriority.Normal);
            return;
        }

        if (_disposed)
        {
            return;
        }

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
                useLfsWhenAvailable,
                rediscoverUnassociatedBackend: executablePathChanged,
                reapplyTrackedRepositoryHygiene: useLfsWhenAvailableChanged);
        }

        StartAvailabilityRefresh();
    }

    private bool TryCaptureGitExecutablePathChange(out string? executablePath)
    {
        executablePath = NormalizeGitExecutablePath(_config.GitExecutablePath);
        if (NullablePathsEqual(executablePath, _observedGitExecutablePath))
        {
            return false;
        }

        _observedGitExecutablePath = executablePath;
        return true;
    }

    private bool TryCaptureUseLfsWhenAvailableChange(out bool useLfsWhenAvailable)
    {
        useLfsWhenAvailable = _config.UseLfsWhenAvailable;
        if (useLfsWhenAvailable == _observedUseLfsWhenAvailable)
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

    // A version-control operation that closed and reopened the project kept its backend, so an LFS setting
    // that changed meanwhile is applied to that backend now.
    private void QueueRepositoryHygieneConfigurationIfDirty(Project project)
    {
        if (_repositoryHygieneConfigurationDirty)
        {
            QueueConfigurationActivation(
                project,
                _observedUseLfsWhenAvailable,
                rediscoverUnassociatedBackend: false,
                reapplyTrackedRepositoryHygiene: true);
        }
    }

    // A changed Git executable rediscovers the repository of a project that is not tracked yet, and a
    // changed LFS setting applies the repository hygiene to a tracked one. Changes that arrive before the
    // previous one ran are merged into it.
    private void QueueConfigurationActivation(
        Project project,
        bool useLfsWhenAvailable,
        bool rediscoverUnassociatedBackend,
        bool reapplyTrackedRepositoryHygiene)
    {
        string projectRoot = GetProjectRoot(project);
        if (_disposed || !ReferenceEquals(_projectService.CurrentProject.Value, project))
        {
            return;
        }

        if (_state.ProjectRoot is { } stateRoot
            && PathsEqual(stateRoot, projectRoot)
            && _state.OwnedService?.Repository is not null
            && !reapplyTrackedRepositoryHygiene)
        {
            return;
        }

        ConfigurationActivationRequest? pending = _pendingConfigurationActivation;
        _pendingConfigurationActivation = new ConfigurationActivationRequest(
            project,
            projectRoot,
            useLfsWhenAvailable,
            rediscoverUnassociatedBackend
            || pending?.RediscoverUnassociatedBackend == true,
            reapplyTrackedRepositoryHygiene
            || pending?.ReapplyTrackedRepositoryHygiene == true);
        // A rediscovery for an executable that changed again starts over with the newer one.
        if (rediscoverUnassociatedBackend && _activation is { IsRediscovery: true } rediscovery)
        {
            CancelActivation(rediscovery);
        }

        StartConfigurationActivation();
    }

    private void StartConfigurationActivation()
    {
        if (_configurationActivationRunning
            || _disposed
            || _pendingConfigurationActivation is null)
        {
            return;
        }

        _configurationActivationRunning = true;
        _ = RunConfigurationActivationsAsync();
    }

    // Applies the queued changes one after another under the operation gate, so no operation runs between
    // them, after any activation in progress has decided how the project is tracked.
    private async Task RunConfigurationActivationsAsync()
    {
        using RunningWork work = BeginWork();
        try
        {
            while (_activation is { } activation)
            {
                await activation.Completion.WaitAsync(_lifetimeCancellation.Token);
            }

            using OperationLease operation = await BeginOperationAsync(CancellationToken.None);
            while (true)
            {
                while (_activation is { } activation)
                {
                    await activation.Completion.WaitAsync(operation.CancellationToken);
                }

                if (TakePendingConfigurationActivation() is not { } start)
                {
                    break;
                }

                await RunConfigurationActivationAsync(start, operation.CancellationToken);
            }
        }
        catch (Exception ex)
            when (ex is OperationCanceledException or VersionControlLifecycleUnavailableException
                  || ex is ObjectDisposedException && _disposed)
        {
            // A close or the disposal stopped the changes. A close that leaves the project open starts
            // them again.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply a version-control configuration change.");
        }
        finally
        {
            _configurationActivationRunning = false;
        }
    }

    private async Task RunConfigurationActivationAsync(
        ConfigurationActivationStart start,
        CancellationToken cancellationToken)
    {
        ConfigurationActivationRequest request = start.Request;
        try
        {
            if (start.TrackedService is { } trackedService)
            {
                await trackedService.EnsureRepositoryHygieneAsync(cancellationToken);
                if (ReferenceEquals(_state.OwnedService, trackedService)
                    && trackedService.Repository is not null
                    && request.UseLfsWhenAvailable == _observedUseLfsWhenAvailable)
                {
                    _repositoryHygieneConfigurationDirty = false;
                }
            }
            else if (ActivateProject(
                         request.Project,
                         newProject: false,
                         preparedNewProject: null,
                         cancellationToken,
                         isRediscovery: true) is { } activation)
            {
                // A newer executable cancels this activation and leaves its change queued instead.
                await activation.Completion;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tried again if a close stopped it and then left the project open.
            _pendingConfigurationActivation ??= request;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply a version-control configuration change.");
        }
    }

    private ConfigurationActivationStart? TakePendingConfigurationActivation()
    {
        while (_pendingConfigurationActivation is { } request)
        {
            _pendingConfigurationActivation = null;
            if (_disposed
                || !ReferenceEquals(_projectService.CurrentProject.Value, request.Project)
                || _state.ProjectRoot is { } stateRoot && !PathsEqual(stateRoot, request.ProjectRoot))
            {
                continue;
            }

            if (_state.ProjectRoot is not null && _state.OwnedService?.Repository is not null)
            {
                if (request.ReapplyTrackedRepositoryHygiene)
                {
                    return new ConfigurationActivationStart(request, _state.OwnedService);
                }
            }
            else if (request.RediscoverUnassociatedBackend)
            {
                return new ConfigurationActivationStart(request, TrackedService: null);
            }
        }

        return null;
    }

    private static string? NormalizeGitExecutablePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path;

    private sealed record ConfigurationActivationRequest(
        Project Project,
        string ProjectRoot,
        bool UseLfsWhenAvailable,
        bool RediscoverUnassociatedBackend,
        bool ReapplyTrackedRepositoryHygiene);

    private sealed record ConfigurationActivationStart(
        ConfigurationActivationRequest Request,
        IProjectVersionControlBackend? TrackedService);
}
