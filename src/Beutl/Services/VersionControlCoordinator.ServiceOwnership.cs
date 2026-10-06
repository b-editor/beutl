using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private bool BeginActivation(
        ActivationContext activation,
        out bool cleanupRejectedService)
    {
        ActivationContext? previousActivation;
        bool schedulePublication = false;
        bool waitsForPredecessors = false;
        bool rejected;
        lock (_stateGate)
        {
            rejected = _disposed
                       || activation.Revision != Volatile.Read(ref _latestActivationRevision)
                       || activation.CancellationToken.IsCancellationRequested
                       || !CanAdoptServiceLocked(activation.Service);
            if (rejected)
            {
                previousActivation = null;
                cleanupRejectedService = TryClaimRejectedServiceCleanupLocked(
                    activation.Service);
            }
            else
            {
                previousActivation = _activation;
                LinkServiceUsersLocked(activation, activation.Service);
                _activation = activation;
                cleanupRejectedService = false;
                waitsForPredecessors =
                    !activation.PredecessorsCompleted.IsCompletedSuccessfully;
                schedulePublication = TransitionOwnedServiceLocked(
                    activation.Service,
                    !waitsForPredecessors
                        ? activation.Service
                        : null,
                    activation.ProjectRoot,
                    previousActivation,
                    out _);
            }
        }

        if (rejected)
        {
            return false;
        }

        CancelPendingPullRecoveryOffer();
        SchedulePublicationDrain(schedulePublication);
        CancelActivation(previousActivation);

        return true;
    }

    private bool TryPublishActivationServiceIfCurrent(ActivationContext activation)
    {
        bool schedulePublication = false;
        bool accepted;
        lock (_stateGate)
        {
            accepted = IsCurrentActivationLocked(activation);
            if (accepted
                && (!activation.HasPredecessors
                    || activation.Service.Repository is null))
            {
                schedulePublication = TransitionStateLocked(
                    _state with
                    {
                        VisibleService = activation.Service,
                        IsTracked = activation.Service.Repository is not null,
                    });
            }
        }

        SchedulePublicationDrain(schedulePublication);
        return accepted;
    }

    private bool TryRegisterCandidateService(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        lock (_stateGate)
        {
            if (!IsCurrentActivationLocked(activation) || !CanAdoptServiceLocked(service))
            {
                if (IsServiceOwnedOrClaimedLocked(service))
                {
                    activation.MarkServiceCleanupDelegated(service);
                }

                return false;
            }

            LinkServiceUsersLocked(activation, service);
            if (!_candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users))
            {
                users = [];
                _candidateServiceUsers.Add(service, users);
            }

            users.Add(activation);
            return true;
        }
    }

    private void LinkServiceUsersLocked(
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

    private bool CanAdoptServiceLocked(IProjectVersionControlBackend service)
    {
        return !_managedServices.Contains(service)
               || ReferenceEquals(_state.OwnedService, service);
    }

    private bool IsServiceOwnedOrClaimedLocked(IProjectVersionControlBackend service)
    {
        return ReferenceEquals(_state.OwnedService, service)
               || _managedServices.Contains(service)
               || _candidateServiceUsers.TryGetValue(service, out HashSet<ActivationContext>? users)
               && users.Count > 0;
    }

    private bool TryClaimRejectedServiceCleanupLocked(
        IProjectVersionControlBackend service)
    {
        return !IsServiceOwnedOrClaimedLocked(service) && _managedServices.Add(service);
    }

    private bool CompleteActivation(
        ActivationContext activation,
        IProjectVersionControlBackend trackedService)
    {
        bool accepted;
        bool schedulePublication = false;
        lock (_stateGate)
        {
            accepted = IsCurrentActivationLocked(activation)
                       && CanAdoptServiceLocked(trackedService);
            if (accepted)
            {
                schedulePublication = TransitionOwnedServiceLocked(
                    trackedService,
                    trackedService,
                    activation.ProjectRoot,
                    activation,
                    out _);
                activation.TransferOwnership(trackedService);
            }
        }

        if (!accepted)
        {
            return false;
        }

        SchedulePublicationDrain(schedulePublication);
        return true;
    }

    private bool IsCurrentActivation(ActivationContext activation)
    {
        lock (_stateGate)
        {
            return IsCurrentActivationLocked(activation);
        }
    }

    private bool IsCurrentActivationLocked(ActivationContext activation)
    {
        return !_disposed
               && ReferenceEquals(_activation, activation)
               && activation.Revision == Volatile.Read(ref _latestActivationRevision)
               && ReferenceEquals(_state.OwnedService, activation.Service)
               && _state.ProjectRoot is { } projectRoot
               && PathsEqual(projectRoot, activation.ProjectRoot)
               && !activation.CancellationToken.IsCancellationRequested;
    }

    private void ClearProjectState(long? expectedActivationRevision = null)
    {
        ActivationContext? activation;
        bool schedulePublication;
        lock (_stateGate)
        {
            if (expectedActivationRevision is { } expected
                && expected != Volatile.Read(ref _latestActivationRevision))
            {
                return;
            }

            activation = _activation;
            _activation = null;
            _repositoryHygieneConfigurationDirty = false;
            schedulePublication = TransitionOwnedServiceLocked(
                ownedService: null,
                visibleService: null,
                projectRoot: null,
                activation,
                out _);
        }

        CancelPendingPullRecoveryOffer();
        SchedulePublicationDrain(schedulePublication);
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
        bool schedulePublication;
        lock (_stateGate)
        {
            if (service is not null && !ReferenceEquals(service, _state.OwnedService))
            {
                return;
            }

            schedulePublication = TransitionStateLocked(
                _state with
                {
                    VisibleService = service,
                    IsTracked = service?.Repository is not null,
                });
        }

        SchedulePublicationDrain(schedulePublication);
    }

    private void RetireService(ServiceRetirement retirement)
    {
        lock (_stateGate)
        {
            _retirementUsers++;
        }

        _ = RetireServiceAsync(retirement);
    }

    private async Task RetireServiceAsync(ServiceRetirement retirement)
    {
        try
        {
            await retirement.ActivationReady.ConfigureAwait(false);
            await retirement.Service.RetireAsync(finalSnapshot: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a project version-control service.");
        }
        finally
        {
            DisposeService(retirement.Service);
            TaskCompletionSource? quiesced = null;
            lock (_stateGate)
            {
                _retirementUsers--;
                if (_retirementUsers == 0 && _disposed)
                {
                    quiesced = _retirementsQuiesced;
                }
            }

            quiesced?.TrySetResult();
        }
    }

    private void UnregisterCandidateService(
        ActivationContext activation,
        IProjectVersionControlBackend service)
    {
        lock (_stateGate)
        {
            UnregisterCandidateServiceLocked(activation, service);
        }
    }

    private void UnregisterCandidateServiceLocked(
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
        bool cleanupService;
        lock (_stateGate)
        {
            UnregisterCandidateServiceLocked(activation, service);
            cleanupService = cleanupAlreadyClaimed
                             || !activation.IsServiceCleanupDelegated(service)
                             && !IsServiceOwnedOrClaimedLocked(service)
                             && _managedServices.Add(service);
            activation.MarkServiceCleanupDelegated(service);
        }

        if (!cleanupService)
        {
            return;
        }

        try
        {
            await service.RetireAsync(finalSnapshot: null).ConfigureAwait(false);
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
        bool detached = false;
        bool schedulePublication = false;
        lock (_stateGate)
        {
            if (ReferenceEquals(_state.OwnedService, service))
            {
                if (service is IRepositoryLockRecoveryService recovery)
                {
                    recovery.RecoverableLockAvailable -= OnRecoverableLockAvailable;
                }

                detached = true;
                schedulePublication = TransitionStateLocked(
                    _state with
                    {
                        OwnedService = null,
                        VisibleService = null,
                        IsTracked = false,
                    });
            }
        }

        SchedulePublicationDrain(schedulePublication);
        if (detached)
        {
            CancelPendingPullRecoveryOffer();
            DisposeService(service);
        }
    }

    private sealed record ServiceRetirement(
        IProjectVersionControlBackend Service,
        Task ActivationReady);
}
