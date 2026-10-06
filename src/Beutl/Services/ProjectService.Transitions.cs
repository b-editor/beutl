using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _openAttemptSync = new();
    private readonly object _transitionSync = new();
    private readonly object _openPreflightSync = new();
    private ProjectOpenAttempt? _currentOpenAttempt;
    private ProjectTransitionContext? _currentTransition;
    private TaskCompletionSource? _openPreflightsDrained;
    private long _nextOpenAttemptId;
    private long _nextTransitionId;
    private int _runningOpenPreflights;

    internal ProjectTransitionContext? CurrentTransition
    {
        get
        {
            lock (_transitionSync)
            {
                return _currentTransition;
            }
        }
    }

    private async Task WaitForExistingTransitionAsync(ProjectOpenAttempt attempt)
    {
        CancelCreationPreparationExcept(attempt);
        await _transitionGate.WaitAsync(attempt.CancellationToken);
        try
        {
            attempt.CancellationToken.ThrowIfCancellationRequested();
            // Counted under the gate, so RunExclusiveOfTransitionsAsync cannot hold the gate for its
            // change while this open's preflight is still to run.
            BeginOpenPreflight();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private void BeginOpenPreflight()
    {
        lock (_openPreflightSync)
        {
            _runningOpenPreflights++;
        }
    }

    private void EndOpenPreflight()
    {
        TaskCompletionSource? drained = null;
        lock (_openPreflightSync)
        {
            if (--_runningOpenPreflights == 0)
            {
                drained = _openPreflightsDrained;
                _openPreflightsDrained = null;
            }
        }

        drained?.TrySetResult();
    }

    private Task WaitForOpenPreflightsAsync()
    {
        lock (_openPreflightSync)
        {
            if (_runningOpenPreflights == 0)
            {
                return Task.CompletedTask;
            }

            _openPreflightsDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _openPreflightsDrained.Task;
        }
    }

    private bool HasRunningOpenPreflight()
    {
        lock (_openPreflightSync)
        {
            return _runningOpenPreflights > 0;
        }
    }

    internal ValueTask<ProjectTransitionScope> BeginVersionControlTransitionAsync(
        object owner,
        CancellationToken cancellationToken,
        ProjectOpenAttempt? preservedOpenAttempt = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return BeginTransitionAsync(
            ProjectTransitionPurpose.VersionControlMutation,
            owner,
            cancellationToken,
            preservedOpenAttempt);
    }

    // Runs a change to project files on disk that no open, close or create may interleave with:
    // one already running finishes first and the next waits for the change. Unlike a transition it
    // leaves a pending open alone, because deleting one project must not cancel opening another; an
    // open of the changed project itself goes on to find the files as the change left them.
    internal async Task RunExclusiveOfTransitionsAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        while (true)
        {
            // An open inspects its project between two waits for the gate, and the inspection can
            // take the gate itself, so it is waited for without holding the gate. Once the gate is
            // held and none is running, none can start until the change is done.
            await WaitForOpenPreflightsAsync();
            await _transitionGate.WaitAsync();
            if (!HasRunningOpenPreflight())
            {
                break;
            }

            _transitionGate.Release();
        }

        await using ProjectTransitionScope transition = EnterTransition(
            ProjectTransitionPurpose.Normal,
            new ProjectFileChange());
        await action();
    }

    private async ValueTask<ProjectTransitionScope> BeginTransitionAsync(
        ProjectTransitionPurpose purpose,
        object owner,
        CancellationToken cancellationToken,
        ProjectOpenAttempt? preservedOpenAttempt = null)
    {
        object openAttemptOwner = preservedOpenAttempt ?? owner;
        CancelPendingOpenAttemptExcept(openAttemptOwner);
        CancelCreationPreparationExcept(owner);
        await _transitionGate.WaitAsync(cancellationToken);
        CancelPendingOpenAttemptExcept(openAttemptOwner);
        if (cancellationToken.IsCancellationRequested)
        {
            _transitionGate.Release();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return EnterTransition(purpose, owner);
    }

    // Must be called while holding _transitionGate; disposing the scope releases it.
    private ProjectTransitionScope EnterTransition(ProjectTransitionPurpose purpose, object owner)
    {
        var context = new ProjectTransitionContext(
            Interlocked.Increment(ref _nextTransitionId),
            purpose,
            owner);
        lock (_transitionSync)
        {
            _currentTransition = context;
        }

        return new ProjectTransitionScope(this, context);
    }

    private ProjectOpenAttempt BeginOpenAttempt(string file)
    {
        var attempt = new ProjectOpenAttempt(
            Interlocked.Increment(ref _nextOpenAttemptId),
            file);
        ProjectOpenAttempt? previous;
        lock (_openAttemptSync)
        {
            previous = _currentOpenAttempt;
            _currentOpenAttempt = attempt;
        }

        CancelOpenAttempt(previous);
        return attempt;
    }

    private void CancelPendingOpenAttemptExcept(object? owner)
    {
        ProjectOpenAttempt? attempt;
        lock (_openAttemptSync)
        {
            attempt = _currentOpenAttempt;
        }

        if (owner is ProjectOpenAttempt openingAttempt)
        {
            if (!ReferenceEquals(attempt, openingAttempt))
            {
                CancelOpenAttempt(openingAttempt);
            }

            return;
        }

        if (!ReferenceEquals(attempt, owner))
        {
            CancelOpenAttempt(attempt);
        }
    }

    // A creation that still prepares its project, such as recording the first version, stops waiting for
    // Git or the user once another transition is requested, as the close of an open project would stop it.
    private void CancelCreationPreparationExcept(object owner)
    {
        ProjectCreation? creation;
        lock (_transitionSync)
        {
            creation = _currentTransition?.Owner as ProjectCreation;
        }

        if (creation is null || ReferenceEquals(creation, owner))
        {
            return;
        }

        try
        {
            creation.CancelPreparation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A project-creation cancellation callback failed.");
        }
    }

    private void CancelOpenAttempt(ProjectOpenAttempt? attempt)
    {
        try
        {
            attempt?.CancelIfPending();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A project-open cancellation callback failed.");
        }
    }

    private void CompleteOpenAttempt(ProjectOpenAttempt attempt)
    {
        lock (_openAttemptSync)
        {
            if (ReferenceEquals(_currentOpenAttempt, attempt))
            {
                _currentOpenAttempt = null;
            }
        }

        attempt.Complete();
    }

    private void VerifyTransition(ProjectTransitionContext transition)
    {
        lock (_transitionSync)
        {
            if (!ReferenceEquals(_currentTransition, transition))
            {
                throw new InvalidOperationException("The project transition is no longer active.");
            }
        }
    }

    private void EndTransition(ProjectTransitionContext transition)
    {
        lock (_transitionSync)
        {
            if (!ReferenceEquals(_currentTransition, transition))
            {
                return;
            }

            _currentTransition = null;
        }

        _transitionGate.Release();
    }
}
