using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

public class Dispatcher
{
    [ThreadStatic]
    private static Dispatcher? s_current;

    private readonly QueueSynchronizationContext _synchronizationContext;
    private readonly TimeProvider _timeProvider;
    // For tests
    internal bool _catchExceptions;

    private Dispatcher(TimeProvider timeProvider)
    {
        _synchronizationContext = new(this, timeProvider);
        _timeProvider = timeProvider;
        Thread = new Thread(Start);
        Thread.TrySetApartmentState(ApartmentState.STA);
    }

    public static Dispatcher Current => s_current!;

    public Thread Thread { get; }

    public bool HasShutdownStarted => _synchronizationContext.HasShutdownStarted;

    public bool HasShutdownFinished => _synchronizationContext.HasShutdownFinished;

    // For tests: only they subscribe, to observe what operations throw on the dispatcher thread.
    internal event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException
    {
        add => _synchronizationContext.UnhandledException += value;
        [ExcludeFromCodeCoverage]
        remove => _synchronizationContext.UnhandledException -= value;
    }

    public event EventHandler? ShutdownStarted
    {
        add => _synchronizationContext.ShutdownStarted += value;
        [ExcludeFromCodeCoverage]
        remove => _synchronizationContext.ShutdownStarted -= value;
    }

    public event EventHandler? ShutdownFinished
    {
        add => _synchronizationContext.ShutdownFinished += value;
        [ExcludeFromCodeCoverage]
        remove => _synchronizationContext.ShutdownFinished -= value;
    }

    private void Start()
    {
        Dispatcher? oldDispatcher = s_current;
        SynchronizationContext? oldSynchronizationContext = SynchronizationContext.Current;

        try
        {
            s_current = this;
            SynchronizationContext.SetSynchronizationContext(_synchronizationContext);

            _synchronizationContext.Start();
        }
        catch when (_catchExceptions)
        {
        }
        finally
        {
            s_current = oldDispatcher;
            SynchronizationContext.SetSynchronizationContext(oldSynchronizationContext);
        }
    }

    public void Shutdown()
    {
        _synchronizationContext.Shutdown();
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
        return Spawn(TimeProvider.System);
    }

    // For tests: they inject a clock to drive Schedule.
    internal static Dispatcher Spawn(TimeProvider timeProvider)
    {
        var dispatcher = new Dispatcher(timeProvider);
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

    // For a UI caller: a job that has not started by the deadline is dropped, one that has keeps running.
    public void Invoke(Action operation, TimeSpan timeout, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        Invoke(() =>
        {
            operation();
            return true;
        }, timeout, priority, ct);
    }

    public T Invoke<T>(Func<T> operation, TimeSpan timeout, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        if (CheckAccess())
        {
            ct.ThrowIfCancellationRequested();
            return operation();
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<T> task = _synchronizationContext.Send(priority, operation, cancellation.Token);
        if (!task.Wait(timeout, ct))
        {
            cancellation.Cancel();
            throw new TimeoutException($"The dispatcher did not finish the operation within {timeout}.");
        }

        return task.Result;
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

    public void Schedule(TimeSpan delay, Action operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        _synchronizationContext.PostDelayed(_timeProvider.GetUtcNow() + delay, priority, operation, ct);
    }

    public void Schedule(TimeSpan delay, Func<Task> operation, DispatchPriority priority = DispatchPriority.Medium, CancellationToken ct = default)
    {
        _synchronizationContext.PostDelayed(_timeProvider.GetUtcNow() + delay, priority, () => operation(), ct);
    }
}
