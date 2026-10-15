using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Reactive;

namespace Beutl.UnitTests.Core;

public class ObservableExtensionsTests
{
    [Test]
    public void ReturnThenNever_PublishesValueOnceAndStaysSubscribed()
    {
        IObservable<int> obs = Observable.ReturnThenNever(42);
        var values = new List<int>();
        bool completed = false;

        using IDisposable sub = obs.Subscribe(values.Add, () => completed = true);

        Assert.That(values, Is.EqualTo(new[] { 42 }));
        Assert.That(completed, Is.False);
    }

    [Test]
    public void CombineWithPrevious_EmitsTuplePerValue()
    {
        var source = new Subject<int>();
        var pairs = new List<(int? Old, int? New)>();

        using IDisposable sub = source.CombineWithPrevious().Subscribe(p => pairs.Add(p));

        source.OnNext(1);
        source.OnNext(2);
        source.OnNext(3);

        Assert.That(pairs[0], Is.EqualTo(((int?)0, (int?)1)));
        Assert.That(pairs[1], Is.EqualTo(((int?)1, (int?)2)));
        Assert.That(pairs[2], Is.EqualTo(((int?)2, (int?)3)));
    }
}
