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

    // Waits for the operation gate. The lease's token ends when the caller cancels and, unless the operation
    // is a lifecycle one, when the coordinator is disposed or a project close begins. The lifecycle
    // operations, restore, branch switch and pull, follow only the caller's token: the restore and the
    // switch hold a project transition that keeps any close away, and the pull's token follows the
    // project/service epoch, which a close advances.
    private async Task<OperationLease> BeginOperationAsync(
        CancellationToken cancellationToken,
        bool lifecycle = false)
    {
        ThrowIfOperationUnavailable(lifecycle);
        CancellationTokenSource cancellation = lifecycle
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token,
                _operationEpochCancellation.Token);
        try
        {
            await _operationGate.WaitAsync(cancellation.Token);
        }
        catch
        {
            cancellation.Dispose();
            throw;
        }

        var operation = new OperationLease(this, cancellation);
        try
        {
            // A close or the disposal may have begun while this operation waited.
            ThrowIfOperationUnavailable(lifecycle);
            return operation;
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }

    // A lifecycle operation reports a close in progress as the project changing under it.
    private void ThrowIfOperationUnavailable(bool lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_close is null)
        {
            return;
        }

        throw lifecycle
            ? new VersionControlLifecycleUnavailableException(
                "Lifecycle version-control operations cannot run while the project is closing.")
            : new InvalidOperationException(
                "Version-control operations cannot start while the project is closing.");
    }

    // The branch switch and the restore close and reopen the project, so each takes a version-control
    // transition, then the operation gate, and reserves the workspace; all three are released in the
    // reverse order once the cycle has finished. A close also takes the gate inside its own transition, so
    // the transition always comes first.
    private async Task<bool> RunProjectTransitionCycleAsync(
        Func<ProjectService.ProjectTransitionScope, Task<bool>> cycle,
        CancellationToken cancellationToken)
    {
        ThrowIfOperationUnavailable(lifecycle: true);
        await using ProjectService.ProjectTransitionScope transition =
            await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
        using OperationLease operation = await BeginOperationAsync(
            cancellationToken,
            lifecycle: true);
        using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
        if (worktreeMutation is null)
        {
            return false;
        }

        return await cycle(transition);
    }

    private CancellationTokenSource CreateProjectServiceEpochCancellation(
        CancellationToken cancellationToken)
    {
        ThrowIfOperationUnavailable(lifecycle: true);
        return CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token,
            _projectServiceEpochCancellation.Token);
    }

    private void AdvanceProjectServiceEpoch()
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource previous = _projectServiceEpochCancellation;
        _projectServiceEpochCancellation = new CancellationTokenSource();
        try
        {
            previous.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A project/service epoch cancellation callback failed.");
        }
        finally
        {
            previous.Dispose();
        }
    }

    // Disposal waits until no coordinator work is running. Entry points, the coordinator's background tasks
    // and a project close each count as running work for as long as they last.
    private RunningWork BeginWork()
    {
        _runningWork++;
        return new RunningWork(this);
    }

    private void EndWork()
    {
        if (--_runningWork == 0)
        {
            _workDrained?.TrySetResult();
            _workDrained = null;
        }
    }

    private Task WaitForRunningWorkAsync()
    {
        return _runningWork == 0
            ? Task.CompletedTask
            : (_workDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    private sealed class OperationLease(
        VersionControlCoordinator owner,
        CancellationTokenSource cancellation) : IDisposable
    {
        private bool _released;

        public CancellationToken CancellationToken { get; } = cancellation.Token;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            cancellation.Dispose();
            owner._operationGate.Release();
        }
    }

    private sealed class RunningWork(VersionControlCoordinator owner) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            owner.EndWork();
        }
    }
}
