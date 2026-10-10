using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private void TransitionOwnedService(
        IProjectVersionControlBackend? ownedService,
        IProjectVersionControlService? visibleService,
        string? projectRoot,
        ActivationContext? retiringActivation)
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
        if (previous is not null && !ReferenceEquals(previous, ownedService))
        {
            bool retirementWaitsForActivation = retiringActivation?.OwnsService(previous) == true;
            Task activationReady = retirementWaitsForActivation
                ? retiringActivation!.Completion
                : Task.CompletedTask;
            retirement = new ServiceRetirement(previous, activationReady);
            if (retirementWaitsForActivation)
            {
                retiringActivation!.MarkServiceCleanupDelegated(previous);
            }
        }

        SetState(_state with
        {
            ProjectRoot = projectRoot,
            OwnedService = ownedService,
            VisibleService = visibleService,
            IsTracked = visibleService?.Repository is not null,
        });

        // Retired only once the state no longer shows it.
        if (retirement is not null)
        {
            RetireService(retirement);
        }
    }

    // Each value is read from the state as it is published, so when a subscriber changes the state
    // again, the values that end up published are the latest ones rather than those it interrupted.
    private void SetState(CoordinatorState next)
    {
        if (ReferenceEquals(_state.OwnedService, next.OwnedService)
            && ReferenceEquals(_state.VisibleService, next.VisibleService)
            && NullablePathsEqual(_state.ProjectRoot, next.ProjectRoot)
            && _state.IsGitAvailable == next.IsGitAvailable
            && _state.IsTracked == next.IsTracked)
        {
            return;
        }

        _state = next;
        if (_propertiesDisposed)
        {
            return;
        }

        PublishStateValue(
            () => _isGitAvailable.Value = _state.IsGitAvailable,
            nameof(IsGitAvailable));
        PublishStateValue(
            () => _isTracked.Value = _state.IsTracked,
            nameof(IsTracked));
        PublishStateValue(
            () => _editorService.PublishProjectVersionControlService(_state.VisibleService),
            nameof(EditorService.ProjectVersionControlService));
    }

    private void PublishStateValue(Action publish, string member)
    {
        try
        {
            publish();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A version-control state subscriber failed while publishing {Member}.",
                member);
        }
    }

    private void DisposePublishedProperties()
    {
        if (_propertiesDisposed)
        {
            return;
        }

        _propertiesDisposed = true;
        _isGitAvailable.Dispose();
        _isTracked.Dispose();
    }

    private sealed record CoordinatorState(
        string? ProjectRoot,
        IProjectVersionControlBackend? OwnedService,
        IProjectVersionControlService? VisibleService,
        bool IsGitAvailable,
        bool IsTracked)
    {
        public static CoordinatorState Empty { get; } = new(
            ProjectRoot: null,
            OwnedService: null,
            VisibleService: null,
            IsGitAvailable: false,
            IsTracked: false);
    }
}
