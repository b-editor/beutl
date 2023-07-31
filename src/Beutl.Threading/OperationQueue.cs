using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

// Todo: ExecutionContext
//   https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/Dispatcher.cs#L514
//   https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/DispatcherOperation.cs#L22
//   https://github.com/dotnet/wpf/blob/11b45badc1a514ac9a3311145316b2e7cb543eae/src/Microsoft.DotNet.Wpf/src/Shared/MS/Internal/CulturePreservingExecutionContext.cs#L68
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
