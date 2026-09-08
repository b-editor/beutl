using System.Reflection;

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
        => Contains(exception, static current => current is IOException or UnauthorizedAccessException);

    public static bool ContainsFatalFailure(Exception exception)
        => Contains(exception, static current => current is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or OperationCanceledException);

    public static bool ContainsNonRecoverableFileSystemFailure(Exception exception)
        => Contains(exception, static current => current is UnauthorizedAccessException
            or IOException and not FileNotFoundException);

    private static bool Contains(Exception exception, Func<Exception, bool> predicate)
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

            if (predicate(current))
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
            else if (current is ReflectionTypeLoadException reflectionLoad)
            {
                foreach (Exception? loaderException in reflectionLoad.LoaderExceptions)
                {
                    if (loaderException is not null)
                    {
                        pending.Push(loaderException);
                    }
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
