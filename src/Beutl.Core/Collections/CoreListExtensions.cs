using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;

namespace Beutl.Collections;

public static class CoreListExtensions
{
    public static IDisposable ForEachItem<T>(
           this ICoreReadOnlyList<T> collection,
           Action<T> added,
           Action<T> removed,
           Action reset,
           bool weakSubscription = false)
    {
        return collection.ForEachItem((_, i) => added(i), (_, i) => removed(i), reset, weakSubscription);
    }

    public static IDisposable ForEachItem<T>(
        this ICoreReadOnlyList<T> collection,
        Action<int, T> added,
        Action<int, T> removed,
        Action reset,
        bool weakSubscription = false)
    {
        NotifyCollectionChangedEventHandler handler = CreateItemHandler(
            added,
            removed,
            reset,
            () => AddItems(added, 0, (IList)collection));
        AddItems(added, 0, (IList)collection);
        return Subscribe(collection, handler, weakSubscription);
    }

    public static IDisposable ForEachItem<T, TCollection>(
        this TCollection collection,
        Action<int, T> added,
        Action<int, T> removed,
        Action reset,
        bool weakSubscription = false)
        where TCollection : IReadOnlyList<T>, INotifyCollectionChanged
    {
        NotifyCollectionChangedEventHandler handler = CreateItemHandler(
            added,
            removed,
            reset,
            () => AddItems(added, 0, collection));
        AddItems(added, 0, collection);
        return Subscribe(collection, handler, weakSubscription);
    }

    public static IDisposable TrackCollectionChanged<T>(
           this ICoreReadOnlyList<T> collection,
           Action<T> added,
           Action<T> removed,
           Action reset,
           bool weakSubscription = false)
    {
        return collection.TrackCollectionChanged((_, i) => added(i), (_, i) => removed(i), reset, weakSubscription);
    }

    public static IDisposable TrackCollectionChanged<T>(
        this ICoreReadOnlyList<T> collection,
        Action<int, T> added,
        Action<int, T> removed,
        Action reset,
        bool weakSubscription = false)
    {
        NotifyCollectionChangedEventHandler handler = CreateItemHandler(
            added,
            removed,
            reset,
            () => AddItems(added, 0, (IList)collection));
        return Subscribe(collection, handler, weakSubscription);
    }

    public static IDisposable TrackItemPropertyChanged<T>(
        this ICoreReadOnlyList<T> collection,
        Action<Tuple<object?, PropertyChangedEventArgs>> callback)
    {
        var tracked = new List<INotifyPropertyChanged>();

        void handler(object? s, PropertyChangedEventArgs e)
        {
            callback(Tuple.Create(s, e));
        }

        collection.ForEachItem(
            x =>
            {
                if (x is INotifyPropertyChanged inpc)
                {
                    inpc.PropertyChanged += handler;
                    tracked.Add(inpc);
                }
            },
            x =>
            {
                if (x is INotifyPropertyChanged inpc)
                {
                    inpc.PropertyChanged -= handler;
                    tracked.Remove(inpc);
                }
            },
            () => throw new NotSupportedException("Collection reset not supported."));

        return Disposable.Create(() =>
        {
            foreach (INotifyPropertyChanged i in tracked)
            {
                i.PropertyChanged -= handler;
            }
        });
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
                    if (reset == null)
                    {
                        throw new InvalidOperationException(
                            "Reset called on collection without reset handler.");
                    }

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
        NotifyCollectionChangedEventHandler handler,
        bool weakSubscription)
        where TCollection : INotifyCollectionChanged
    {
        if (weakSubscription)
        {
            return collection.WeakSubscribe(handler);
        }
        else
        {
            collection.CollectionChanged += handler;

            return Disposable.Create(() => collection.CollectionChanged -= handler);
        }
    }
}
