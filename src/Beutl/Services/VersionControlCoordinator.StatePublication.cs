using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task FlushPublicationDrainAsync()
    {
        Task? runningDrain;
        lock (_stateGate)
        {
            runningDrain = _publicationDrainRunning
                ? (_publicationDrainQuiesced ??= CreateCompletionSource()).Task
                : null;
        }

        if (runningDrain is not null)
        {
            await runningDrain.ConfigureAwait(false);
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            DrainStatePublications();
        }
        else
        {
            await _dispatcher.InvokeAsync(DrainStatePublications);
        }
    }

    private bool TransitionOwnedServiceLocked(
        IProjectVersionControlBackend? ownedService,
        IProjectVersionControlService? visibleService,
        string? projectRoot,
        ActivationContext? retiringActivation,
        out bool retirementQueued)
    {
        IProjectVersionControlBackend? previous = _state.OwnedService;
        if (ownedService is not null)
        {
            _managedServices.Add(ownedService);
        }

        if (!ReferenceEquals(previous, ownedService))
        {
            if (previous is IRepositoryLockRecoveryService previousRecovery)
            {
                previousRecovery.RecoverableLockAvailable -= OnRecoverableLockAvailable;
            }

            if (ownedService is IRepositoryLockRecoveryService recovery)
            {
                recovery.RecoverableLockAvailable += OnRecoverableLockAvailable;
            }
        }

        ServiceRetirement? retirement = null;
        bool retirementWaitsForActivation = false;
        if (previous is not null && !ReferenceEquals(previous, ownedService))
        {
            retirementWaitsForActivation = retiringActivation?.OwnsService(previous) == true;
            Task activationReady = retirementWaitsForActivation
                ? retiringActivation!.Completion
                : Task.CompletedTask;
            retirement = new ServiceRetirement(previous, activationReady);
        }
        if (retirement is not null && retirementWaitsForActivation)
        {
            retiringActivation!.MarkServiceCleanupDelegated(previous!);
        }

        retirementQueued = retirement is not null;
        return TransitionStateLocked(
            _state with
            {
                ProjectRoot = projectRoot,
                OwnedService = ownedService,
                VisibleService = visibleService,
                IsTracked = visibleService?.Repository is not null,
            },
            retirement);
    }

    private bool TransitionStateLocked(
        CoordinatorState next,
        ServiceRetirement? retirement = null)
    {
        if (retirement is null
            && ReferenceEquals(_state.OwnedService, next.OwnedService)
            && ReferenceEquals(_state.VisibleService, next.VisibleService)
            && NullablePathsEqual(_state.ProjectRoot, next.ProjectRoot)
            && _state.IsGitAvailable == next.IsGitAvailable
            && _state.IsTracked == next.IsTracked)
        {
            return false;
        }

        next = next with { Revision = ++_nextStateRevision };
        _state = next;
        _publicationQueue.Enqueue(new StatePublication(next, retirement));
        if (_publicationDrainScheduled)
        {
            return false;
        }

        _publicationDrainScheduled = true;
        return true;
    }

    private void SchedulePublicationDrain(bool schedulePublication)
    {
        if (!schedulePublication)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            DrainStatePublications();
        }
        else
        {
            _dispatcher.Post(DrainStatePublications);
        }
    }

    private void DrainStatePublications()
    {
        lock (_stateGate)
        {
            if (_publicationDrainRunning)
            {
                return;
            }

            _publicationDrainRunning = true;
        }

        bool disposeProperties = false;
        bool reschedule = false;
        TaskCompletionSource? drainQuiesced = null;
        try
        {
            while (true)
            {
                StatePublication publication;
                lock (_stateGate)
                {
                    if (_publicationQueue.Count == 0)
                    {
                        break;
                    }

                    publication = _publicationQueue.Dequeue();
                }

                if (publication.State.Revision > _lastPublishedRevision)
                {
                    _lastPublishedRevision = publication.State.Revision;
                    if (!_propertiesDisposed)
                    {
                        PublishStateValue(
                            () => _isGitAvailable.Value = publication.State.IsGitAvailable,
                            publication.State.Revision,
                            nameof(IsGitAvailable));
                        PublishStateValue(
                            () => _isTracked.Value = publication.State.IsTracked,
                            publication.State.Revision,
                            nameof(IsTracked));
                        PublishStateValue(
                            () => _editorService.PublishProjectVersionControlService(
                                publication.State.VisibleService),
                            publication.State.Revision,
                            nameof(EditorService.ProjectVersionControlService));
                    }
                }

                if (publication.Retirement is { } retirement)
                {
                    RetireService(retirement);
                }
            }
        }
        finally
        {
            lock (_stateGate)
            {
                _publicationDrainRunning = false;
                if (_publicationQueue.Count == 0)
                {
                    _publicationDrainScheduled = false;
                    drainQuiesced = _publicationDrainQuiesced;
                    _publicationDrainQuiesced = null;
                    if (_disposePropertiesRequested && !_propertiesDisposed)
                    {
                        _propertiesDisposed = true;
                        disposeProperties = true;
                    }
                }
                else
                {
                    _publicationDrainScheduled = true;
                    reschedule = true;
                }
            }

            try
            {
                if (disposeProperties)
                {
                    try
                    {
                        _isGitAvailable.Dispose();
                        _isTracked.Dispose();
                    }
                    finally
                    {
                        _propertiesDisposedCompletion.TrySetResult();
                    }
                }

                if (reschedule)
                {
                    _dispatcher.Post(DrainStatePublications);
                }
            }
            finally
            {
                drainQuiesced?.TrySetResult();
            }
        }
    }

    private void PublishStateValue(Action publish, long revision, string member)
    {
        try
        {
            publish();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A version-control state subscriber failed while publishing {Member} at revision {Revision}.",
                member,
                revision);
        }
    }

    private void DisposePublishedProperties()
    {
        void DisposeOnUiThread()
        {
            bool drain;
            lock (_stateGate)
            {
                if (_propertiesDisposed)
                {
                    return;
                }

                _disposePropertiesRequested = true;
                drain = !_publicationDrainRunning;
                if (drain)
                {
                    _publicationDrainScheduled = true;
                }
            }

            if (drain)
            {
                DrainStatePublications();
            }
        }

        if (_dispatcher.CheckAccess())
        {
            DisposeOnUiThread();
        }
        else
        {
            _dispatcher.Post(DisposeOnUiThread);
        }
    }

    private sealed record CoordinatorState(
        long Revision,
        string? ProjectRoot,
        IProjectVersionControlBackend? OwnedService,
        IProjectVersionControlService? VisibleService,
        bool IsGitAvailable,
        bool IsTracked)
    {
        public static CoordinatorState Empty { get; } = new(
            Revision: 0,
            ProjectRoot: null,
            OwnedService: null,
            VisibleService: null,
            IsGitAvailable: false,
            IsTracked: false);
    }

    private sealed record StatePublication(
        CoordinatorState State,
        ServiceRetirement? Retirement);
}
