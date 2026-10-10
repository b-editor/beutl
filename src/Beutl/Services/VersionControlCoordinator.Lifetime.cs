using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
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
        PreparedNewProject? preparedNewProject;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingConfigurationActivation = null;
            preparedNewProject = _preparedNewProject;
            _preparedNewProject = null;
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
        if (preparedNewProject?.Service is { } preparedService)
        {
            DiscardNewProjectBackend(preparedService);
        }

        _config.ConfigurationChanged -= OnVersionControlConfigChanged;
        _projectService.Opening -= InspectProjectOpeningAsync;
        _projectService.ClosingPreparing -= PrepareProjectClosingAsync;
        _projectService.ClosingFinalizing -= NotifyProjectClosingAsync;
        _projectService.TransitionCommitted -= OnProjectChanged;
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
            await WaitForQuiescenceAsync(ref _availabilityUsers, ref _availabilityQuiesced).ConfigureAwait(false);
            await WaitForQuiescenceAsync(ref _operationUsers, ref _operationsQuiesced).ConfigureAwait(false);
            await WaitForQuiescenceAsync(ref _closeBarrierUsers, ref _closeBarriersQuiesced).ConfigureAwait(false);
            await WaitForQuiescenceAsync(ref _lifecycleUsers, ref _lifecycleQuiesced).ConfigureAwait(false);
            await WaitForQuiescenceAsync(ref _activationSetupUsers, ref _activationSetupsQuiesced).ConfigureAwait(false);
            ClearProjectState();
            await WaitForQuiescenceAsync(ref _lockRecoveryUsers, ref _lockRecoveryQuiesced).ConfigureAwait(false);
            await WaitForQuiescenceAsync(ref _notificationUsers, ref _notificationsQuiesced).ConfigureAwait(false);
            await FlushPublicationDrainAsync();
            await _propertiesDisposedCompletion.Task.ConfigureAwait(false);
            await WaitForQuiescenceAsync(ref _retirementUsers, ref _retirementsQuiesced).ConfigureAwait(false);
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

    // The counter and its completion source are passed by reference so both are read and created under
    // the state lock, as each per-counter wait did.
    private Task WaitForQuiescenceAsync(ref int users, ref TaskCompletionSource? quiesced)
    {
        lock (_stateGate)
        {
            if (users == 0)
            {
                return Task.CompletedTask;
            }

            return (quiesced ??= CreateCompletionSource()).Task;
        }
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
