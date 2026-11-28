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
    // The sequence of the newest open request that activated its session. It outlives the request, so an older
    // request that reaches its transition later does not take the activation back.
    private long _lastActivatedOpenSequence;
    // A project an older request loaded without activating, because a newer request was under way. It becomes
    // active if that newer request fails.
    private (ProjectSession Session, long Sequence)? _deferredActivation;
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

    // Whether the attempt still owns the activation: no request made after it is still under way, and none made
    // after it has activated its project already.
    private bool IsNewestOpenAttempt(ProjectOpenAttempt attempt)
    {
        lock (_openAttemptSync)
        {
            return attempt.Sequence > _lastActivatedOpenSequence && !HasNewerPendingOpen_NoLock(attempt.Sequence);
        }
    }

    private bool HasNewerPendingOpen_NoLock(long sequence)
        => _pendingOpenAttempts.Any(pending => pending.Sequence > sequence && !pending.IsCancellationRequested);

    // Records how an open request ended once it applied: its session became active, or it only loaded it.
    private void RecordOpenOutcome(ProjectOpenAttempt attempt, ProjectSession? session)
    {
        lock (_openAttemptSync)
        {
            if (session is not null && ReferenceEquals(Sessions.ActiveSession.Value, session))
            {
                _lastActivatedOpenSequence = Math.Max(_lastActivatedOpenSequence, attempt.Sequence);
                if (_deferredActivation is { } deferred && deferred.Sequence < attempt.Sequence)
                    _deferredActivation = null;
            }
            else if (session is not null
                     && attempt.Sequence > _lastActivatedOpenSequence
                     && (_deferredActivation is not { } deferred || deferred.Sequence < attempt.Sequence))
            {
                _deferredActivation = (session, attempt.Sequence);
            }
        }
    }

    // Called when a request failed: a project an older request loaded in the background on its behalf becomes
    // active, unless another request made after that one is still under way or activated its project already.
    private async Task ActivateDeferredSessionAsync()
    {
        (ProjectSession Session, long Sequence) deferred;
        lock (_openAttemptSync)
        {
            if (_deferredActivation is not { } candidate
                || candidate.Sequence <= _lastActivatedOpenSequence
                || HasNewerPendingOpen_NoLock(candidate.Sequence))
            {
                return;
            }

            deferred = candidate;
        }

        await using ProjectTransitionScope transition = await BeginTransitionAsync(
            ProjectTransitionPurpose.Normal,
            this,
            CancellationToken.None,
            cancelPendingOpens: false);
        lock (_openAttemptSync)
        {
            // Decided again under the gate: a request may have taken the activation meanwhile.
            if (_deferredActivation != deferred
                || deferred.Sequence <= _lastActivatedOpenSequence
                || HasNewerPendingOpen_NoLock(deferred.Sequence))
            {
                return;
            }

            _deferredActivation = null;
        }

        ProjectSession session = deferred.Session;
        if (!Sessions.Sessions.Contains(session) || ReferenceEquals(Sessions.ActiveSession.Value, session))
        {
            return;
        }

        ProjectSession? previous = Sessions.ActiveSession.Value;
        try
        {
            transition.Context.TargetSession = session;
            if (previous is not null)
                await DeactivateSessionCoreAsync(previous, transition.Context);

            await ActivateSessionCoreAsync(session, freshlyLoaded: false);
            lock (_openAttemptSync)
            {
                _lastActivatedOpenSequence = Math.Max(_lastActivatedOpenSequence, deferred.Sequence);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to activate a project loaded in the background. Project: {Project}", session.Project.Uri);
            if (previous is not null
                && Sessions.ActiveSession.Value is null
                && Sessions.Sessions.Contains(previous))
            {
                try
                {
                    await ActivateSessionCoreAsync(previous, freshlyLoaded: false);
                }
                catch (Exception recoveryFailure)
                {
                    _logger.LogError(recoveryFailure, "Unable to restore the previous project.");
                }
            }

            _notifications.ShowInformation(Strings.Project, MessageStrings.FailedToOpenProject);
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
            // A transition that cancels the open requests also ends the claim of one loaded in the background.
            _deferredActivation = null;
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
