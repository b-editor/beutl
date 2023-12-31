using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

internal sealed class OperationQueue
{
    private readonly object _lock = new();
    private readonly Queue<DispatcherOperation>[] _queuedOperations =
    [
        new Queue<DispatcherOperation>(),
        new Queue<DispatcherOperation>(),
        new Queue<DispatcherOperation>()
    ];

    public void Enqueue(DispatcherOperation operation)
    {
        lock (_lock)
        {
            _queuedOperations[(int)operation.Priority].Enqueue(operation);
        }
    }

    public bool TryDequeue([NotNullWhen(true)] out DispatcherOperation? operation)
    {
        lock (_lock)
        {
            for (DispatchPriority priority = DispatchPriority.High; priority >= DispatchPriority.Low; --priority)
            {
                Queue<DispatcherOperation> queue = _queuedOperations[(int)priority];
                if (queue.Count > 0)
                {
                    operation = queue.Dequeue();
                    return true;
                }
            }
        }

        operation = default;
        return false;
    }

    public int Count(DispatchPriority minPriority)
    {
        int count = 0;

        lock (_lock)
        {
            for (DispatchPriority priority = DispatchPriority.High; priority >= minPriority; --priority)
            {
                count += _queuedOperations[(int)priority].Count;
            }
        }

        return count;
    }

    public bool Any(DispatchPriority minPriority)
    {
        return Count(minPriority) > 0;
    }
}
