namespace Beutl.Threading;

internal sealed class QueueSynchronizationContext(Dispatcher dispatcher, TimeProvider timeProvider) : SynchronizationContext
{
    // CancelAfter throws when the delay exceeds int.MaxValue milliseconds; clamp to it.
    private static readonly TimeSpan s_maxCancelAfter = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly OperationQueue _operationQueue = new();
    private readonly TimerQueue _timerQueue = new(timeProvider);

    // Written by Shutdown() outside the lock, read unlocked elsewhere; volatile stops a stale read
    // that misses a shutdown on weak-memory architectures.
    private volatile bool _running;
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
        _running = true;

        while (_running)
        {
            ExecuteAvailableOperations();
            WaitForPendingOperations();
        }

        HasShutdownFinished = true;
        ShutdownFinished?.Invoke(dispatcher, EventArgs.Empty);
    }

    internal void Shutdown()
    {
        HasShutdownStarted = true;
        _running = false;
        lock (this)
        {
            _waitToken?.Cancel();
        }

        ShutdownStarted?.Invoke(dispatcher, EventArgs.Empty);
    }

    private void ExecuteAvailableOperations()
    {
        FlushTimerQueue();

        while (_running && _operationQueue.TryDequeue(out DispatcherOperation? operation))
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
        if (!_running)
            return;

        CancellationTokenSource cts;
        lock (this)
        {
            // Shutdown() may have cleared _running since the check above; bail out so we
            // don't arm a _waitToken nothing cancels and block on WaitOne() forever.
            if (!_running)
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

        lock (this)
        {
            _waitToken = null;
        }

        // Dispose after clearing _waitToken: other threads see null and won't cancel it.
        cts.Dispose();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        Send(DispatchPriority.High, () => d(state), default).Wait();
    }

    internal Task Send(DispatchPriority priority, Action operation, CancellationToken ct)
    {
        var task = new Task(operation, ct);
        Post(priority, () => task.RunSynchronously(), ct);
        return task;
    }

    internal Task<T> Send<T>(DispatchPriority priority, Func<T> operation, CancellationToken ct)
    {
        var task = new Task<T>(operation, ct);
        Post(priority, () => task.RunSynchronously(), ct);
        return task;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        Post(DispatchPriority.High, () => d(state), default);
    }

    internal void Post(DispatchPriority priority, Action operation, CancellationToken ct)
    {
        _operationQueue.Enqueue(new(operation, priority, ct));
        lock (this)
        {
            _waitToken?.Cancel();
        }
    }

    internal void Post(DispatcherOperation operation)
    {
        _operationQueue.Enqueue(operation);
        lock (this)
        {
            _waitToken?.Cancel();
        }
    }

    internal void PostDelayed(DateTimeOffset dateTime, DispatchPriority priority, Action action, CancellationToken ct)
    {
        _timerQueue.Enqueue(dateTime, priority, action, ct);
        lock (this)
        {
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
