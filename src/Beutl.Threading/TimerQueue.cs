using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

internal sealed class TimerQueue
{
    private readonly object _lock = new();
    private readonly SortedDictionary<DateTime, List<(DispatchPriority Priority, Action Operation)>> _operations = new();

    public DateTime? Next
    {
        get
        {
            lock (_lock)
            {
                SortedDictionary<DateTime, List<(DispatchPriority Priority, Action Operation)>>.Enumerator enumerator;
                enumerator = _operations.GetEnumerator();
                return enumerator.MoveNext() ? enumerator.Current.Key : null;
            }
        }
    }

    public void Enqueue(DateTime timestamp, DispatchPriority priority, Action operation)
    {
        lock (_lock)
        {
            if (_operations.TryGetValue(timestamp, out List<(DispatchPriority Priority, Action Operation)>? stampedOperations))
            {
                stampedOperations.Add((priority, operation));
            }
            else
            {
                _operations.Add(timestamp, new List<(DispatchPriority, Action)> { (priority, operation) });
            }
        }
    }

    public bool TryDequeue([NotNullWhen(true)] out List<(DispatchPriority Priority, Action Operation)>? operations)
    {
        lock (_lock)
        {
            SortedDictionary<DateTime, List<(DispatchPriority Priority, Action Operation)>>.Enumerator enumerator;
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
