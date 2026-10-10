using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private void StartDisposalCompletion()
    {
        if (_disposalStarted)
        {
            return;
        }

        _disposalStarted = true;
        _ = CompleteDisposalAsync();
        _ = ObserveDisposalCompletionAsync();
    }

    private async Task ObserveDisposalCompletionAsync()
    {
        try
        {
            await _asyncDisposalCompletion.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete version-control coordinator disposal.");
        }
    }

    private void BeginDisposal()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pendingConfigurationActivation = null;
        PreparedNewProject? preparedNewProject = _preparedNewProject;
        _preparedNewProject = null;
        CancellationTokenSource? configurationActivationCancellation = _configurationActivationCancellation;
        CancellationTokenSource? projectServiceEpochCancellation = _projectServiceEpochCancellation;
        _projectServiceEpochCancellation = null;
        bool clearProjectState = _closeBarrierUsers == 0
                                 && _lifecycleUsers == 0
                                 && _operationUsers == 0;
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
            await WaitForQuiescenceAsync(ref _availabilityUsers, ref _availabilityQuiesced);
            await WaitForQuiescenceAsync(ref _operationUsers, ref _operationsQuiesced);
            await WaitForQuiescenceAsync(ref _closeBarrierUsers, ref _closeBarriersQuiesced);
            await WaitForQuiescenceAsync(ref _lifecycleUsers, ref _lifecycleQuiesced);
            await WaitForQuiescenceAsync(ref _activationSetupUsers, ref _activationSetupsQuiesced);
            ClearProjectState();
            await WaitForQuiescenceAsync(ref _lockRecoveryUsers, ref _lockRecoveryQuiesced);
            await WaitForQuiescenceAsync(ref _retirementUsers, ref _retirementsQuiesced);
            _operationEpochCancellation?.Dispose();
            _operationEpochCancellation = null;
            _lifetimeCancellation.Dispose();
            _asyncDisposalCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            _asyncDisposalCompletion.TrySetException(ex);
        }
    }

    // The counter and its completion source are passed by reference so the wait is created only
    // while work of that kind is still running.
    private static Task WaitForQuiescenceAsync(ref int users, ref TaskCompletionSource? quiesced)
    {
        if (users == 0)
        {
            return Task.CompletedTask;
        }

        return (quiesced ??= CreateCompletionSource()).Task;
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
