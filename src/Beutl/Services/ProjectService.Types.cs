using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
    internal enum ProjectCloseIntent
    {
        SaveChanges,
        DiscardChanges,
    }

    internal sealed class ProjectCloseContext(ProjectCloseIntent closeIntent)
    {
        private readonly object _gate = new();
        private readonly List<Func<bool, Task>> _completions = [];
        private bool _completed;

        internal ProjectCloseIntent CloseIntent { get; } = closeIntent;

        // The snapshot phase may have saved these editors or obtained permission to discard them.
        internal EditorService? PreparedEditorService { get; set; }

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

        internal async Task CompleteAsync(bool projectClosed, ILogger logger)
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
                    await completion(projectClosed);
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

    internal sealed class ProjectOpenAttempt
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _gate = new();
        private ProjectOpenAttemptState _state;
        private bool _cancellationInProgress;
        private bool _disposeCancellationWhenCancelCompletes;

        internal CancellationToken CancellationToken => _cancellation.Token;

        internal bool IsCancellationRequested => _cancellation.IsCancellationRequested;

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

        internal Task CloseProjectAsync(CancellationToken cancellationToken = default)
        {
            ProjectService owner = _owner
                                   ?? throw new ObjectDisposedException(nameof(ProjectTransitionScope));
            return owner.CloseProjectCoreAsync(Context, cancellationToken);
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
    long Id,
    ProjectTransitionPurpose Purpose,
    object Owner);
