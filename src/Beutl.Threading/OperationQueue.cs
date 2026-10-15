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

    public bool Any(DispatchPriority minPriority)
    {
        lock (_lock)
        {
            for (DispatchPriority priority = DispatchPriority.High; priority >= minPriority; --priority)
            {
                if (_queuedOperations[(int)priority].Count > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
