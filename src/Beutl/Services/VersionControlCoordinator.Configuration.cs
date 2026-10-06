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
                || NullablePathsEqual(executablePath, _observedGitExecutablePath))
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
            && !PathsEqual(stateRoot, request.ProjectRoot))
        {
            _pendingConfigurationActivation = null;
            return null;
        }

        if (IsVersionControlWorkActiveLocked())
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

    private bool IsVersionControlWorkActiveLocked()
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
