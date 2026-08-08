namespace Beutl.Utilities;

/// <summary>
/// Exception inspection helpers shared by recovery paths that must distinguish
/// environmental failures (I/O, access) from content damage.
/// </summary>
public static class ExceptionHelpers
{
    /// <summary>
    /// Returns <see langword="true"/> when the exception chain contains an
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>,
    /// including failures wrapped by reflection or aggregation.
    /// </summary>
    public static bool ContainsFileSystemFailure(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.TryPop(out Exception? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is IOException or UnauthorizedAccessException)
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return false;
    }
}
