using System.Collections.Specialized;

namespace Beutl.Utilities;

public class WeakEvents
{
    public static readonly WeakEvent<INotifyCollectionChanged, NotifyCollectionChangedEventArgs>
        CollectionChanged = WeakEvent.Register<INotifyCollectionChanged, NotifyCollectionChangedEventArgs>(
            (c, s) =>
            {
                void handler(object? _, NotifyCollectionChangedEventArgs e) => s(c, e);
                c.CollectionChanged += handler;
                return () => c.CollectionChanged -= handler;
            });
}
