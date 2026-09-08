using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed partial class ProjectService
{
    private readonly Dictionary<ProjectTransitionContext, TaskCompletionSource> _transitionCompletions = [];
    private readonly object _projectOperationGate = new();
    private Task _projectOperationTask = Task.CompletedTask;
    private bool _projectTransitionAdmissionClosed;
    private TaskCompletionSource? _projectTransitionAdmission;
    private readonly object _closeGate = new();
    private Task? _activeCloseTask;
    private Task? _activeCloseOperationTail;
    private readonly object _projectNotificationGate = new();
    private Task _projectNotificationTask = Task.CompletedTask;
    private readonly object _projectChangeGate = new();
    private IProjectChangeHandler? _projectChangeHandler;
    private IProjectChangeHandler? _closingProjectChangeHandler;
    private Task _lastProjectChangeTask = Task.CompletedTask;

    internal Func<string, Task>? BeforeCreateProjectPreparation { get; set; }

    internal Action<string>? AfterCreateProjectPreparation { get; set; }

    internal Func<string, Task>? BeforeRecentProjectUpdate { get; set; }

    internal Func<Task>? BeforeProjectChangeHandlerInitialization { get; set; }

    internal ProjectChangeRegistration RegisterProjectChangeHandler(IProjectChangeHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Task previous;
        Project? current;
        TaskCompletionSource? admission;
        var initialization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectOperationGate)
        {
            if (!_projectTransitionAdmissionClosed && !_projectOperationTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "An editor host cannot be registered during a project transition.");
            }

            lock (_projectChangeGate)
            {
                if (_projectChangeHandler is not null || _closingProjectChangeHandler is not null)
                {
                    throw new InvalidOperationException(
                        "An editor host is already registered or still completing shutdown.");
                }

                if (_projectTransitionAdmissionClosed
                    && (_projectTransitionAdmission is null
                        || _projectTransitionAdmission.Task.IsCompleted))
                {
                    _projectTransitionAdmission = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _projectChangeHandler = handler;
                current = _app.Project;
                previous = _lastProjectChangeTask;
                _lastProjectChangeTask = initialization.Task;
                admission = _projectTransitionAdmissionClosed
                    ? _projectTransitionAdmission
                    : null;
            }
        }

        _ = InitializeProjectChangeHandlerAsync(
            previous,
            handler,
            current,
            initialization,
            admission);

        return new ProjectChangeRegistration(this, handler);
    }

    private async Task InitializeProjectChangeHandlerAsync(
        Task previous,
        IProjectChangeHandler handler,
        Project? current,
        TaskCompletionSource completion,
        TaskCompletionSource? admission)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A previous project change failed before editor-host initialization.");
        }

        try
        {
            if (BeforeProjectChangeHandlerInitialization is { } beforeInitialization)
            {
                await beforeInitialization();
            }

            await handler.ApplyProjectChangeAsync(current, null);
            await handler.WaitForPendingProjectChangesAsync();
            OpenProjectTransitionAdmission(admission);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            CloseProjectTransitionAdmission(admission, ex);
            completion.TrySetException(ex);
        }
    }

    private void OpenProjectTransitionAdmission(TaskCompletionSource? admission)
    {
        if (admission is null)
            return;

        lock (_projectOperationGate)
        {
            bool isCurrent = ReferenceEquals(_projectTransitionAdmission, admission);
            if (isCurrent)
                _projectTransitionAdmissionClosed = false;
            admission.TrySetResult();
        }
    }

    private void CloseProjectTransitionAdmission(
        TaskCompletionSource? admission,
        Exception exception)
    {
        if (admission is null)
            return;

        lock (_projectOperationGate)
        {
            admission.TrySetException(exception);
        }
    }

    /// <summary>
    /// Waits until every project transition accepted before this call, together with causally
    /// queued project-item changes, has reached a stable editor state.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A reentrant wait from an editor tab operation that project reconciliation would otherwise
    /// have to drain was detected while causal execution context remained available.
    /// </exception>
    public async Task WaitForPendingProjectChangesAsync()
    {
        if (EditorService.IsTabLifecycleOperationActive)
        {
            throw new InvalidOperationException(
                "Project changes cannot be awaited from an active editor tab operation.");
        }

        Task transition;
        IProjectChangeHandler? handler;
        Task lastPublished;
        lock (_projectOperationGate)
        {
            transition = _projectOperationTask;
        }

        lock (_projectChangeGate)
        {
            handler = _projectChangeHandler;
            lastPublished = _lastProjectChangeTask;
        }

        await transition;
        await lastPublished;
        if (handler is not null)
        {
            await handler.WaitForPendingProjectChangesAsync();
        }
    }

    private async Task ApplyProjectChangeAsync(
        Project? @new,
        Project? old)
    {
        Task previous;
        IProjectChangeHandler? handler;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectChangeGate)
        {
            previous = _lastProjectChangeTask;
            handler = _projectChangeHandler;
            _lastProjectChangeTask = completion.Task;
        }

        _ = CompleteProjectChangeAsync(
            previous,
            handler,
            @new,
            old,
            completion);
        await completion.Task;
    }

    private async Task PublishProjectClosingAsync(ProjectCloseContext context)
    {
        Task previous;
        IProjectChangeHandler? handler;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectChangeGate)
        {
            previous = _lastProjectChangeTask;
            handler = _projectChangeHandler;
            _lastProjectChangeTask = completion.Task;
        }
        try
        {
            await previous;
            if (handler is not null)
                await handler.ApplyProjectCloseAsync(context);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task CompleteProjectChangeAsync(
        Task previous,
        IProjectChangeHandler? handler,
        Project? @new,
        Project? old,
        TaskCompletionSource completion)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A previous project change failed before the next change could run.");
        }

        try
        {
            if (handler is not null)
            {
                await handler.ApplyProjectChangeAsync(@new, old);
                await handler.WaitForPendingProjectChangesAsync();
            }

            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void QueueProjectNotification(Project? @new, Project? old, Task operationCompletion)
    {
        Task previous;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectNotificationGate)
        {
            previous = _projectNotificationTask;
            _projectNotificationTask = completion.Task;
        }

        _ = CompleteProjectNotificationAsync(previous, operationCompletion, @new, old, completion);
    }

    private async Task CompleteProjectNotificationAsync(
        Task previous,
        Task operationCompletion,
        Project? @new,
        Project? old,
        TaskCompletionSource completion)
    {
        try
        {
            await previous;
            await operationCompletion;
            PublishProjectObservers((@new, old));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A project observer failed after the editor reached a stable state.");
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task BeginUnregisterProjectChangeHandlerAsync(IProjectChangeHandler handler)
    {
        Task operation;
        lock (_projectOperationGate)
        {
            operation = _projectOperationTask;
            lock (_projectChangeGate)
            {
                if (!ReferenceEquals(_projectChangeHandler, handler))
                    return;
            }

            _projectTransitionAdmissionClosed = true;
            _projectTransitionAdmission = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Keep the handler installed while already-admitted transitions drain. An operation can
        // be queued before unregister starts but reach PublishProjectChangeAsync afterwards.
        await operation;

        Task pending;
        lock (_projectChangeGate)
        {
            if (ReferenceEquals(_projectChangeHandler, handler))
            {
                _projectChangeHandler = null;
                _closingProjectChangeHandler = handler;
            }

            pending = _lastProjectChangeTask;
        }

        try
        {
            await pending;
        }
        finally
        {
            FailProjectTransitionAdmission();
        }
    }

    private void FailProjectTransitionAdmission()
    {
        lock (_projectOperationGate)
        {
            if (_projectTransitionAdmissionClosed)
            {
                _projectTransitionAdmission?.TrySetException(new InvalidOperationException(
                    "The editor host was unregistered before the project transition could be applied."));
            }
        }
    }

    private void CompleteUnregisterProjectChangeHandler(IProjectChangeHandler handler)
    {
        lock (_projectChangeGate)
        {
            if (ReferenceEquals(_closingProjectChangeHandler, handler))
            {
                _closingProjectChangeHandler = null;
            }
        }
    }

    internal sealed class ProjectChangeRegistration(
        ProjectService owner,
        IProjectChangeHandler handler) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private ProjectService? _owner = owner;
        private Task? _beginDisposeTask;

        internal Task BeginDisposeAsync()
        {
            lock (_gate)
            {
                return _beginDisposeTask ??= _owner is { } currentOwner
                    ? currentOwner.BeginUnregisterProjectChangeHandlerAsync(handler)
                    : Task.CompletedTask;
            }
        }

        internal void CompleteDispose()
        {
            ProjectService? currentOwner;
            lock (_gate)
            {
                currentOwner = _owner;
                _owner = null;
            }

            currentOwner?.CompleteUnregisterProjectChangeHandler(handler);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await BeginDisposeAsync();
            }
            finally
            {
                CompleteDispose();
            }
        }
    }

}

internal interface IProjectChangeHandler
{
    Task ApplyProjectCloseAsync(ProjectService.ProjectCloseContext context)
        => ApplyProjectChangeAsync(null, null);
    Task ApplyProjectChangeAsync(Project? @new, Project? old);
    Task WaitForPendingProjectChangesAsync();
}
