using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

internal sealed class OperationQueue
{
    private readonly object _lock = new();
    private readonly Queue<Action>[] _queuedOperations = new Queue<Action>[]
    {
        new Queue<Action>(),
        new Queue<Action>(),
        new Queue<Action>()
    };

    public void Enqueue(DispatchPriority priority, Action operation)
    {
        lock (_lock)
        {
            _queuedOperations[(int)priority].Enqueue(operation);
        }
    }

    public bool TryDequeue([NotNullWhen(true)] out Action? operation)
    {
        lock (_lock)
        {
            for (DispatchPriority priority = DispatchPriority.High; priority >= DispatchPriority.Low; --priority)
            {
                Queue<Action> queue = _queuedOperations[(int)priority];
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
