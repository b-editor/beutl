using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeUtl;

internal static class FastEnumerableExtensions
{
    public static T[] SelectArray<TSource, T>(this IReadOnlyList<TSource> list, Func<TSource, T> selector)
    {
        var array = new T[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            TSource item = list[i];
            array[i] = selector(item);
        }

        return array;
    }

    public static T[] SelectArray<TSource, T>(this ReadOnlySpan<TSource> list, Func<TSource, T> selector)
    {
        var array = new T[list.Length];
        int i = 0;
        foreach (TSource item in list)
        {
            array[i] = selector(item);
            i++;
        }

        return array;
    }

    public static void SelectArray<TSource, T>(this IReadOnlyList<TSource> list, Span<T> dst, Func<TSource, T> selector)
    {
        for (int i = 0; i < list.Count; i++)
        {
            TSource item = list[i];
            dst[i] = selector(item);
        }
    }

    public static void SelectArray<TSource, T>(this ReadOnlySpan<TSource> list, Span<T> dst, Func<TSource, T> selector)
    {
        int i = 0;
        foreach (TSource item in list)
        {
            dst[i] = selector(item);
            i++;
        }
    }
}
