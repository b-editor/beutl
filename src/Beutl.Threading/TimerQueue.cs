using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

internal sealed class TimerQueue(TimeProvider timeProvider)
{
    private readonly object _lock = new();
    private readonly SortedDictionary<DateTimeOffset, List<DispatcherOperation>> _operations = [];

    public DateTimeOffset? Next
    {
        get
        {
            lock (_lock)
            {
                SortedDictionary<DateTimeOffset, List<DispatcherOperation>>.Enumerator enumerator;
                enumerator = _operations.GetEnumerator();
                return enumerator.MoveNext() ? enumerator.Current.Key : null;
            }
        }
    }

    public void Enqueue(DateTimeOffset timestamp, DispatchPriority priority, Action operation, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_operations.TryGetValue(timestamp, out List<DispatcherOperation>? stampedOperations))
            {
                stampedOperations.Add(new(operation, priority, ct));
            }
            else
            {
                _operations.Add(timestamp, [new(operation, priority, ct)]);
            }
        }
    }

    public bool TryDequeue([NotNullWhen(true)] out List<DispatcherOperation>? operations)
    {
        lock (_lock)
        {
            SortedDictionary<DateTimeOffset, List<DispatcherOperation>>.Enumerator enumerator;
            enumerator = _operations.GetEnumerator();
            if (enumerator.MoveNext() && enumerator.Current.Key <= timeProvider.GetUtcNow())
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
