using System.Runtime.ExceptionServices;

namespace Beutl.Threading;

internal sealed class QueueSynchronizationContext(Dispatcher dispatcher, TimeProvider timeProvider) : SynchronizationContext
{
    private enum LifecycleState
    {
        Created,
        Running,
        Stopping,
        Finished,
    }

    // CancelAfter throws when the delay exceeds int.MaxValue milliseconds; clamp to it.
    private static readonly TimeSpan s_maxCancelAfter = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly OperationQueue _operationQueue = new();
    private readonly TimerQueue _timerQueue = new(timeProvider);
    private readonly object _gate = new();

    // Written by Shutdown() outside the lock, read unlocked elsewhere; volatile stops a stale read
    // that misses a shutdown on weak-memory architectures.
    private volatile LifecycleState _state;
    private CancellationTokenSource? _waitToken;

    // Volatile for the same reason as _running: written on one thread, read cross-thread through the
    // public getters with no lock.
    private volatile bool _hasShutdownFinished;
    private volatile bool _hasShutdownStarted;

    public event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException;

    public event EventHandler? ShutdownStarted;

    public event EventHandler? ShutdownFinished;

    public bool HasShutdownFinished { get => _hasShutdownFinished; private set => _hasShutdownFinished = value; }

    public bool HasShutdownStarted { get => _hasShutdownStarted; private set => _hasShutdownStarted = value; }

    internal void Start()
    {
        bool shouldRun;
        lock (_gate)
        {
            shouldRun = _state == LifecycleState.Created;
            if (shouldRun)
                _state = LifecycleState.Running;
        }

        if (!shouldRun)
        {
            Finish();
            return;
        }

        try
        {
            while (_state == LifecycleState.Running)
            {
                ExecuteAvailableOperations();
                WaitForPendingOperations();
            }
        }
        finally
        {
            Finish();
        }
    }

    internal void Shutdown()
    {
        List<DispatcherOperation> abandoned;
        lock (_gate)
        {
            if (_state >= LifecycleState.Stopping)
                return;

            _state = LifecycleState.Stopping;
            HasShutdownStarted = true;
            _waitToken?.Cancel();
            abandoned = DrainQueues();
        }

        // The events must fire even when an abort fallback threw, or shutdown waiters would never resume.
        try
        {
            Abort(abandoned);
        }
        finally
        {
            ShutdownStarted?.Invoke(dispatcher, EventArgs.Empty);
        }
    }

    private void Finish()
    {
        List<DispatcherOperation> abandoned;
        bool raiseStarted;
        lock (_gate)
        {
            if (_state == LifecycleState.Finished)
                return;

            raiseStarted = !HasShutdownStarted;
            HasShutdownStarted = true;
            _state = LifecycleState.Finished;
            HasShutdownFinished = true;
            _waitToken?.Cancel();
            abandoned = DrainQueues();
        }

        try
        {
            Abort(abandoned);
        }
        finally
        {
            if (raiseStarted)
                ShutdownStarted?.Invoke(dispatcher, EventArgs.Empty);
            ShutdownFinished?.Invoke(dispatcher, EventArgs.Empty);
        }
    }

    private List<DispatcherOperation> DrainQueues()
    {
        List<DispatcherOperation> result = _operationQueue.Drain();
        result.AddRange(_timerQueue.Drain());
        return result;
    }

    private static void Abort(List<DispatcherOperation> operations)
    {
        var exception = new ObjectDisposedException(nameof(Dispatcher), "The dispatcher is shutting down.");
        Exception? abortFailure = null;
        foreach (DispatcherOperation operation in operations)
        {
            // A throwing abort fallback must not stop the sweep — every abandoned operation still gets aborted.
            try
            {
                operation.Abort(exception);
            }
            catch (Exception ex)
            {
                abortFailure ??= ex;
            }
        }

        if (abortFailure is not null)
            ExceptionDispatchInfo.Capture(abortFailure).Throw();
    }

    private void ExecuteAvailableOperations()
    {
        FlushTimerQueue();

        while (_state == LifecycleState.Running && _operationQueue.TryDequeue(out DispatcherOperation? operation))
        {
            try
            {
                operation.Run();
            }
            catch (Exception ex)
            {
                var args = new DispatcherUnhandledExceptionEventArgs(ex);
                UnhandledException?.Invoke(dispatcher, args);
                if (!args.Handled)
                {
                    throw;
                }
            }

            FlushTimerQueue();
        }
    }

    private void WaitForPendingOperations()
    {
        if (_state != LifecycleState.Running)
            return;

        CancellationTokenSource cts;
        lock (_gate)
        {
            // Shutdown() may have cleared _running since the check above; bail out so we
            // don't arm a _waitToken nothing cancels and block on WaitOne() forever.
            if (_state != LifecycleState.Running)
                return;

            // An operation may have been posted before we took the lock; re-check.
            if (_operationQueue.Any(DispatchPriority.Low))
            {
                return;
            }

            cts = new CancellationTokenSource();
            _waitToken = cts;

            if (_timerQueue.Next is { } next)
            {
                TimeSpan delay = next - timeProvider.GetUtcNow();
                if (delay <= TimeSpan.Zero)
                {
                    // The deadline has already passed; wake immediately without arming a timer.
                    cts.Cancel();
                }
                else
                {
                    // CancelAfter throws for delays longer than int.MaxValue ms, so clamp
                    // far-future timers; the wait is re-armed each cycle until the real deadline.
                    cts.CancelAfter(delay < s_maxCancelAfter ? delay : s_maxCancelAfter);
                }
            }
        }

        cts.Token.WaitHandle.WaitOne();

        lock (_gate)
        {
            _waitToken = null;
        }

        // Dispose after clearing _waitToken: other threads see null and won't cancel it.
        cts.Dispose();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        Send(DispatchPriority.High, () => d(state), default).GetAwaiter().GetResult();
    }

    internal Task Send(DispatchPriority priority, Action operation, CancellationToken ct)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int queuedState = 0;
        const int runningState = 1;
        const int finishedState = 2;
        int state = queuedState;
        CancellationTokenRegistration registration = default;
        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                if (Interlocked.CompareExchange(ref state, finishedState, queuedState) == queuedState)
                    completion.TrySetCanceled(ct);
            });
        }

        var queued = new DispatcherOperation(
            () =>
            {
                if (Interlocked.CompareExchange(ref state, runningState, queuedState) != queuedState)
                {
                    registration.Dispose();
                    return;
                }

                try
                {
                    operation();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    Volatile.Write(ref state, finishedState);
                    registration.Dispose();
                }
            },
            priority,
            default,
            ex =>
            {
                registration.Dispose();
                if (Interlocked.CompareExchange(ref state, finishedState, queuedState) == queuedState)
                    completion.TrySetException(ex);
            });
        if (!TryPost(queued))
            queued.Abort(new ObjectDisposedException(nameof(Dispatcher), "The dispatcher is shutting down."));
        return completion.Task;
    }

    internal Task<T> Send<T>(DispatchPriority priority, Func<T> operation, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        const int queuedState = 0;
        const int runningState = 1;
        const int finishedState = 2;
        int state = queuedState;
        CancellationTokenRegistration registration = default;
        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                if (Interlocked.CompareExchange(ref state, finishedState, queuedState) == queuedState)
                    completion.TrySetCanceled(ct);
            });
        }

        var queued = new DispatcherOperation(
            () =>
            {
                if (Interlocked.CompareExchange(ref state, runningState, queuedState) != queuedState)
                {
                    registration.Dispose();
                    return;
                }

                try
                {
                    completion.TrySetResult(operation());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    Volatile.Write(ref state, finishedState);
                    registration.Dispose();
                }
            },
            priority,
            default,
            ex =>
            {
                registration.Dispose();
                if (Interlocked.CompareExchange(ref state, finishedState, queuedState) == queuedState)
                    completion.TrySetException(ex);
            });
        if (!TryPost(queued))
            queued.Abort(new ObjectDisposedException(nameof(Dispatcher), "The dispatcher is shutting down."));
        return completion.Task;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        Post(DispatchPriority.High, () => d(state), default);
    }

    internal void Post(DispatchPriority priority, Action operation, CancellationToken ct)
    {
        DispatcherOperation queued = new(operation, priority, ct);
        if (!TryPost(queued))
            queued.Abort(new ObjectDisposedException(nameof(Dispatcher), "The dispatcher is shutting down."));
    }

    internal bool TryPost(
        DispatchPriority priority,
        Action operation,
        CancellationToken ct,
        Action<Exception>? abort)
    {
        DispatcherOperation queued = new(operation, priority, ct, abort);
        if (TryPost(queued))
            return true;

        queued.Abort(new ObjectDisposedException(nameof(Dispatcher), "The dispatcher is shutting down."));
        return false;
    }

    internal void Post(DispatcherOperation operation)
    {
        if (!TryPost(operation))
            operation.Abort(new ObjectDisposedException(nameof(Dispatcher), "The dispatcher is shutting down."));
    }

    private bool TryPost(DispatcherOperation operation)
    {
        lock (_gate)
        {
            if (_state >= LifecycleState.Stopping)
                return false;

            _operationQueue.Enqueue(operation);
            _waitToken?.Cancel();
            return true;
        }
    }

    internal void PostDelayed(DateTimeOffset dateTime, DispatchPriority priority, Action action, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_state >= LifecycleState.Stopping)
                return;

            _timerQueue.Enqueue(dateTime, priority, action, ct);
            _waitToken?.Cancel();
        }
    }

    internal bool HasQueuedTasks(DispatchPriority priority)
    {
        return _operationQueue.Any(priority);
    }

    private void FlushTimerQueue()
    {
        while (_timerQueue.TryDequeue(out List<DispatcherOperation>? operations))
        {
            foreach (DispatcherOperation operation in operations)
            {
                Post(operation);
            }
        }
    }
}
