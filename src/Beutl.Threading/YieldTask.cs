using System.Runtime.CompilerServices;

namespace Beutl.Threading;

public readonly struct YieldTask(DispatchPriority priority)
{
    public YieldTaskAwaiter GetAwaiter()
    {
        return new YieldTaskAwaiter(priority);
    }
}

public readonly struct YieldTaskAwaiter(DispatchPriority priority) : INotifyCompletion
{
    public bool IsCompleted
    {
        get
        {
            if (SynchronizationContext.Current is not QueueSynchronizationContext context)
            {
                throw new DispatcherException("Awaiting Dispatcher.Yield outside of QueueSynchronizationContext");
            }

            return !context.HasQueuedTasks(priority);
        }
    }

    public void OnCompleted(Action continuation)
    {
        if (SynchronizationContext.Current is not QueueSynchronizationContext context)
        {
            throw new DispatcherException("Awaiting Dispatcher.Yield outside of QueueSynchronizationContext");
        }

        context.Post(priority, continuation);
    }

    public void GetResult()
    { }
}
