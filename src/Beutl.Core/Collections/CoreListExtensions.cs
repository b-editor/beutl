using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;

namespace Beutl.Collections;

public static class CoreListExtensions
{
    public static IDisposable ForEachItem<T>(
           this ICoreReadOnlyList<T> collection,
           Action<T> added,
           Action<T> removed,
           Action reset)
    {
        return collection.ForEachItem((_, i) => added(i), (_, i) => removed(i), reset);
    }

    public static IDisposable ForEachItem<T>(
        this ICoreReadOnlyList<T> collection,
        Action<int, T> added,
        Action<int, T> removed,
        Action reset)
    {
        NotifyCollectionChangedEventHandler handler = CreateItemHandler(
            added,
            removed,
            reset,
            () => AddItems(added, 0, (IList)collection));
        AddItems(added, 0, (IList)collection);
        return Subscribe(collection, handler);
    }

    public static IDisposable ForEachItem<T, TCollection>(
        this TCollection collection,
        Action<int, T> added,
        Action<int, T> removed,
        Action reset)
        where TCollection : IReadOnlyList<T>, INotifyCollectionChanged
    {
        NotifyCollectionChangedEventHandler handler = CreateItemHandler(
            added,
            removed,
            reset,
            () => AddItems(added, 0, collection));
        AddItems(added, 0, collection);
        return Subscribe(collection, handler);
    }

    public static IDisposable TrackCollectionChanged<T>(
           this ICoreReadOnlyList<T> collection,
           Action<T> added,
           Action<T> removed,
           Action reset)
    {
        return collection.TrackCollectionChanged((_, i) => added(i), (_, i) => removed(i), reset);
    }

    public static IDisposable TrackCollectionChanged<T>(
        this ICoreReadOnlyList<T> collection,
        Action<int, T> added,
        Action<int, T> removed,
        Action reset)
    {
        NotifyCollectionChangedEventHandler handler = CreateItemHandler(
            added,
            removed,
            reset,
            () => AddItems(added, 0, (IList)collection));
        return Subscribe(collection, handler);
    }

    // The handler the ForEachItem/TrackCollectionChanged variants share: addAll is how each variant
    // re-adds the whole collection after a reset.
    private static NotifyCollectionChangedEventHandler CreateItemHandler<T>(
        Action<int, T> added,
        Action<int, T> removed,
        Action reset,
        Action addAll)
    {
        return (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddItems(added, e.NewStartingIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Replace:
                    RemoveItems(removed, e.OldStartingIndex, e.OldItems!);
                    AddItems(added, e.NewStartingIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    RemoveItems(removed, e.OldStartingIndex, e.OldItems!);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    reset();
                    addAll();
                    break;
            }
        };
    }

    private static void AddItems<T>(Action<int, T> added, int index, IList items)
    {
        foreach (T item in items)
        {
            added(index++, item);
        }
    }

    private static void AddItems<T, TCollection>(Action<int, T> added, int index, TCollection items)
        where TCollection : IReadOnlyList<T>
    {
        foreach (T item in items)
        {
            added(index++, item);
        }
    }

    private static void RemoveItems<T>(Action<int, T> removed, int index, IList items)
    {
        for (int i = items.Count - 1; i >= 0; --i)
        {
            removed(index + i, (T)items[i]!);
        }
    }

    private static IDisposable Subscribe<TCollection>(
        TCollection collection,
        NotifyCollectionChangedEventHandler handler)
        where TCollection : INotifyCollectionChanged
    {
        collection.CollectionChanged += handler;

        return Disposable.Create(() => collection.CollectionChanged -= handler);
    }
}
