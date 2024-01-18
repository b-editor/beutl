using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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
        void Add(int index, IList items)
        {
            foreach (T item in items)
            {
                added(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = items.Count - 1; i >= 0; --i)
            {
                removed(index + i, (T)items[i]!);
            }
        }

        void handler(object? _, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Add(e.NewStartingIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Replace:
                    Remove(e.OldStartingIndex, e.OldItems!);
                    Add(e.NewStartingIndex, e.NewItems!);
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
        }

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
        void Add(int index, IList items)
        {
            foreach (T item in items)
            {
                added(index++, item);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = items.Count - 1; i >= 0; --i)
            {
                removed(index + i, (T)items[i]!);
            }
        }

        void handler(object? _, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Add(e.NewStartingIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Replace:
                    Remove(e.OldStartingIndex, e.OldItems!);
                    Add(e.NewStartingIndex, e.NewItems!);
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
        }

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
}
