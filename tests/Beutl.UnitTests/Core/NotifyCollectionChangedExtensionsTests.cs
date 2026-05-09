using System.Collections.Specialized;
using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class NotifyCollectionChangedExtensionsTests
{
    [Test]
    public void GetWeakCollectionChangedObservable_NullCollection_Throws()
    {
        Assert.That(() => NotifyCollectionChangedExtensions.GetWeakCollectionChangedObservable(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void WeakSubscribe_Handler_NullCollection_Throws()
    {
        Assert.That(() => ((INotifyCollectionChanged)null!).WeakSubscribe((NotifyCollectionChangedEventHandler)((_, _) => { })),
            Throws.ArgumentNullException);
    }

    [Test]
    public void WeakSubscribe_Action_NullCollection_Throws()
    {
        Assert.That(() => ((INotifyCollectionChanged)null!).WeakSubscribe((Action<NotifyCollectionChangedEventArgs>)(_ => { })),
            Throws.ArgumentNullException);
    }

    [Test]
    public void WeakSubscribe_Handler_NullHandler_Throws()
    {
        var list = new CoreList<int>();

        Assert.That(() => list.WeakSubscribe((NotifyCollectionChangedEventHandler)null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void WeakSubscribe_Action_NullHandler_Throws()
    {
        var list = new CoreList<int>();

        Assert.That(() => list.WeakSubscribe((Action<NotifyCollectionChangedEventArgs>)null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void WeakSubscribe_Handler_ReceivesNotifications()
    {
        var list = new CoreList<int>();
        var actions = new List<NotifyCollectionChangedAction>();

        using IDisposable sub = list.WeakSubscribe((sender, args) => actions.Add(args.Action));

        list.Add(1);
        list.Add(2);
        list.RemoveAt(0);

        Assert.That(actions, Is.EqualTo(new[]
        {
            NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Remove,
        }));
    }

    [Test]
    public void WeakSubscribe_Action_ReceivesNotifications()
    {
        var list = new CoreList<int>();
        int count = 0;

        using IDisposable sub = list.WeakSubscribe(_ => count++);

        list.Add(99);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void WeakSubscribe_Dispose_StopsNotifications()
    {
        var list = new CoreList<int>();
        int count = 0;

        IDisposable sub = list.WeakSubscribe(_ => count++);
        list.Add(1);
        sub.Dispose();
        list.Add(2);

        Assert.That(count, Is.EqualTo(1));
    }
}
