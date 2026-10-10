using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<GitAvailability> GetAvailabilityTrackedAsync(
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        try
        {
            int revision = ++_availabilityRevision;
            GitAvailability availability = await _installationLocator.LocateAsync(
                linkedCancellation.Token);
            linkedCancellation.Token.ThrowIfCancellationRequested();
            if (!_disposed && revision == _availabilityRevision)
            {
                SetState(_state with
                {
                    IsGitAvailable = availability.State == GitAvailabilityState.Installed,
                });
            }

            return availability;
        }
        finally
        {
            FinishAvailabilityOperation();
        }
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => OnVersionControlConfigChanged(sender, e), DispatcherPriority.Normal);
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
        if (_disposed
            || NullablePathsEqual(executablePath, _observedGitExecutablePath))
        {
            return false;
        }

        _observedGitExecutablePath = executablePath;
        return true;
    }

    private bool TryCaptureUseLfsWhenAvailableChange(out bool useLfsWhenAvailable)
    {
        useLfsWhenAvailable = _config.UseLfsWhenAvailable;
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

    private void QueueRepositoryHygieneConfigurationIfDirty(Project project)
    {
        if (_disposed
            || !_repositoryHygieneConfigurationDirty
            || !ReferenceEquals(_projectService.CurrentProject.Value, project))
        {
            return;
        }

        QueueConfigurationActivation(
            project,
            _observedGitExecutablePath,
            _observedUseLfsWhenAvailable,
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
            CancelConfigurationActivation(_configurationActivationCancellation);
        }

        TryStartPendingConfigurationActivation();
    }

    private void TryStartPendingConfigurationActivation()
    {
        StartConfigurationActivation(TryPreparePendingConfigurationActivation());
    }

    private ConfigurationActivationStart? TryPreparePendingConfigurationActivation()
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
            && !PathsEqual(stateRoot, request.ProjectRoot))
        {
            _pendingConfigurationActivation = null;
            return null;
        }

        if (IsVersionControlWorkActive())
        {
            return null;
        }

        IProjectVersionControlBackend? trackedService = null;
        if (_state.ProjectRoot is { } trackedRoot
            && PathsEqual(trackedRoot, request.ProjectRoot)
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

    private bool IsVersionControlWorkActive()
    {
        return _operationCloseBarrierActive
               || _closeBarrierUsers != 0
               || _operationUsers != 0
               || _lifecycleUsers != 0
               || _activationSetupUsers != 0
               || _activation is not null;
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
                await trackedService.EnsureRepositoryHygieneAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (ReferenceEquals(_state.OwnedService, trackedService)
                    && trackedService.Repository is not null
                    && request.UseLfsWhenAvailable == _observedUseLfsWhenAvailable)
                {
                    _repositoryHygieneConfigurationDirty = false;
                }
            }
            else
            {
                ActivationContext? activation = await StartProjectActivationAsync(
                    request.Project,
                    internalTransition: false,
                    cancellation.Token);
                if (activation is not null)
                {
                    await activation.Completion;
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
        TaskCompletionSource? operationsQuiesced = null;
        if (ReferenceEquals(_configurationActivationCancellation, cancellation))
        {
            _configurationActivationCancellation = null;
        }

        _configurationActivationActive = false;
        TaskCompletionSource? configurationActivationQuiesced = _configurationActivationQuiesced;
        _configurationActivationQuiesced = null;
        _operationUsers--;

        bool retryTargetStillCurrent = activationStart.TrackedService is { } trackedService
            ? request.ReapplyTrackedRepositoryHygiene
              && request.UseLfsWhenAvailable == _observedUseLfsWhenAvailable
              && ReferenceEquals(_state.OwnedService, trackedService)
              && trackedService.Repository is not null
            : request.RediscoverUnassociatedBackend
              && NullablePathsEqual(
                  _observedGitExecutablePath,
                  request.ExecutablePath)
              && _state.OwnedService?.Repository is null;
        if (retry
            && !_disposed
            && request.Revision == _nextConfigurationActivationRevision
            && _pendingConfigurationActivation is null
            && ReferenceEquals(_projectService.CurrentProject.Value, request.Project)
            && (_state.ProjectRoot is null
                || PathsEqual(_state.ProjectRoot, request.ProjectRoot))
            && retryTargetStillCurrent)
        {
            _pendingConfigurationActivation = request;
        }

        ConfigurationActivationStart? nextActivation = TryPreparePendingConfigurationActivation();
        if (_operationUsers == 0)
        {
            operationsQuiesced = _operationsQuiesced;
            _operationsQuiesced = null;
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
        if (_disposed)
        {
            return;
        }

        _availabilityUsers++;
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
            if (!_disposed)
            {
                SetState(_state with { IsGitAvailable = false });
            }

            _logger.LogWarning(ex, "Failed to refresh Git availability.");
        }
        finally
        {
            FinishAvailabilityOperation();
        }
    }

    private void FinishAvailabilityOperation()
    {
        _availabilityUsers--;
        if (_availabilityUsers == 0 && _disposed)
        {
            _availabilityQuiesced?.TrySetResult();
        }
    }

    private static string? NormalizeGitExecutablePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path;

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
}
