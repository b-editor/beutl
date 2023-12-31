using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

internal sealed class TimerQueue
{
    private readonly object _lock = new();
    private readonly SortedDictionary<DateTime, List<DispatcherOperation>> _operations = [];

    public DateTime? Next
    {
        get
        {
            lock (_lock)
            {
                SortedDictionary<DateTime, List<DispatcherOperation>>.Enumerator enumerator;
                enumerator = _operations.GetEnumerator();
                return enumerator.MoveNext() ? enumerator.Current.Key : null;
            }
        }
    }

    public void Enqueue(DateTime timestamp, DispatchPriority priority, Action operation)
    {
        lock (_lock)
        {
            if (_operations.TryGetValue(timestamp, out List<DispatcherOperation>? stampedOperations))
            {
                stampedOperations.Add(new(operation, priority));
            }
            else
            {
                _operations.Add(timestamp, [new(operation, priority)]);
            }
        }
    }

    public bool TryDequeue([NotNullWhen(true)] out List<DispatcherOperation>? operations)
    {
        lock (_lock)
        {
            SortedDictionary<DateTime, List<DispatcherOperation>>.Enumerator enumerator;
            enumerator = _operations.GetEnumerator();
            if (enumerator.MoveNext() && enumerator.Current.Key <= DateTime.UtcNow)
            {
                operations = enumerator.Current.Value;
                _operations.Remove(enumerator.Current.Key);

                return true;
            }
        }

        operations = default;
        return false;
    }
}
