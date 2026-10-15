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
    public void DisposeWith_WithCompositeDisposable_DisposesWhenContainerDisposed()
    {
        var composite = new CompositeDisposable();
        var disposable = new CountedDisposable();

        disposable.DisposeWith((ICollection<IDisposable>)composite);
        composite.Dispose();

        Assert.That(disposable.DisposeCount, Is.EqualTo(1));
    }
}
