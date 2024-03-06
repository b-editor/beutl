namespace Beutl.Threading;

internal sealed class QueueSynchronizationContext(Dispatcher dispatcher) : SynchronizationContext
{
    private readonly OperationQueue _operationQueue = new();
    private readonly TimerQueue _timerQueue = new();

    private bool _running;
    private CancellationTokenSource? _waitToken;

    public event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException;

    public event EventHandler? ShutdownStarted;

    public event EventHandler? ShutdownFinished;

    public bool HasShutdownFinished { get; private set; }

    public bool HasShutdownStarted { get; private set; }

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

        lock (this)
        {
            _waitToken = new CancellationTokenSource();

            if (_timerQueue.Next is DateTime next)
                _waitToken.CancelAfter(next - DateTime.UtcNow);
        }

        _waitToken.Token.WaitHandle.WaitOne();

        lock (this)
        {
            _waitToken = null;
        }
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

    internal void PostDelayed(DateTime dateTime, DispatchPriority priority, Action action, CancellationToken ct)
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
