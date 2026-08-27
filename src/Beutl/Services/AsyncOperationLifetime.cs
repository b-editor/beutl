namespace Beutl.Services;

/// <summary>
/// Admits asynchronous operations while a view-model is alive, owns their cancellation token,
/// prevents publication after shutdown begins, and allows disposal to await every admitted operation.
/// </summary>
internal sealed class AsyncOperationLifetime : IAsyncDisposable
{
    private static readonly TimeSpan s_defaultShutdownDeadline = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private readonly TimeSpan _shutdownDeadline;
    private CancellationTokenSource? _cancellation = new();
    private TaskCompletionSource? _drained;
    private Task? _stopTask;
    private TaskCompletionSource? _stopCompletion;
    private Exception? _cancellationFailure;
    private Task? _disposeTask;
    private int _activeOperations;
    private bool _stopping;

    public AsyncOperationLifetime(TimeSpan? shutdownDeadline = null)
    {
        _shutdownDeadline = shutdownDeadline ?? s_defaultShutdownDeadline;
        if (_shutdownDeadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownDeadline));
    }

    public Operation? TryEnter()
    {
        lock (_gate)
        {
            if (_stopping || _cancellation is null)
                return null;

            _activeOperations++;
            return new Operation(
                this,
                CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token));
        }
    }

    public Task StopAsync()
    {
        CancellationTokenSource? cancellation = null;
        Task stopTask;
        Task? drained = null;
        bool startCompletion = false;
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
                cancellation = _cancellation;
            }

            if (_stopTask is null)
            {
                drained = _activeOperations == 0
                    ? Task.CompletedTask
                    : (_drained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                _stopCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = _stopCompletion.Task;
                startCompletion = true;
            }

            stopTask = _stopTask;
        }

        if (startCompletion && cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (Exception ex)
            {
                RecordCancellationFailure(ex);
            }
        }

        if (startCompletion)
        {
            _ = CompleteStopAsync(drained!);
        }

        return stopTask;
    }

    private async Task CompleteStopAsync(Task drained)
    {
        await drained.ConfigureAwait(false);
        TaskCompletionSource? completion;
        Exception? failure;
        lock (_gate)
        {
            completion = _stopCompletion;
            failure = _cancellationFailure;
        }

        if (failure is not null)
            completion?.TrySetException(failure);
        else
            completion?.TrySetResult();
    }

    public ValueTask DisposeAsync()
        => DisposeAsync(static () => ValueTask.CompletedTask);

    public ValueTask DisposeAsync(Func<ValueTask> disposeResources)
        => DisposeAsync(static () => { }, disposeResources);

    public ValueTask DisposeAsync(Action cancelAdditionalWork, Func<ValueTask> disposeResources)
    {
        ArgumentNullException.ThrowIfNull(cancelAdditionalWork);
        ArgumentNullException.ThrowIfNull(disposeResources);
        TaskCompletionSource? proxy = null;
        CancellationTokenSource? cancellation = null;
        Task? drained = null;
        bool startStopCompletion = false;
        Task disposeTask;
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                proxy = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = proxy.Task;

                if (!_stopping)
                {
                    _stopping = true;
                    cancellation = _cancellation;
                }
                if (_stopTask is null)
                {
                    drained = _activeOperations == 0
                        ? Task.CompletedTask
                        : (_drained ??= new TaskCompletionSource(
                            TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                    _stopCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopTask = _stopCompletion.Task;
                    startStopCompletion = true;
                }
            }

            disposeTask = _disposeTask;
        }

        if (proxy is not null)
        {
            Task stopTask = _stopTask!;
            Task teardown = Task.Run(() => DisposeCoreAsync(
                cancellation,
                drained,
                startStopCompletion,
                stopTask,
                cancelAdditionalWork,
                disposeResources));
            _ = CompleteDisposeAsync(proxy, teardown);
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(
        CancellationTokenSource? cancellation,
        Task? drained,
        bool startStopCompletion,
        Task stopTask,
        Action cancelAdditionalWork,
        Func<ValueTask> disposeResources)
    {
        List<Exception>? failures = null;
        Task additionalCancellation = Task.Run(cancelAdditionalWork);

        try
        {
            if (cancellation is not null)
                cancellation.Cancel();
        }
        catch (Exception ex)
        {
            RecordCancellationFailure(ex);
            (failures ??= []).Add(ex);
        }
        if (startStopCompletion)
            _ = CompleteStopAsync(drained!);

        try
        {
            await Task.WhenAll(stopTask, additionalCancellation).ConfigureAwait(false);
        }
        catch
        {
            AddFailures(stopTask, ref failures);
            AddFailures(additionalCancellation, ref failures);
        }

        try
        {
            await disposeResources().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }

        CancellationTokenSource? ownedCancellation;
        lock (_gate)
        {
            ownedCancellation = _cancellation;
            _cancellation = null;
        }
        ownedCancellation?.Dispose();

        if (failures is { Count: 1 })
            throw failures[0];
        if (failures is { Count: > 1 })
            throw new AggregateException(failures);
    }

    private static void AddFailures(Task task, ref List<Exception>? failures)
    {
        if (task.Exception is not { } aggregate)
            return;

        foreach (Exception exception in aggregate.Flatten().InnerExceptions)
        {
            if (failures?.Contains(exception) != true)
                (failures ??= []).Add(exception);
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource proxy, Task teardown)
    {
        try
        {
            await teardown.WaitAsync(_shutdownDeadline).ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (TimeoutException)
        {
            proxy.TrySetResult();
            _ = ObserveDeferredDisposeAsync(teardown);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    private static async Task ObserveDeferredDisposeAsync(Task teardown)
    {
        try
        {
            await teardown.ConfigureAwait(false);
        }
        catch
        {
            // The initiating DisposeAsync call has already completed at its deadline.
            // Cleanup still runs to completion and its failure is intentionally observed here.
        }
    }

    private bool TryPublish(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (_gate)
        {
            if (_stopping)
                return false;
        }

        publication();
        return true;
    }

    private void Exit()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_activeOperations <= 0)
                return;

            _activeOperations--;
            if (_stopping && _activeOperations == 0)
            {
                drained = _drained;
            }
        }
        drained?.TrySetResult();
    }

    private void RecordCancellationFailure(Exception exception)
    {
        lock (_gate)
        {
            _cancellationFailure = _cancellationFailure is null
                ? exception
                : new AggregateException(_cancellationFailure, exception);
        }
    }

    /// <summary>
    /// One admitted operation. Its token dies with the view-model, and separately
    /// with <see cref="Cancel"/>, so a caller can abandon a single long request
    /// without shutting down everything else the view-model has admitted.
    /// </summary>
    internal sealed class Operation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private AsyncOperationLifetime? _owner;

        internal Operation(AsyncOperationLifetime owner, CancellationTokenSource cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
            CancellationToken = cancellation.Token;
        }

        public CancellationToken CancellationToken { get; }

        public bool TryPublish(Action publication)
            => _owner?.TryPublish(publication) == true;

        public void Cancel()
        {
            if (_owner is not { } owner)
                return;

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                owner.RecordCancellationFailure(ex);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } owner)
                return;

            _cancellation.Dispose();
            owner.Exit();
        }
    }
}
