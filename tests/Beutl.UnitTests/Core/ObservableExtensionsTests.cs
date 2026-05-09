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

    [Test]
    public void CombineWithPrevious_WithSelector_PassesPreviousAndCurrent()
    {
        var source = new Subject<int>();
        var diffs = new List<int>();

        using IDisposable sub = source
            .CombineWithPrevious((prev, curr) => curr - prev)
            .Subscribe(diffs.Add);

        source.OnNext(10);
        source.OnNext(15);
        source.OnNext(7);

        Assert.That(diffs, Is.EqualTo(new[] { 10, 5, -8 }));
    }

    [Test]
    public void CombineWithPrevious_FirstValueHasDefaultPrevious()
    {
        var source = new Subject<string>();
        var pairs = new List<(string? Old, string? New)>();

        using IDisposable sub = source.CombineWithPrevious().Subscribe(p => pairs.Add(p));

        source.OnNext("first");

        Assert.That(pairs, Has.Count.EqualTo(1));
        Assert.That(pairs[0].Old, Is.Null);
        Assert.That(pairs[0].New, Is.EqualTo("first"));
    }
}
