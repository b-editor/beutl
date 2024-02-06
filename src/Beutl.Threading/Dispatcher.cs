using System.Diagnostics;

namespace Beutl.Threading;

public class Dispatcher
{
    [ThreadStatic]
    private static Dispatcher? s_current;

    private readonly QueueSynchronizationContext _synchronizationContext;

    private Dispatcher()
    {
        _synchronizationContext = new(this);
        Thread = new Thread(Start);
        Thread.TrySetApartmentState(ApartmentState.STA);
    }

    public static Dispatcher Current => s_current!;

    public Thread Thread { get; private set; }

    public bool HasShutdownStarted => _synchronizationContext.HasShutdownStarted;

    public bool HasShutdownFinished => _synchronizationContext.HasShutdownFinished;

    public event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException
    {
        add => _synchronizationContext.UnhandledException += value;
        remove => _synchronizationContext.UnhandledException -= value;
    }

    public event EventHandler? ShutdownStarted
    {
        add => _synchronizationContext.ShutdownStarted += value;
        remove => _synchronizationContext.ShutdownStarted -= value;
    }

    public event EventHandler? ShutdownFinished
    {
        add => _synchronizationContext.ShutdownFinished += value;
        remove => _synchronizationContext.ShutdownFinished -= value;
    }

    private void Start()
    {
        Dispatcher? oldDispatcher = s_current;
        SynchronizationContext? oldSynchronizationContext = SynchronizationContext.Current;

        try
        {
            s_current = this;
            Thread = Thread.CurrentThread;
            SynchronizationContext.SetSynchronizationContext(_synchronizationContext);

            _synchronizationContext.Start();
        }
        finally
        {
            s_current = oldDispatcher;
            SynchronizationContext.SetSynchronizationContext(oldSynchronizationContext);
        }
    }

    [Obsolete("Use Shutdown.")]
    public void Stop()
    {
        Shutdown();
    }

    public void Shutdown()
    {
        _synchronizationContext.Shutdown();
        Debug.WriteLine($"'{Thread?.Name ?? Thread?.ManagedThreadId.ToString()}' を停止しました");
    }

    public bool CheckAccess()
    {
        return this == Current;
    }

    public void VerifyAccess()
    {
        if (!CheckAccess())
            throw new InvalidOperationException("Call from invalid thread");
    }

    public static Dispatcher Spawn()
    {
        var dispatcher = new Dispatcher();
        dispatcher.Thread.Start();
        return dispatcher;
    }

    public static Dispatcher Spawn(Action operation)
    {
        Dispatcher dispatcher = Spawn();
        dispatcher.Dispatch(operation, DispatchPriority.High);
        return dispatcher;
    }

    public void Invoke(Action operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        if (CheckAccess())
        {
            ct.ThrowIfCancellationRequested();
            operation();
        }
        else
        {
            _synchronizationContext.Send(priority, operation, ct).Wait(ct);
        }
    }

    public T Invoke<T>(Func<T> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        if (CheckAccess())
        {
            ct.ThrowIfCancellationRequested();
            return operation();
        }
        else
        {
            Task<T> task = InvokeAsync(operation, priority, ct);
            task.Wait(ct);
            return task.Result;
        }
    }

    public Task InvokeAsync(Action operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        return _synchronizationContext.Send(priority, operation, ct);
    }

    public async Task InvokeAsync(Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        await await InvokeAsync<Task>(operation, priority, ct);
    }

    public Task<T> InvokeAsync<T>(Func<T> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        return _synchronizationContext.Send(priority, operation, ct);
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        return await await InvokeAsync<Task<T>>(operation, priority, ct);
    }

    public void Dispatch(Action operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        _synchronizationContext.Post(priority, operation, ct);
    }

    public void Dispatch(Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        _synchronizationContext.Post(priority, () => operation(), ct);
    }

    public void Run(Action operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        if (CheckAccess())
        {
            if (!ct.IsCancellationRequested)
                operation();
        }
        else
        {
            Dispatch(operation, priority, ct);
        }
    }

    public void Run(Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        if (CheckAccess())
        {
            if (!ct.IsCancellationRequested)
                operation();
        }
        else
        {
            Dispatch(operation, priority, ct);
        }
    }

    public void Schedule(TimeSpan delay, Action operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        _synchronizationContext.PostDelayed(DateTime.UtcNow + delay, priority, operation, ct);
    }

    public void Schedule(TimeSpan delay, Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        _synchronizationContext.PostDelayed(DateTime.UtcNow + delay, priority, () => operation(), ct);
    }

    public static YieldTask Yield(DispatchPriority priority = DispatchPriority.Low)
    {
        return new YieldTask(priority);
    }
}
