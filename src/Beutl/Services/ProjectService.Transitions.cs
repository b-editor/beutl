using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
    // Serializes every change to the loaded sessions and their files: loading, activating, deactivating and closing
    // a session, version-control mutations and file changes. Waiting for it never cancels or closes another session;
    // only the open requests below are superseded.
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _openAttemptSync = new();
    private readonly object _transitionSync = new();
    // The open requests that have not completed, oldest first. Each belongs to its own project file.
    private readonly List<ProjectOpenAttempt> _pendingOpenAttempts = [];
    private long _openAttemptSequence;
    private ProjectTransitionContext? _currentTransition;

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
    internal async Task RunExclusiveOfTransitionsAsync(Func<Task> action, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _transitionGate.WaitAsync(token);
        await using ProjectTransitionScope transition = EnterTransition(
            ProjectTransitionPurpose.Normal,
            new ProjectFileChange());
        await action();
    }

    // cancelPendingOpens is false for a transition that concerns only a session the editor does not show, such as
    // unloading it, which must not cancel the user's request to open another project.
    private async ValueTask<ProjectTransitionScope> BeginTransitionAsync(
        ProjectTransitionPurpose purpose,
        object owner,
        CancellationToken cancellationToken,
        bool cancelPendingOpens = true)
    {
        CancelPendingOpenAttemptExcept(owner, cancelPendingOpens);
        CancelCreationPreparationExcept(owner);
        await _transitionGate.WaitAsync(cancellationToken);
        CancelPendingOpenAttemptExcept(owner, cancelPendingOpens);
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
        var context = new ProjectTransitionContext(purpose, owner);
        lock (_transitionSync)
        {
            _currentTransition = context;
        }

        return new ProjectTransitionScope(this, context);
    }

    // A newer request for the same file supersedes a pending one. Requests for other files are left alone: each
    // loads its project, and only the newest one activates it.
    private ProjectOpenAttempt BeginOpenAttempt(string file)
    {
        ProjectOpenAttempt attempt;
        ProjectOpenAttempt[] superseded;
        lock (_openAttemptSync)
        {
            attempt = new ProjectOpenAttempt(file, ++_openAttemptSequence);
            superseded = _pendingOpenAttempts.Where(pending => pending.IsFor(file)).ToArray();
            _pendingOpenAttempts.Add(attempt);
        }

        foreach (ProjectOpenAttempt previous in superseded)
        {
            CancelOpenAttempt(previous);
        }

        return attempt;
    }

    // Whether the attempt still owns the activation: no request made after it is still under way.
    private bool IsNewestOpenAttempt(ProjectOpenAttempt attempt)
    {
        lock (_openAttemptSync)
        {
            return !_pendingOpenAttempts.Any(pending =>
                pending.Sequence > attempt.Sequence && !pending.IsCancellationRequested);
        }
    }

    private void CancelPendingOpenAttemptExcept(object? owner, bool cancelPendingOpens)
    {
        if (owner is ProjectOpenAttempt)
        {
            // An open supersedes only an earlier open of its own file, which BeginOpenAttempt already canceled.
            return;
        }

        if (!cancelPendingOpens)
        {
            return;
        }

        ProjectOpenAttempt[] attempts;
        lock (_openAttemptSync)
        {
            attempts = _pendingOpenAttempts.ToArray();
        }

        foreach (ProjectOpenAttempt attempt in attempts)
        {
            if (!ReferenceEquals(attempt, owner))
            {
                CancelOpenAttempt(attempt);
            }
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
            _pendingOpenAttempts.Remove(attempt);
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
