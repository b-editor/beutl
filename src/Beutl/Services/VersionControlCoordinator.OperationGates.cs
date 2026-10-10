using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private bool IsInternalVersionControlTransition()
    {
        return IsInternalVersionControlTransition(_projectService.CurrentTransition);
    }

    private bool IsInternalVersionControlTransition(ProjectTransitionContext? transition)
    {
        return transition is
        {
            Purpose: ProjectTransitionPurpose.VersionControlMutation,
            Owner: var owner,
        }
               && ReferenceEquals(owner, this);
    }

    private static bool IsProjectCreationTransition(ProjectTransitionContext? transition)
    {
        return transition is
        {
            Purpose: ProjectTransitionPurpose.Normal,
            Owner: ProjectService.ProjectCreation,
        };
    }

    private void FinishInternalTransition()
    {
        if (_projectService.CurrentProject.Value is null)
        {
            ClearProjectState();
        }
    }

    private async Task BeginLifecycleOperationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            ThrowIfLifecycleOperationUnavailable();
            if (!_configurationActivationActive)
            {
                _lifecycleUsers++;
                return;
            }

            await (_configurationActivationQuiesced ??= CreateCompletionSource()).Task
                .WaitAsync(cancellationToken);
        }
    }

    private void ThrowIfLifecycleOperationUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_operationCloseBarrierActive)
        {
            throw new InvalidOperationException(
                "Lifecycle version-control operations cannot run while the project is closing.");
        }
    }

    private CancellationTokenSource CreateProjectServiceEpochCancellation(
        CancellationToken cancellationToken)
    {
        ThrowIfLifecycleOperationUnavailable();
        CancellationToken projectServiceEpoch =
            (_projectServiceEpochCancellation
             ?? throw new ObjectDisposedException(nameof(VersionControlCoordinator)))
            .Token;
        return CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token,
            projectServiceEpoch);
    }

    private void AdvanceProjectServiceEpoch()
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource? previous = _projectServiceEpochCancellation;
        _projectServiceEpochCancellation = new CancellationTokenSource();
        CancelProjectServiceEpoch(previous);
    }

    private void CancelProjectServiceEpoch(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A project/service epoch cancellation callback failed.");
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async ValueTask<NonTransactionalOperationLease> BeginNonTransactionalOperationAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_operationCloseBarrierActive)
            {
                throw new InvalidOperationException(
                    "Version-control operations cannot start while the project is closing.");
            }

            if (!_configurationActivationActive)
            {
                CancellationToken operationEpochCancellation = (_operationEpochCancellation
                                                                ?? throw new ObjectDisposedException(
                                                                    nameof(VersionControlCoordinator)))
                    .Token;
                _operationUsers++;
                return CreateNonTransactionalOperationLease(
                    cancellationToken,
                    operationEpochCancellation);
            }

            await (_configurationActivationQuiesced ??= CreateCompletionSource()).Task
                .WaitAsync(cancellationToken);
        }
    }

    private NonTransactionalOperationLease? TryBeginNonTransactionalOperation(
        CancellationToken cancellationToken)
    {
        if (_disposed || _operationCloseBarrierActive || _configurationActivationActive)
        {
            return null;
        }

        CancellationToken operationEpochCancellation = (_operationEpochCancellation
                                                        ?? throw new ObjectDisposedException(
                                                            nameof(VersionControlCoordinator)))
            .Token;
        _operationUsers++;
        return CreateNonTransactionalOperationLease(
            cancellationToken,
            operationEpochCancellation);
    }

    // Called once _operationUsers counts the new operation, which the lease releases again.
    private NonTransactionalOperationLease CreateNonTransactionalOperationLease(
        CancellationToken cancellationToken,
        CancellationToken operationEpochCancellation)
    {
        try
        {
            return new NonTransactionalOperationLease(
                this,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellation.Token,
                    operationEpochCancellation));
        }
        catch
        {
            FinishNonTransactionalOperation();
            throw;
        }
    }

    private void FinishNonTransactionalOperation()
    {
        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        _operationUsers--;
        if (_operationUsers == 0)
        {
            quiesced = _operationsQuiesced;
            _operationsQuiesced = null;
            clearProjectState = _disposed
                                && _closeBarrierUsers == 0
                                && _lifecycleUsers == 0;
        }

        try
        {
            if (clearProjectState)
            {
                ClearProjectState();
            }
        }
        finally
        {
            quiesced?.TrySetResult();
            TryStartPendingConfigurationActivation();
        }
    }

    private void FinishLifecycleOperation(bool gateEntered)
    {
        if (gateEntered)
        {
            _lifecycleGate.Release();
        }

        TaskCompletionSource? quiesced = null;
        bool clearProjectState = false;
        _lifecycleUsers--;
        if (_lifecycleUsers == 0 && _disposed)
        {
            clearProjectState = _closeBarrierUsers == 0 && _operationUsers == 0;
            quiesced = _lifecycleQuiesced;
        }

        try
        {
            if (clearProjectState)
            {
                ClearProjectState();
            }
        }
        finally
        {
            quiesced?.TrySetResult();
            TryStartPendingConfigurationActivation();
        }
    }

    // The branch switch and the restore close and reopen the project, so each runs under the lifecycle
    // gate, inside a version-control transition and with the workspace reserved; all three are released
    // in the reverse order once the cycle has finished.
    private async Task<bool> RunProjectTransitionCycleAsync(
        Func<ProjectService.ProjectTransitionScope, Task<bool>> cycle,
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return false;
            }

            return await cycle(transition);
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private sealed class NonTransactionalOperationLease : IDisposable
    {
        private VersionControlCoordinator? _owner;
        private readonly CancellationTokenSource _cancellation;

        public NonTransactionalOperationLease(
            VersionControlCoordinator owner,
            CancellationTokenSource cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
        }

        public CancellationToken CancellationToken => _cancellation.Token;

        public void Dispose()
        {
            VersionControlCoordinator? owner = _owner;
            if (owner is null)
            {
                return;
            }

            _owner = null;
            _cancellation.Dispose();
            owner.FinishNonTransactionalOperation();
        }
    }
}
