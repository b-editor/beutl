using Reactive.Bindings;

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

        int index = FindOrderedInsertIndex(list, value, keySelector, comparer, descending: false);
        if (index >= 0)
        {
            list.Insert(index, value);
        }
        else
        {
            list.Add(value);
        }
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

        int index = FindOrderedInsertIndex(list, value, keySelector, comparer, descending: true);
        if (index >= 0)
        {
            list.Insert(index, value);
        }
        else
        {
            list.Add(value);
        }
    }

    public static void OrderedAddOnScheduler<T, TKey>(this ReactiveCollection<T> list, T value, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        int index = FindOrderedInsertIndex(list, value, keySelector, comparer, descending: false);
        if (index >= 0)
        {
            list.InsertOnScheduler(index, value);
        }
        else
        {
            list.AddOnScheduler(value);
        }
    }

    public static void OrderedAddDescendingOnScheduler<T, TKey>(this ReactiveCollection<T> list, T value, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        int index = FindOrderedInsertIndex(list, value, keySelector, comparer, descending: true);
        if (index >= 0)
        {
            list.InsertOnScheduler(index, value);
        }
        else
        {
            list.AddOnScheduler(value);
        }
    }

    // The index the ordered adds insert value at, or -1 when it belongs after every item.
    private static int FindOrderedInsertIndex<T, TKey>(
        IList<T> list,
        T value,
        Func<T, TKey> keySelector,
        IComparer<TKey>? comparer,
        bool descending)
    {
        comparer ??= Comparer<TKey>.Default;

        TKey? valueKey = keySelector(value);
        for (int i = 0; i < list.Count; i++)
        {
            TKey key = keySelector(list[i]);

            int comparison = comparer.Compare(valueKey, key);
            if (descending ? comparison >= 0 : comparison <= 0)
            {
                return i;
            }
        }

        return -1;
    }
}
