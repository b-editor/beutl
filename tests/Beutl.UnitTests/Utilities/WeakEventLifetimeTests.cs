using System.Runtime.CompilerServices;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

[TestFixture]
public class WeakEventLifetimeTests
{
    [Test]
    public void CollectedSubscriber_DetachesSourceHandlerOnNextEvent()
    {
        var source = new EventSource();
        var weakEvent = CreateEvent();
        WeakReference subscriber = SubscribeTemporary(source, weakEvent);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(subscriber.IsAlive, Is.False);

        source.Raise();

        Assert.That(source.HandlerCount, Is.Zero);
        GC.KeepAlive(weakEvent);
    }

    [Test]
    public void UnsubscribeLastLiveSubscriber_RemovesCollectedSubscribersToo()
    {
        var source = new EventSource();
        var weakEvent = CreateEvent();
        WeakReference temporary = SubscribeTemporary(source, weakEvent);
        var survivor = new Subscriber();
        weakEvent.Subscribe(source, survivor);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(temporary.IsAlive, Is.False);

        weakEvent.Unsubscribe(source, survivor);

        Assert.That(source.HandlerCount, Is.Zero);
    }

    [Test]
    public void UnsubscribeDuringEvent_DoesNotSkipFollowingSubscriber()
    {
        var source = new EventSource();
        var weakEvent = CreateEvent();
        var first = new Subscriber();
        var second = new Subscriber();
        first.Callback = () => weakEvent.Unsubscribe(source, first);
        weakEvent.Subscribe(source, first);
        weakEvent.Subscribe(source, second);

        source.Raise();
        source.Raise();

        Assert.Multiple(() =>
        {
            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(second.CallCount, Is.EqualTo(2));
        });
        GC.KeepAlive(second);
    }

    [Test]
    public void UnsubscribeDuringNestedEvent_DoesNotSkipFollowingSubscriber()
    {
        var source = new EventSource();
        var weakEvent = CreateEvent();
        var first = new Subscriber();
        var second = new Subscriber();
        first.Callback = () =>
        {
            if (first.CallCount == 1)
                source.Raise();
            else
                weakEvent.Unsubscribe(source, first);
        };
        weakEvent.Subscribe(source, first);
        weakEvent.Subscribe(source, second);

        source.Raise();
        source.Raise();

        Assert.Multiple(() =>
        {
            Assert.That(first.CallCount, Is.EqualTo(2));
            Assert.That(second.CallCount, Is.EqualTo(3));
            Assert.That(source.HandlerCount, Is.EqualTo(1));
        });
        GC.KeepAlive(second);
    }

    [Test]
    public void UnsubscribeAndThrowDuringEvent_DetachesSourceHandler()
    {
        var source = new EventSource();
        var weakEvent = CreateEvent();
        var subscriber = new Subscriber();
        var failure = new InvalidOperationException("Subscriber failed.");
        subscriber.Callback = () =>
        {
            weakEvent.Unsubscribe(source, subscriber);
            throw failure;
        };
        weakEvent.Subscribe(source, subscriber);

        Assert.That(Assert.Throws<InvalidOperationException>(source.Raise), Is.SameAs(failure));
        Assert.That(source.HandlerCount, Is.Zero);
    }

    [Test]
    public void ThrowDuringEvent_DoesNotPreventLaterUnsubscribe()
    {
        var source = new EventSource();
        var weakEvent = CreateEvent();
        var subscriber = new Subscriber { Callback = () => throw new InvalidOperationException() };
        weakEvent.Subscribe(source, subscriber);

        Assert.Throws<InvalidOperationException>(source.Raise);
        weakEvent.Unsubscribe(source, subscriber);

        Assert.That(source.HandlerCount, Is.Zero);
    }

    private static WeakEvent<EventSource, EventArgs> CreateEvent()
        => WeakEvent.Register<EventSource, EventArgs>(
            (source, handler) => source.Changed += handler,
            (source, handler) => source.Changed -= handler);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeTemporary(EventSource source, WeakEvent<EventSource, EventArgs> weakEvent)
    {
        var subscriber = new Subscriber();
        weakEvent.Subscribe(source, subscriber);
        return new WeakReference(subscriber);
    }

    private sealed class EventSource
    {
        public event EventHandler<EventArgs>? Changed;
        public int HandlerCount => Changed?.GetInvocationList().Length ?? 0;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Subscriber : IWeakEventSubscriber<EventArgs>
    {
        public int CallCount { get; private set; }
        public Action? Callback { get; set; }

        public void OnEvent(object? sender, WeakEvent ev, EventArgs e)
        {
            CallCount++;
            Callback?.Invoke();
        }
    }
}
