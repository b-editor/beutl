using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    // Hands the project to another backend, or to none, and retires the previous one once the state no
    // longer shows it. An activation still using the previous backend keeps it until the activation ends.
    private void TransitionOwnedService(
        IProjectVersionControlBackend? ownedService,
        IProjectVersionControlService? visibleService,
        string? projectRoot,
        ActivationContext? retiringActivation)
    {
        IProjectVersionControlBackend? previous = _state.OwnedService;
        bool replaced = previous is not null && !ReferenceEquals(previous, ownedService);
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

        SetState(_state with
        {
            ProjectRoot = projectRoot,
            OwnedService = ownedService,
            VisibleService = visibleService,
            IsTracked = visibleService?.Repository is not null,
        });
        if (replaced)
        {
            RetireService(
                previous!,
                retiringActivation?.Uses(previous!) == true
                    ? retiringActivation.Completion
                    : Task.CompletedTask);
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
        _propertiesDisposed = true;
        _isGitAvailable.Dispose();
        _isTracked.Dispose();
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

    private void ClearProjectState()
    {
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

    // The activation is the one deciding how the open project is tracked, and nothing has stopped it.
    // The result of any other activation is stale.
    private bool IsCurrentActivation(ActivationContext activation)
    {
        return !_disposed
               && ReferenceEquals(_activation, activation)
               && ReferenceEquals(_state.OwnedService, activation.Service)
               && !activation.CancellationToken.IsCancellationRequested;
    }

    private void CancelActivation(ActivationContext? activation)
    {
        try
        {
            activation?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An activation cancellation callback failed while version control state was transitioning.");
        }
    }

    // Retires a backend that nothing uses any more: neither the state nor the activation in progress.
    private void RetireIfUnused(IProjectVersionControlBackend service)
    {
        if (!ReferenceEquals(_state.OwnedService, service)
            && _activation?.Uses(service) != true)
        {
            RetireService(service, Task.CompletedTask);
        }
    }

    private void RetireService(IProjectVersionControlBackend service, Task usedUntil)
    {
        _ = RetireServiceAsync(service, usedUntil);
    }

    private async Task RetireServiceAsync(IProjectVersionControlBackend service, Task usedUntil)
    {
        using RunningWork work = BeginWork();
        try
        {
            await usedUntil;
            await service.RetireAsync(finalSnapshot: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire a project version-control service.");
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

    // The close has retired the backend with its final snapshot, so it is let go without retiring again.
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
