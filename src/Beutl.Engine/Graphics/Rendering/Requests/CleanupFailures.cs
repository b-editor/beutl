using System.Runtime.ExceptionServices;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>Collects cleanup failures without duplicates and rethrows them as one exception.</summary>
internal static class CleanupFailures
{
    /// <summary>Adds <paramref name="failure"/>, or the leaves of an aggregate, unless already present.</summary>
    public static void Append(ICollection<Exception> failures, Exception failure)
    {
        if (failure is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                AddDistinct(failures, inner);
        }
        else
        {
            AddDistinct(failures, failure);
        }
    }

    /// <summary>Rethrows a single failure as it was, or several as one <see cref="AggregateException"/>.</summary>
    public static void ThrowIfAny(IReadOnlyList<Exception>? failures, string message)
    {
        if (failures is not { Count: > 0 })
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(message, failures);
    }

    private static void AddDistinct(ICollection<Exception> failures, Exception failure)
    {
        foreach (Exception existing in failures)
        {
            if (ReferenceEquals(existing, failure))
                return;
        }

        failures.Add(failure);
    }
}
