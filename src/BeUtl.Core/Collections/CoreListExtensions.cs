using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;

namespace BeUtl.Collections;

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
        void Add(int index, IList items)
        {
            foreach (T item in items)
            {
                added(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (var i = items.Count - 1; i >= 0; --i)
            {
                removed(index + i, (T)items[i]!);
            }
        }

        NotifyCollectionChangedEventHandler handler = (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Add(e.NewStartingIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Replace:
                    Remove(e.OldStartingIndex, e.OldItems!);
                    int newIndex = e.NewStartingIndex;
                    if (newIndex > e.OldStartingIndex)
                    {
                        newIndex -= e.OldItems!.Count;
                    }
                    Add(newIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Remove(e.OldStartingIndex, e.OldItems!);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    if (reset == null)
                    {
                        throw new InvalidOperationException(
                            "Reset called on collection without reset handler.");
                    }

                    reset();
                    Add(0, (IList)collection);
                    break;
            }
        };

        Add(0, (IList)collection);

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

    public static IDisposable TrackItemPropertyChanged<T>(
        this ICoreReadOnlyList<T> collection,
        Action<Tuple<object?, PropertyChangedEventArgs>> callback)
    {
        var tracked = new List<INotifyPropertyChanged>();

        PropertyChangedEventHandler handler = (s, e) =>
        {
            callback(Tuple.Create(s, e));
        };

        collection.ForEachItem(
            x =>
            {
                var inpc = x as INotifyPropertyChanged;

                if (inpc != null)
                {
                    inpc.PropertyChanged += handler;
                    tracked.Add(inpc);
                }
            },
            x =>
            {
                var inpc = x as INotifyPropertyChanged;

                if (inpc != null)
                {
                    inpc.PropertyChanged -= handler;
                    tracked.Remove(inpc);
                }
            },
            () => throw new NotSupportedException("Collection reset not supported."));

        return Disposable.Create(() =>
        {
            foreach (var i in tracked)
            {
                i.PropertyChanged -= handler;
            }
        });
    }
}
