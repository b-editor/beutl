using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

/// <summary>Projects a sequence into an array without the intermediate a LINQ query would allocate.</summary>
/// <remarks>
/// Planning walks the same fragment lists once per frame, so a query object between the source and the array
/// it materializes is garbage once a frame. A <see langword="static"/> selector is cached by the compiler,
/// which leaves the result array as the only allocation.
/// </remarks>
internal static class RenderProjection
{
    public static TResult[] SelectToArray<TSource, TResult>(
        this ImmutableArray<TSource> source,
        Func<TSource, TResult> selector)
    {
        var result = new TResult[source.Length];
        for (int index = 0; index < result.Length; index++)
            result[index] = selector(source[index]);
        return result;
    }

    /// <summary>Projects the first <paramref name="count"/> elements, skipping the rest.</summary>
    public static TResult[] SelectToArray<TSource, TResult>(
        this ImmutableArray<TSource> source,
        int count,
        Func<TSource, TResult> selector)
    {
        var result = new TResult[Math.Min(count, source.Length)];
        for (int index = 0; index < result.Length; index++)
            result[index] = selector(source[index]);
        return result;
    }

    public static TResult[] SelectToArray<TSource, TResult>(
        this IReadOnlyList<TSource> source,
        Func<TSource, TResult> selector)
    {
        var result = new TResult[source.Count];
        for (int index = 0; index < result.Length; index++)
            result[index] = selector(source[index]);
        return result;
    }

    public static TResult[] SelectToArray<TSource, TResult>(
        this TSource[] source,
        Func<TSource, TResult> selector)
    {
        var result = new TResult[source.Length];
        for (int index = 0; index < result.Length; index++)
            result[index] = selector(source[index]);
        return result;
    }
}
