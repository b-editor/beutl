namespace Beutl.Threading;

internal sealed class QueueSynchronizationContext(Dispatcher dispatcher, TimeProvider timeProvider) : SynchronizationContext
{
    // Re-arm long waits until the deadline, keeping each timer within int.MaxValue milliseconds.
    private static readonly TimeSpan s_maxWaitDelay = TimeSpan.FromMilliseconds(int.MaxValue);

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
        lock (this)
        {
            // Shutdown() can win the race with this thread reaching Start(). Setting _running back to
            // true would restart the loop it stopped, and nothing would stop it a second time: the
            // dispatcher would run forever and never raise ShutdownFinished.
            if (!HasShutdownStarted)
                _running = true;
        }

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
        // Under the same lock as Start()'s _running write, so a dispatcher thread still on its way
        // into the loop observes this shutdown instead of overwriting it.
        lock (this)
        {
            HasShutdownStarted = true;
            _running = false;
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

            cts = CreateWaitTokenSource();
            _waitToken = cts;
        }

        cts.Token.WaitHandle.WaitOne();

        lock (this)
        {
            _waitToken = null;
        }

        // Dispose after clearing _waitToken: other threads see null and won't cancel it.
        cts.Dispose();
    }

    // Called with the lock on this held.
    private CancellationTokenSource CreateWaitTokenSource()
    {
        CancellationTokenSource cts;
        if (_timerQueue.Next is { } next)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            TimeSpan delay = next - now;
            if (delay <= TimeSpan.Zero)
            {
                // The deadline has already passed; wake immediately without arming a timer.
                cts = new CancellationTokenSource();
                cts.Cancel();
            }
            else
            {
                TimeSpan waitDelay = delay < s_maxWaitDelay ? delay : s_maxWaitDelay;
                cts = new CancellationTokenSource(waitDelay, timeProvider);
                DateTimeOffset wakeAt = now + waitDelay;
                DateTimeOffset armedAt = now;
                while (!cts.IsCancellationRequested)
                {
                    DateTimeOffset currentTime = timeProvider.GetUtcNow();
                    if (wakeAt <= currentTime)
                    {
                        cts.Cancel();
                        break;
                    }
                    // Cancellation timers have millisecond precision. Do not chase the
                    // sub-millisecond cost of reading a continuously advancing system clock.
                    if (currentTime - armedAt < TimeSpan.FromMilliseconds(1))
                        break;

                    // Timer creation and Change both start a relative delay. Recheck after
                    // each arm so a clock advance in either operation cannot extend the deadline.
                    armedAt = currentTime;
                    cts.CancelAfter(wakeAt - currentTime);
                }
            }
        }
        else
        {
            cts = new CancellationTokenSource();
        }

        return cts;
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (dispatcher.CheckAccess())
        {
            d(state);
            return;
        }

        Send(DispatchPriority.High, () => d(state), default).Wait();
    }

    internal Task Send(DispatchPriority priority, Action operation, CancellationToken ct)
    {
        var task = new Task(operation, ct);
        Post(priority, () => RunSynchronously(task), ct);
        return task;
    }

    internal Task<T> Send<T>(DispatchPriority priority, Func<T> operation, CancellationToken ct)
    {
        var task = new Task<T>(operation, ct);
        Post(priority, () => RunSynchronously(task), ct);
        return task;
    }

    private static void RunSynchronously(Task task)
    {
        try
        {
            task.RunSynchronously();
        }
        catch (InvalidOperationException) when (task.IsCanceled)
        {
            // Cancellation can complete the task after DispatcherOperation checks the token.
            // The returned task already carries the cancellation; the dispatcher has no failure.
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        Post(DispatchPriority.High, () => d(state), default);
    }

    internal void Post(DispatchPriority priority, Action operation, CancellationToken ct)
    {
        _operationQueue.Enqueue(new(operation, priority, ct));
        WakeDispatcher();
    }

    internal void Post(DispatcherOperation operation)
    {
        _operationQueue.Enqueue(operation);
        WakeDispatcher();
    }

    internal void PostDelayed(DateTimeOffset dateTime, DispatchPriority priority, Action action, CancellationToken ct)
    {
        _timerQueue.Enqueue(dateTime, priority, action, ct);
        WakeDispatcher();
    }

    private void WakeDispatcher()
    {
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
