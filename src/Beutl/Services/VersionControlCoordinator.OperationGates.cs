using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private bool IsInternalVersionControlTransition()
    {
        return _projectService.CurrentTransition is
        {
            Purpose: ProjectTransitionPurpose.VersionControlMutation,
            Owner: var owner,
        }
               && ReferenceEquals(owner, this);
    }

    private bool IsProjectCreationTransition()
    {
        return _projectService.CurrentTransition is
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
            Task? configurationActivation;
            lock (_stateGate)
            {
                ThrowIfLifecycleOperationUnavailableLocked();
                if (_configurationActivationActive)
                {
                    configurationActivation =
                        (_configurationActivationQuiesced ??= CreateCompletionSource()).Task;
                }
                else
                {
                    _lifecycleUsers++;
                    return;
                }
            }

            await configurationActivation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ThrowIfLifecycleOperationUnavailable()
    {
        lock (_stateGate)
        {
            ThrowIfLifecycleOperationUnavailableLocked();
        }
    }

    private void ThrowIfLifecycleOperationUnavailableLocked()
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
        lock (_stateGate)
        {
            ThrowIfLifecycleOperationUnavailableLocked();
            CancellationToken projectServiceEpoch =
                (_projectServiceEpochCancellation
                 ?? throw new ObjectDisposedException(nameof(VersionControlCoordinator)))
                .Token;
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token,
                projectServiceEpoch);
        }
    }

    private void AdvanceProjectServiceEpoch()
    {
        CancellationTokenSource? previous;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _projectServiceEpochCancellation;
            _projectServiceEpochCancellation = new CancellationTokenSource();
        }

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
            Task? configurationActivation;
            CancellationToken operationEpochCancellation = default;
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_operationCloseBarrierActive)
                {
                    throw new InvalidOperationException(
                        "Version-control operations cannot start while the project is closing.");
                }

                if (_configurationActivationActive)
                {
                    configurationActivation =
                        (_configurationActivationQuiesced ??= CreateCompletionSource()).Task;
                }
                else
                {
                    configurationActivation = null;
                    operationEpochCancellation = (_operationEpochCancellation
                                                  ?? throw new ObjectDisposedException(
                                                      nameof(VersionControlCoordinator)))
                        .Token;
                    _operationUsers++;
                }
            }

            if (configurationActivation is not null)
            {
                await configurationActivation.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

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
    }

    private NonTransactionalOperationLease? TryBeginNonTransactionalOperation(
        CancellationToken cancellationToken)
    {
        CancellationToken operationEpochCancellation;
        lock (_stateGate)
        {
            if (_disposed || _operationCloseBarrierActive || _configurationActivationActive)
            {
                return null;
            }

            operationEpochCancellation = (_operationEpochCancellation
                                          ?? throw new ObjectDisposedException(nameof(VersionControlCoordinator)))
                .Token;
            _operationUsers++;
        }

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
        lock (_stateGate)
        {
            _operationUsers--;
            if (_operationUsers == 0)
            {
                quiesced = _operationsQuiesced;
                _operationsQuiesced = null;
                clearProjectState = _disposed
                                    && _closeBarrierUsers == 0
                                    && _lifecycleUsers == 0;
            }
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
        lock (_stateGate)
        {
            _lifecycleUsers--;
            if (_lifecycleUsers == 0 && _disposed)
            {
                clearProjectState = _closeBarrierUsers == 0 && _operationUsers == 0;
                quiesced = _lifecycleQuiesced;
            }
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
            VersionControlCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            _cancellation.Dispose();
            owner.FinishNonTransactionalOperation();
        }
    }
}
