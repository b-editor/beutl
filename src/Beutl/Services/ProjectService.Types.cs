using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
    internal enum ProjectCloseIntent
    {
        SaveChanges,
        DiscardChanges,
    }

    // What the close chain does to its session once every handler has run.
    internal enum ProjectCloseKind
    {
        // The session stays loaded with its edits, tabs and history, and the editor stops showing it. Handlers
        // record what they would record on a close (selected scene, thumbnail, version-control snapshot) but
        // release nothing the session needs to come back.
        Deactivate,

        // The session is unloaded, and handlers release what belongs to it.
        Close,
    }

    // How the editor's sessions changed when a transition committed.
    internal enum ProjectSessionChange
    {
        // The session became the one the editor shows.
        Activated,

        // The session stays loaded, and the editor no longer shows it.
        Deactivated,

        // The session was unloaded after its changes were saved or released.
        Closed,

        // The session was unloaded and its unsaved changes were thrown away.
        Discarded,
    }

    // Published once a transition has changed a session. ActiveProject is the project the editor shows afterwards.
    internal sealed record ProjectTransitionCommit(
        ProjectSessionChange Change,
        ProjectSession Session,
        bool ActiveSessionChanged,
        Project? ActiveProject)
    {
        internal Project Project => Session.Project;
    }

    internal sealed class ProjectCloseContext(
        ProjectSession session,
        ProjectCloseKind kind,
        ProjectCloseIntent closeIntent)
    {
        private readonly object _gate = new();
        private readonly List<Func<bool, Task>> _completions = [];
        private bool _completed;

        // The session the chain deactivates or closes, which need not be the active one when it is closed.
        internal ProjectSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

        internal ProjectCloseKind Kind { get; } = kind;

        internal ProjectCloseIntent CloseIntent { get; } = closeIntent;

        // The snapshot phase may have saved these editors or obtained permission to discard them.
        internal EditorService? PreparedEditorService { get; set; }

        // A completion learns whether the chain committed: the session was deactivated or closed as asked.
        internal void RegisterCompletion(Func<bool, Task> completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            lock (_gate)
            {
                if (_completed)
                {
                    throw new InvalidOperationException(
                        "The project-close transition has already completed.");
                }

                _completions.Add(completion);
            }
        }

        internal async Task CompleteAsync(bool committed, ILogger logger)
        {
            Func<bool, Task>[] completions;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                completions = _completions.ToArray();
                _completions.Clear();
            }

            foreach (Func<bool, Task> completion in completions)
            {
                try
                {
                    await completion(committed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "A project-close completion callback failed.");
                }
            }
        }
    }

    // Owns the transition that creates a project, so observers can tell a new project from an opened one.
    internal sealed class ProjectCreation
    {
        private readonly CancellationTokenSource _preparationCancellation = new();

        // Canceled when another transition is requested while this creation still prepares its project,
        // so a step that waits on Git or on the user does not hold that request back.
        internal CancellationToken PreparationCancellation => _preparationCancellation.Token;

        internal void CancelPreparation()
        {
            _preparationCancellation.Cancel();
        }
    }

    // Owns the transition held by RunExclusiveOfTransitionsAsync, which opens and closes nothing.
    internal sealed class ProjectFileChange
    {
    }

    // One request to open a project file. A newer request for the same file supersedes it; a newer request for
    // another file only takes the activation from it, so both projects end up loaded.
    internal sealed class ProjectOpenAttempt(string? file = null, long sequence = 0)
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _gate = new();
        private ProjectOpenAttemptState _state;
        private bool _cancellationInProgress;
        private bool _disposeCancellationWhenCancelCompletes;

        internal CancellationToken CancellationToken => _cancellation.Token;

        internal bool IsCancellationRequested => _cancellation.IsCancellationRequested;

        internal string? File { get; } = file;

        // Orders the attempts of one ProjectService: a larger number was requested later.
        internal long Sequence { get; } = sequence;

        internal bool IsFor(string file)
            => File is { } own && FilePathComparison.AreSameCanonicalPath(own, file);

        internal bool TryBeginApply()
        {
            lock (_gate)
            {
                if (_state != ProjectOpenAttemptState.Pending
                    || _cancellation.IsCancellationRequested)
                {
                    return false;
                }

                _state = ProjectOpenAttemptState.Applying;
                return true;
            }
        }

        internal void CancelIfPending()
        {
            bool cancel;
            lock (_gate)
            {
                cancel = _state == ProjectOpenAttemptState.Pending;
                if (cancel)
                {
                    _state = ProjectOpenAttemptState.Cancelled;
                    _cancellationInProgress = true;
                }
            }

            if (cancel)
            {
                bool disposeCancellation;
                try
                {
                    _cancellation.Cancel();
                }
                finally
                {
                    lock (_gate)
                    {
                        _cancellationInProgress = false;
                        disposeCancellation = _disposeCancellationWhenCancelCompletes;
                    }

                    if (disposeCancellation)
                    {
                        _cancellation.Dispose();
                    }
                }
            }
        }

        internal void Complete()
        {
            bool disposeCancellation;
            lock (_gate)
            {
                _state = ProjectOpenAttemptState.Completed;
                disposeCancellation = !_cancellationInProgress;
                _disposeCancellationWhenCancelCompletes = !disposeCancellation;
            }

            if (disposeCancellation)
            {
                _cancellation.Dispose();
            }
        }
    }

    internal sealed class ProjectTransitionScope : IAsyncDisposable
    {
        private ProjectService? _owner;

        internal ProjectTransitionScope(ProjectService owner, ProjectTransitionContext context)
        {
            _owner = owner;
            Context = context;
        }

        internal ProjectTransitionContext Context { get; }

        // Closes the active session inside this transition.
        internal Task CloseProjectAsync(CancellationToken cancellationToken = default)
        {
            ProjectService owner = _owner
                                   ?? throw new ObjectDisposedException(nameof(ProjectTransitionScope));
            return owner.CloseSessionCoreAsync(
                owner.Sessions.ActiveSession.Value,
                Context,
                cancellationToken);
        }

        internal Task OpenProjectAsync(string file)
        {
            ProjectService owner = _owner
                                   ?? throw new ObjectDisposedException(nameof(ProjectTransitionScope));
            return owner.OpenProjectCoreAsync(file, Context);
        }

        public ValueTask DisposeAsync()
        {
            ProjectService? owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndTransition(Context);
            return ValueTask.CompletedTask;
        }
    }

    private enum ProjectOpenAttemptState
    {
        Pending,
        Applying,
        Cancelled,
        Completed,
    }
}

internal enum ProjectTransitionPurpose
{
    Normal,
    VersionControlMutation,
}

internal sealed record ProjectTransitionContext(
    ProjectTransitionPurpose Purpose,
    object Owner)
{
    // The session the transition loads, activates, deactivates or closes, once it is known.
    internal ProjectSession? TargetSession { get; set; }
}
