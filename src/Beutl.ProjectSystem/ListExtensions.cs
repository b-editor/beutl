namespace Beutl;

public static class ListExtensions
{
    public static void OrderedAdd<T, TKey>(this IList<T> list, T value, Func<T, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(keySelector);

        int index = FindOrderedInsertIndex(list, value, keySelector);
        if (index >= 0)
        {
            list.Insert(index, value);
        }
        else
        {
            list.Add(value);
        }
    }

    // The index OrderedAdd inserts value at, or -1 when it belongs after every item.
    private static int FindOrderedInsertIndex<T, TKey>(IList<T> list, T value, Func<T, TKey> keySelector)
    {
        Comparer<TKey> comparer = Comparer<TKey>.Default;

        TKey? valueKey = keySelector(value);
        for (int i = 0; i < list.Count; i++)
        {
            TKey key = keySelector(list[i]);

            if (comparer.Compare(valueKey, key) <= 0)
            {
                return i;
            }
        }

        return -1;
    }
}
