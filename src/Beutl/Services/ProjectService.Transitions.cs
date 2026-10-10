using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _openAttemptSync = new();
    private readonly object _transitionSync = new();
    private ProjectOpenAttempt? _currentOpenAttempt;
    private ProjectTransitionContext? _currentTransition;
    private long _nextTransitionId;

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

    // Lets a transition already under way finish before the open looks for its project file, which that
    // transition may still be replacing.
    private async Task WaitForExistingTransitionAsync(ProjectOpenAttempt attempt)
    {
        CancelCreationPreparationExcept(attempt);
        await _transitionGate.WaitAsync(attempt.CancellationToken);
        try
        {
            attempt.CancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    internal ValueTask<ProjectTransitionScope> BeginVersionControlTransitionAsync(
        object owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return BeginTransitionAsync(
            ProjectTransitionPurpose.VersionControlMutation,
            owner,
            cancellationToken);
    }

    // Runs a change to project files on disk that no open, close or create may interleave with:
    // one already running finishes first and the next waits for the change. Unlike a transition it
    // leaves a pending open alone, because deleting one project must not cancel opening another; an
    // open of the changed project itself goes on to find the files as the change left them.
    internal async Task RunExclusiveOfTransitionsAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _transitionGate.WaitAsync();
        await using ProjectTransitionScope transition = EnterTransition(
            ProjectTransitionPurpose.Normal,
            new ProjectFileChange());
        await action();
    }

    private async ValueTask<ProjectTransitionScope> BeginTransitionAsync(
        ProjectTransitionPurpose purpose,
        object owner,
        CancellationToken cancellationToken)
    {
        CancelPendingOpenAttemptExcept(owner);
        CancelCreationPreparationExcept(owner);
        await _transitionGate.WaitAsync(cancellationToken);
        CancelPendingOpenAttemptExcept(owner);
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

    private ProjectOpenAttempt BeginOpenAttempt()
    {
        var attempt = new ProjectOpenAttempt();
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
