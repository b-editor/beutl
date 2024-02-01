using System.Diagnostics;

namespace Beutl.Threading;

public class Dispatcher
{
    [ThreadStatic]
    private static Dispatcher? s_current;

    private readonly QueueSynchronizationContext _synchronizationContext = new();
    private Thread? _thread;

    public static Dispatcher Current => s_current!;

    public void Start()
    {
        Dispatcher? oldDispatcher = s_current;
        SynchronizationContext? oldSynchronizationContext = SynchronizationContext.Current;

        try
        {
            s_current = this;
            _thread = Thread.CurrentThread;
            SynchronizationContext.SetSynchronizationContext(_synchronizationContext);

            _synchronizationContext.Start();
        }
        finally
        {
            s_current = oldDispatcher;
            SynchronizationContext.SetSynchronizationContext(oldSynchronizationContext);
        }
    }

    public void Execute()
    {
        Dispatcher? oldDispatcher = s_current;
        SynchronizationContext? oldSynchronizationContext = SynchronizationContext.Current;

        try
        {
            s_current = this;
            _thread = Thread.CurrentThread;
            SynchronizationContext.SetSynchronizationContext(_synchronizationContext);

            _synchronizationContext.Execute();
        }
        finally
        {
            s_current = oldDispatcher;
            SynchronizationContext.SetSynchronizationContext(oldSynchronizationContext);
        }
    }

    public void Stop()
    {
        _synchronizationContext.Stop();
        Debug.WriteLine($"'{_thread?.Name ?? _thread?.ManagedThreadId.ToString()}' を停止しました");
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
        var thread = new Thread(() =>
        {
            dispatcher.Start();
        });
        thread.TrySetApartmentState(ApartmentState.STA);
        thread.Start();

        return dispatcher;
    }

    public static Dispatcher Spawn(Action operation)
    {
        Dispatcher dispatcher = Spawn();
        dispatcher.Dispatch(operation, DispatchPriority.High);
        return dispatcher;
    }

    public void Invoke(Action operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        if (CheckAccess())
        {
            operation();
        }
        else
        {
            _synchronizationContext.Send(priority, operation);
        }
    }

    public T Invoke<T>(Func<T> operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        if (CheckAccess())
        {
            return operation();
        }
        else
        {
            return InvokeAsync(operation, priority).Result;
        }
    }

    public Task InvokeAsync(Action operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        return _synchronizationContext.Send(priority, operation);
    }

    public async Task InvokeAsync(Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        await await InvokeAsync<Task>(operation, priority);
    }

    public Task<T> InvokeAsync<T>(Func<T> operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        return _synchronizationContext.Send(priority, operation);
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        return await await InvokeAsync<Task<T>>(operation, priority);
    }

    public void Dispatch(Action operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        _synchronizationContext.Post(priority, operation);
    }

    public void Dispatch(Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        _synchronizationContext.Post(priority, () => operation());
    }

    public void Run(Action operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        if (CheckAccess())
        {
            operation();
        }
        else
        {
            Dispatch(operation, priority);
        }
    }

    public void Run(Func<Task> operation, DispatchPriority priority)
    {
        if (CheckAccess())
        {
            operation();
        }
        else
        {
            Dispatch(operation, priority);
        }
    }

    public void Schedule(TimeSpan delay, Action operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        _synchronizationContext.PostDelayed(DateTime.UtcNow + delay, priority, operation);
    }

    public void Schedule(TimeSpan delay, Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium)
    {
        _synchronizationContext.PostDelayed(DateTime.UtcNow + delay, priority, () => operation());
    }

    public static YieldTask Yield(DispatchPriority priority = DispatchPriority.Low)
    {
        return new YieldTask(priority);
    }
}
