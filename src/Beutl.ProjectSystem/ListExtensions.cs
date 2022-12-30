namespace Beutl;

public static class ListExtensions
{
    public static void OrderedAdd<T, TKey>(this IList<T> list, T value, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        comparer ??= Comparer<TKey>.Default;

        TKey? valueKey = keySelector(value);
        for (int i = 0; i < list.Count; i++)
        {
            TKey key = keySelector(list[i]);

            if (comparer.Compare(valueKey, key) <= 0)
            {
                list.Insert(i, value);
                return;
            }
        }

        list.Add(value);
    }

    public static void OrderedAddDescending<T, TKey>(this IList<T> list, T value, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        comparer ??= Comparer<TKey>.Default;

        TKey? valueKey = keySelector(value);
        for (int i = 0; i < list.Count; i++)
        {
            TKey key = keySelector(list[i]);

            if (comparer.Compare(valueKey, key) >= 0)
            {
                list.Insert(i, value);
                return;
            }
        }

        list.Add(value);
    }
}
