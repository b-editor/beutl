using System.Diagnostics;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

/// <summary>
/// Admits asynchronous operations while a view-model is alive, owns their cancellation token,
/// admits publication before shutdown begins, keeps admitted publication in the
/// drain, and allows disposal to await every admitted operation. Publication admission and
/// shutdown are linearized by the gate. If a publication acquires the gate before
/// <see cref="StopAsync"/> or <see cref="DisposeAsync()"/> marks the lifetime as stopping, its
/// callback may run after shutdown has started, but remains in the drain until that callback
/// returns.
/// </summary>
internal sealed class AsyncOperationLifetime : IAsyncDisposable
{
    private static readonly TimeSpan s_defaultShutdownDeadline = TimeSpan.FromSeconds(30);
    private static readonly ILogger s_logger = Log.CreateLogger<AsyncOperationLifetime>();
    private readonly object _gate = new();
    private readonly TimeSpan _shutdownDeadline;
    private readonly Action<Exception>? _deferredFailureObserver;
    private CancellationTokenSource? _cancellation = new();
    private TaskCompletionSource? _drained;
    private Task? _stopTask;
    private TaskCompletionSource? _stopCompletion;
    private Task? _stopDrainTask;
    private TaskCompletionSource? _stopDrainCompletion;
    private Task? _stopCancellationTask;
    private readonly List<CancellationTokenSource> _deferredOperationCancellations = [];
    private Exception? _cancellationFailure;
    private Task? _disposeTask;
    private int _activeOperations;
    private int _activePublications;
    private bool _stopping;

    public AsyncOperationLifetime(
        TimeSpan? shutdownDeadline = null,
        Action<Exception>? deferredFailureObserver = null)
    {
        _shutdownDeadline = shutdownDeadline ?? s_defaultShutdownDeadline;
        if (_shutdownDeadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownDeadline));
        _deferredFailureObserver = deferredFailureObserver;
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
        long started = Stopwatch.GetTimestamp();
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
                drained = HasNoActiveWork_NoLock()
                    ? Task.CompletedTask
                    : (_drained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                _stopCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _stopDrainCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = _stopCompletion.Task;
                _stopDrainTask = _stopDrainCompletion.Task;
                _stopCancellationTask = CreateCancellationTask(cancellation);
                startCompletion = true;
            }

            stopTask = _stopTask;
        }

        if (startCompletion)
        {
            Task cancellationTask = _stopCancellationTask!;
            if (cancellation is not null)
            {
                // Preserve the old synchronous observation that admission has
                // been cancelled, without running user callbacks on this
                // thread. CancellationTokenSource marks itself cancelled before
                // invoking callbacks, so this wait cannot be held by a
                // reentrant callback.
                SpinWait.SpinUntil(
                    () => cancellation.IsCancellationRequested || cancellationTask.IsCompleted,
                    _shutdownDeadline);
            }
            _ = CompleteStopDrainAsync(drained!, cancellationTask);
            TimeSpan remaining = _shutdownDeadline - Stopwatch.GetElapsedTime(started);
            _ = CompleteStopProxyAsync(
                _stopCompletion!,
                _stopDrainTask!,
                remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }

        return stopTask;
    }

    private void CancelStop(CancellationTokenSource cancellation)
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

    private Task CreateCancellationTask(CancellationTokenSource? cancellation)
        => cancellation is null
            ? Task.CompletedTask
            : Task.Factory.StartNew(
                () => CancelStop(cancellation),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

    private async Task CompleteStopDrainAsync(Task drained, Task cancellationTask)
    {
        List<Exception>? failures = null;
        try
        {
            await Task.WhenAll(drained, cancellationTask).ConfigureAwait(false);
        }
        catch
        {
            AddFailures(drained, ref failures);
            AddFailures(cancellationTask, ref failures);
        }

        TaskCompletionSource? completion;
        Exception? failure;
        List<CancellationTokenSource>? deferredCancellations;
        lock (_gate)
        {
            completion = _stopDrainCompletion;
            failure = _cancellationFailure;
            deferredCancellations = _deferredOperationCancellations.Count == 0
                ? null
                : [.. _deferredOperationCancellations];
            _deferredOperationCancellations.Clear();
        }

        if (deferredCancellations is not null)
        {
            foreach (CancellationTokenSource deferred in deferredCancellations)
                deferred.Dispose();
        }

        if (failure is not null)
        {
            IEnumerable<Exception> cancellationFailures = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [failure];
            foreach (Exception cancellationFailure in cancellationFailures)
            {
                if (failures?.Contains(cancellationFailure) != true)
                    (failures ??= []).Add(cancellationFailure);
            }
        }

        if (failures is { Count: 1 })
            completion?.TrySetException(failures[0]);
        else if (failures is { Count: > 1 })
            completion?.TrySetException(new AggregateException(failures));
        else
            completion?.TrySetResult();
    }

    private async Task CompleteStopProxyAsync(
        TaskCompletionSource completion,
        Task drain,
        TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            s_logger.LogWarning(
                "Asynchronous operation shutdown exceeded {Deadline}; draining will continue.",
                _shutdownDeadline);
            completion.TrySetResult();
            _ = ObserveDeferredStopAsync(drain);
            return;
        }

        try
        {
            await drain.WaitAsync(remaining).ConfigureAwait(false);
            if (drain.IsFaulted)
                completion.TrySetException(drain.Exception!.Flatten().InnerExceptions);
            else
                completion.TrySetResult();
        }
        catch (TimeoutException)
        {
            s_logger.LogWarning(
                "Asynchronous operation shutdown exceeded {Deadline}; draining will continue.",
                _shutdownDeadline);
            completion.TrySetResult();
            _ = ObserveDeferredStopAsync(drain);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task ObserveDeferredStopAsync(Task drain)
    {
        try
        {
            await drain.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Deferred asynchronous operation shutdown failed.");
            _deferredFailureObserver?.Invoke(ex);
        }
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
                    // The cancellation task is created together with the stop
                    // state below, before any callback can re-enter.
                }
                if (_stopTask is null)
                {
                    drained = HasNoActiveWork_NoLock()
                        ? Task.CompletedTask
                        : (_drained ??= new TaskCompletionSource(
                            TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                    _stopCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopDrainCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopTask = _stopCompletion.Task;
                    _stopDrainTask = _stopDrainCompletion.Task;
                    _stopCancellationTask = CreateCancellationTask(_cancellation);
                    startStopCompletion = true;
                }
            }

            disposeTask = _disposeTask;
        }

        if (proxy is not null)
        {
            Task stopDrainTask = _stopDrainTask!;
            Task teardown = Task.Run(() => DisposeCoreAsync(
                drained,
                startStopCompletion,
                stopDrainTask,
                cancelAdditionalWork,
                disposeResources));
            if (startStopCompletion)
            {
                _ = CompleteStopProxyAsync(_stopCompletion!, stopDrainTask, _shutdownDeadline);
            }
            _ = CompleteDisposeAsync(proxy, teardown);
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(
        Task? drained,
        bool startStopCompletion,
        Task stopDrainTask,
        Action cancelAdditionalWork,
        Func<ValueTask> disposeResources)
    {
        List<Exception>? failures = null;
        Task additionalCancellation = Task.Run(cancelAdditionalWork);

        if (startStopCompletion)
            _ = CompleteStopDrainAsync(drained!, _stopCancellationTask!);

        try
        {
            await Task.WhenAll(stopDrainTask, additionalCancellation).ConfigureAwait(false);
        }
        catch
        {
            AddFailures(stopDrainTask, ref failures);
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
            s_logger.LogWarning(
                "Asynchronous operation disposal exceeded {Deadline}; resource cleanup will continue.",
                _shutdownDeadline);
            proxy.TrySetResult();
            _ = ObserveDeferredDisposeAsync(teardown);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    private async Task ObserveDeferredDisposeAsync(Task teardown)
    {
        try
        {
            await teardown.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Deferred asynchronous operation resource cleanup failed.");
            _deferredFailureObserver?.Invoke(ex);
        }
    }

    private bool TryPublish(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (_gate)
        {
            if (_stopping)
                return false;

            // This increment is the publication's linearization point. Shutdown may close
            // admission immediately afterward; the callback runs outside the lock and may
            // overlap shutdown, but disposal cannot finish until this decrement observes it.
            _activePublications++;
        }

        try
        {
            publication();
            return true;
        }
        finally
        {
            ExitPublication();
        }
    }

    private void ExitPublication()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_activePublications <= 0)
                return;

            _activePublications--;
            if (_stopping && HasNoActiveWork_NoLock())
                drained = _drained;
        }
        drained?.TrySetResult();
    }

    private bool HasNoActiveWork_NoLock()
        => _activeOperations == 0 && _activePublications == 0;

    private void Exit(CancellationTokenSource operationCancellation)
    {
        TaskCompletionSource? drained = null;
        bool disposeCancellation = true;
        lock (_gate)
        {
            if (_activeOperations <= 0)
                return;

            _activeOperations--;
            if (_stopping && _stopCancellationTask is { IsCompleted: false })
            {
                _deferredOperationCancellations.Add(operationCancellation);
                disposeCancellation = false;
            }
            if (_stopping && HasNoActiveWork_NoLock())
            {
                drained = _drained;
            }
        }
        if (disposeCancellation)
            operationCancellation.Dispose();
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

        /// <summary>
        /// Attempts to admit a publication. The callback is invoked outside the lifetime gate;
        /// once admitted, it is included in shutdown draining even if it throws.
        /// </summary>
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

            owner.Exit(_cancellation);
        }
    }
}
