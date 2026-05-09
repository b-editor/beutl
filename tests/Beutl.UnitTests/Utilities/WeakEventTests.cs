using System.ComponentModel;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class WeakEventTests
{
    private sealed class Source : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise(string property)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private sealed class Subscriber : IWeakEventSubscriber<PropertyChangedEventArgs>
    {
        public List<string?> Received { get; } = [];

        public void OnEvent(object? sender, WeakEvent ev, PropertyChangedEventArgs e)
        {
            Received.Add(e.PropertyName);
        }
    }

    [Test]
    public void Subscribe_ReceivesEvent()
    {
        var src = new Source();
        var sub = new Subscriber();

        WeakEvents.PropertyChanged.Subscribe(src, sub);
        src.Raise("X");

        Assert.That(sub.Received, Is.EqualTo(new[] { "X" }));
    }

    [Test]
    public void Unsubscribe_StopsReceivingFurtherEvents()
    {
        var src = new Source();
        var sub = new Subscriber();

        WeakEvents.PropertyChanged.Subscribe(src, sub);
        src.Raise("A");

        WeakEvents.PropertyChanged.Unsubscribe(src, sub);
        src.Raise("B");

        Assert.That(sub.Received, Is.EqualTo(new[] { "A" }));
    }

    [Test]
    public void MultipleSubscribers_AllReceiveEvent()
    {
        var src = new Source();
        var s1 = new Subscriber();
        var s2 = new Subscriber();

        WeakEvents.PropertyChanged.Subscribe(src, s1);
        WeakEvents.PropertyChanged.Subscribe(src, s2);

        src.Raise("hello");

        Assert.That(s1.Received, Is.EqualTo(new[] { "hello" }));
        Assert.That(s2.Received, Is.EqualTo(new[] { "hello" }));
    }

    [Test]
    public void Register_ActionStyle_AlsoWorks()
    {
        WeakEvent<Source, PropertyChangedEventArgs> ev = WeakEvent.Register<Source, PropertyChangedEventArgs>(
            (s, h) => s.PropertyChanged += new PropertyChangedEventHandler(h),
            (s, h) => s.PropertyChanged -= new PropertyChangedEventHandler(h));

        var src = new Source();
        var sub = new Subscriber();
        ev.Subscribe(src, sub);
        src.Raise("x");

        Assert.That(sub.Received, Is.EqualTo(new[] { "x" }));
    }
}
