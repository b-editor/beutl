using System.Collections.Specialized;

using Beutl.Reactive;
using Beutl.Utilities;

namespace Beutl.Collections;

public static class NotifyCollectionChangedExtensions
{
    public static IObservable<NotifyCollectionChangedEventArgs> GetWeakCollectionChangedObservable(
        this INotifyCollectionChanged collection)
    {
        _ = collection ?? throw new ArgumentNullException(nameof(collection));

        return new WeakCollectionChangedObservable(new WeakReference<INotifyCollectionChanged>(collection));
    }

    public static IDisposable WeakSubscribe(
        this INotifyCollectionChanged collection,
        NotifyCollectionChangedEventHandler handler)
    {
        _ = collection ?? throw new ArgumentNullException(nameof(collection));
        _ = handler ?? throw new ArgumentNullException(nameof(handler));

        return collection.GetWeakCollectionChangedObservable()
            .Subscribe(e => handler(collection, e));
    }

    public static IDisposable WeakSubscribe(
        this INotifyCollectionChanged collection,
        Action<NotifyCollectionChangedEventArgs> handler)
    {
        _ = collection ?? throw new ArgumentNullException(nameof(collection));
        _ = handler ?? throw new ArgumentNullException(nameof(handler));

        return collection.GetWeakCollectionChangedObservable().Subscribe(handler);
    }

    private class WeakCollectionChangedObservable(WeakReference<INotifyCollectionChanged> source)
        : LightweightObservableBase<NotifyCollectionChangedEventArgs>,
          IWeakEventSubscriber<NotifyCollectionChangedEventArgs>
    {
        public void OnEvent(object? sender, WeakEvent ev, NotifyCollectionChangedEventArgs e)
        {
            PublishNext(e);
        }

        protected override void Initialize()
        {
            if (source.TryGetTarget(out INotifyCollectionChanged? instance))
            {
                WeakEvents.CollectionChanged.Subscribe(instance, this);
            }
        }

        protected override void Deinitialize()
        {
            if (source.TryGetTarget(out INotifyCollectionChanged? instance))
            {
                WeakEvents.CollectionChanged.Unsubscribe(instance, this);
            }
        }
    }
}
