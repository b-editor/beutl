using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private bool BeginActivation(
        ActivationContext activation,
        out bool cleanupRejectedService)
    {
        bool rejected = _disposed
                        || activation.Revision != _latestActivationRevision
                        || activation.CancellationToken.IsCancellationRequested
                        || !CanAdoptService(activation.Service);
        if (rejected)
        {
            cleanupRejectedService = TryClaimRejectedServiceCleanup(activation.Service);
            return false;
        }

        ActivationContext? previousActivation = _activation;
        LinkServiceUsers(activation, activation.Service);
        _activation = activation;
        cleanupRejectedService = false;
        bool waitsForPredecessors = !activation.PredecessorsCompleted.IsCompletedSuccessfully;
        TransitionOwnedService(
            activation.Service,
            !waitsForPredecessors
                ? activation.Service
                : null,
            activation.ProjectRoot,
            previousActivation);
        CancelActivation(previousActivation);
        return true;
    }

    private bool TryPublishActivationServiceIfCurrent(ActivationContext activation)
    {
        if (!IsCurrentActivation(activation))
        {
            return false;
        }

        if (!activation.HasPredecessors || activation.Service.Repository is null)
        {
            SetState(_state with
            {
                VisibleService = activation.Service,
                IsTracked = activation.Service.Repository is not null,
            });
        }

        return true;
    }

    private bool TryRegisterCandidateService(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        if (!IsCurrentActivation(activation) || !CanAdoptService(service))
        {
            if (IsServiceOwnedOrClaimed(service))
            {
                activation.MarkServiceCleanupDelegated(service);
            }

            return false;
        }

        LinkServiceUsers(activation, service);
        if (!_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
        {
            users = [];
            _candidateServiceUsers.Add(service, users);
        }

        users.Add(activation);
        return true;
    }

    private void LinkServiceUsers(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        var predecessors = new HashSet<ActivationContext>();
        if (_activation is { } current
            && !ReferenceEquals(current, activation)
            && current.Revision < activation.Revision
            && current.OwnsService(service))
        {
            predecessors.Add(current);
        }

        if (_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
        {
            foreach (ActivationContext user in users)
            {
                if (!ReferenceEquals(user, activation)
                    && user.Revision < activation.Revision)
                {
                    predecessors.Add(user);
                }
            }
        }

        foreach (ActivationContext predecessor in predecessors)
        {
            activation.AddCompletionDependency(predecessor.Completion);
            predecessor.MarkServiceCleanupDelegated(service);
        }
    }

    private bool CanAdoptService(IProjectVersionControlBackend service)
    {
        return !_managedServices.Contains(service)
               || ReferenceEquals(_state.OwnedService, service);
    }

    private bool IsServiceOwnedOrClaimed(IProjectVersionControlBackend service)
    {
        return ReferenceEquals(_state.OwnedService, service)
               || _managedServices.Contains(service)
               || _candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users)
               && users.Count > 0;
    }

    private bool TryClaimRejectedServiceCleanup(IProjectVersionControlBackend service)
    {
        return !IsServiceOwnedOrClaimed(service) && _managedServices.Add(service);
    }

    private bool CompleteActivation(
        ActivationContext activation,
        IProjectVersionControlBackend trackedService)
    {
        if (!IsCurrentActivation(activation) || !CanAdoptService(trackedService))
        {
            return false;
        }

        TransitionOwnedService(
            trackedService,
            trackedService,
            activation.ProjectRoot,
            activation);
        activation.TransferOwnership(trackedService);
        return true;
    }

    private bool IsCurrentActivation(ActivationContext activation)
    {
        return !_disposed
               && ReferenceEquals(_activation, activation)
               && activation.Revision == _latestActivationRevision
               && ReferenceEquals(_state.OwnedService, activation.Service)
               && _state.ProjectRoot is { } projectRoot
               && PathsEqual(projectRoot, activation.ProjectRoot)
               && !activation.CancellationToken.IsCancellationRequested;
    }

    private void ClearProjectState(long? expectedActivationRevision = null)
    {
        if (expectedActivationRevision is { } expected
            && expected != _latestActivationRevision)
        {
            return;
        }

        ActivationContext? activation = _activation;
        _activation = null;
        _repositoryHygieneConfigurationDirty = false;
        TransitionOwnedService(
            ownedService: null,
            visibleService: null,
            projectRoot: null,
            activation);
        CancelActivation(activation);
    }

    private void CancelActivation(ActivationContext? activation)
    {
        if (activation is null)
        {
            return;
        }

        try
        {
            activation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An activation cancellation callback failed while version control state was transitioning.");
        }
    }

    private void SetVisibleService(IProjectVersionControlService? service)
    {
        if (service is not null && !ReferenceEquals(service, _state.OwnedService))
        {
            return;
        }

        SetState(_state with
        {
            VisibleService = service,
            IsTracked = service?.Repository is not null,
        });
    }

    private void RetireService(ServiceRetirement retirement)
    {
        _retirementUsers++;
        _ = RetireServiceAsync(retirement);
    }

    private async Task RetireServiceAsync(ServiceRetirement retirement)
    {
        try
        {
            await retirement.ActivationReady;
            await retirement.Service.RetireAsync(finalSnapshot: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a project version-control service.");
        }
        finally
        {
            DisposeService(retirement.Service);
            _retirementUsers--;
            if (_retirementUsers == 0 && _disposed)
            {
                _retirementsQuiesced?.TrySetResult();
            }
        }
    }

    private void UnregisterCandidateService(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        if (_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
        {
            users.Remove(activation);
            if (users.Count == 0)
            {
                _candidateServiceUsers.Remove(service);
            }
        }
    }

    private async Task RetireDiscardedServiceAsync(
        ActivationContext activation,
        IProjectVersionControlBackend service,
        bool cleanupAlreadyClaimed = false)
    {
        UnregisterCandidateService(activation, service);
        bool cleanupService = cleanupAlreadyClaimed
                              || !activation.IsServiceCleanupDelegated(service)
                              && !IsServiceOwnedOrClaimed(service)
                              && _managedServices.Add(service);
        activation.MarkServiceCleanupDelegated(service);
        if (!cleanupService)
        {
            return;
        }

        try
        {
            await service.RetireAsync(finalSnapshot: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a discarded project version-control service.");
        }
        finally
        {
            DisposeService(service);
        }
    }

    private void DisposeService(IProjectVersionControlBackend? service)
    {
        try
        {
            service?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose a project version control service.");
        }
    }

    private void DetachRetiredService(IProjectVersionControlBackend service)
    {
        if (!ReferenceEquals(_state.OwnedService, service))
        {
            return;
        }

        if (service is IRepositoryLockRecoveryService recovery)
        {
            recovery.RecoverableLockAvailable -= OnRecoverableLockAvailable;
        }

        SetState(_state with
        {
            OwnedService = null,
            VisibleService = null,
            IsTracked = false,
        });
        DisposeService(service);
    }

    private sealed record ServiceRetirement(
        IProjectVersionControlBackend Service,
        Task ActivationReady);
}
