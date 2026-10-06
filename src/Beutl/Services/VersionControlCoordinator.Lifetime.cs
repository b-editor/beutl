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
            _pendingOpeningRepositoryDecision = null;
            preparedNewProject = _preparedNewProject;
            _preparedNewProject = null;
            _openingPullRecoveries.Clear();
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
        _projectService.OpeningPreflight -= PrepareProjectOpeningAsync;
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
            await WaitForAvailabilityQuiescenceAsync().ConfigureAwait(false);
            await WaitForOperationQuiescenceAsync().ConfigureAwait(false);
            await WaitForCloseBarrierQuiescenceAsync().ConfigureAwait(false);
            await WaitForLifecycleQuiescenceAsync().ConfigureAwait(false);
            await WaitForActivationSetupQuiescenceAsync().ConfigureAwait(false);
            await WaitForPendingRecoveryOfferQuiescenceAsync().ConfigureAwait(false);
            ClearProjectState();
            await WaitForLockRecoveryQuiescenceAsync().ConfigureAwait(false);
            await WaitForNotificationQuiescenceAsync().ConfigureAwait(false);
            await FlushPublicationDrainAsync();
            await _propertiesDisposedCompletion.Task.ConfigureAwait(false);
            await WaitForRetirementQuiescenceAsync().ConfigureAwait(false);
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

    private Task WaitForAvailabilityQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_availabilityUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_availabilityQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForActivationSetupQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_activationSetupUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_activationSetupsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForLifecycleQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_lifecycleUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_lifecycleQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForCloseBarrierQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_closeBarrierUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_closeBarriersQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForOperationQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_operationUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_operationsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForLockRecoveryQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_lockRecoveryUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_lockRecoveryQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForPendingRecoveryOfferQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_pendingRecoveryOfferUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_pendingRecoveryOffersQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForRetirementQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_retirementUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_retirementsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private Task WaitForNotificationQuiescenceAsync()
    {
        lock (_stateGate)
        {
            if (_notificationUsers == 0)
            {
                return Task.CompletedTask;
            }

            return (_notificationsQuiesced ??= CreateCompletionSource()).Task;
        }
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
