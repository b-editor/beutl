using System.Runtime.CompilerServices;

namespace BeUtl.Threading;

public readonly struct YieldTask
{
    private readonly DispatchPriority _priority;

    public YieldTask(DispatchPriority priority)
    {
        _priority = priority;
    }

    public YieldTaskAwaiter GetAwaiter()
    {
        return new YieldTaskAwaiter(_priority);
    }
}

public readonly struct YieldTaskAwaiter : INotifyCompletion
{
    private readonly DispatchPriority _priority;

    public YieldTaskAwaiter(DispatchPriority priority)
    {
        _priority = priority;
    }

    public bool IsCompleted
    {
        get
        {
            if (SynchronizationContext.Current is not QueueSynchronizationContext context)
            {
                throw new DispatcherException("Awaiting Dispatcher.Yield outside of QueueSynchronizationContext");
            }

            return !context.HasQueuedTasks(_priority);
        }
    }

    public void OnCompleted(Action continuation)
    {
        if (SynchronizationContext.Current is not QueueSynchronizationContext context)
        {
            throw new DispatcherException("Awaiting Dispatcher.Yield outside of QueueSynchronizationContext");
        }

        context.Post(_priority, continuation);
    }

    public void GetResult()
    { }
}
