using System.Reactive.Disposables;
using Beutl.Reactive;

namespace Beutl.UnitTests.Core;

public class DisposableExtensionTests
{
    private sealed class CountedDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    [Test]
    public void DisposeWith_AddsToCollectionAndReturnsSelf()
    {
        var list = new List<IDisposable>();
        var disposable = new CountedDisposable();

        CountedDisposable returned = disposable.DisposeWith(list);

        Assert.That(returned, Is.SameAs(disposable));
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0], Is.SameAs(disposable));
    }

    [Test]
    public void DisposeAll_Tuple2_DisposesBothItems()
    {
        var a = new CountedDisposable();
        var b = new CountedDisposable();

        (a, b).DisposeAll();

        Assert.That(a.DisposeCount, Is.EqualTo(1));
        Assert.That(b.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void DisposeAll_Tuple2_NullsAreSkipped()
    {
        ValueTuple<CountedDisposable?, CountedDisposable?> tuple = (null, null);

        Assert.That(() => tuple.DisposeAll(), Throws.Nothing);
    }

    [Test]
    public void DisposeAll_Tuple3_DisposesAll()
    {
        var a = new CountedDisposable();
        var b = new CountedDisposable();
        var c = new CountedDisposable();

        ((a, b, c)!).DisposeAll();

        Assert.That(a.DisposeCount, Is.EqualTo(1));
        Assert.That(b.DisposeCount, Is.EqualTo(1));
        Assert.That(c.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void DisposeAll_Tuple4_DisposesAll()
    {
        var items = Enumerable.Range(0, 4).Select(_ => new CountedDisposable()).ToArray();

        ValueTuple<CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?> tuple
            = (items[0], items[1], items[2], items[3]);

        tuple.DisposeAll();

        Assert.That(items.Select(d => d.DisposeCount), Is.All.EqualTo(1));
    }

    [Test]
    public void DisposeAll_Tuple5_DisposesAll()
    {
        var items = Enumerable.Range(0, 5).Select(_ => new CountedDisposable()).ToArray();

        ValueTuple<CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?> tuple
            = (items[0], items[1], items[2], items[3], items[4]);

        tuple.DisposeAll();

        Assert.That(items.Select(d => d.DisposeCount), Is.All.EqualTo(1));
    }

    [Test]
    public void DisposeAll_Tuple6_DisposesAll()
    {
        var items = Enumerable.Range(0, 6).Select(_ => new CountedDisposable()).ToArray();

        ValueTuple<CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?> tuple
            = (items[0], items[1], items[2], items[3], items[4], items[5]);

        tuple.DisposeAll();

        Assert.That(items.Select(d => d.DisposeCount), Is.All.EqualTo(1));
    }

    [Test]
    public void DisposeAll_Tuple7_DisposesAll()
    {
        var items = Enumerable.Range(0, 7).Select(_ => new CountedDisposable()).ToArray();

        ValueTuple<CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?, CountedDisposable?> tuple
            = (items[0], items[1], items[2], items[3], items[4], items[5], items[6]);

        tuple.DisposeAll();

        Assert.That(items.Select(d => d.DisposeCount), Is.All.EqualTo(1));
    }

    [Test]
    public void DisposeWith_WithCompositeDisposable_DisposesWhenContainerDisposed()
    {
        var composite = new CompositeDisposable();
        var disposable = new CountedDisposable();

        disposable.DisposeWith((ICollection<IDisposable>)composite);
        composite.Dispose();

        Assert.That(disposable.DisposeCount, Is.EqualTo(1));
    }
}
