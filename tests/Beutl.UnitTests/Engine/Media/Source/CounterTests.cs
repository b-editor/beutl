using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Media.Source;

public class CounterTests
{
    private sealed class Fake : IDisposable
    {
        public int DisposeCount;

        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    [Test]
    public void Initial_RefCount_IsOne()
    {
        var counter = new Counter<Fake>(new Fake(), null);

        Assert.That(counter.RefCount, Is.EqualTo(1));
    }

    [Test]
    public void AddRef_IncrementsRefCount()
    {
        var counter = new Counter<Fake>(new Fake(), null);

        counter.AddRef();
        counter.AddRef();

        Assert.That(counter.RefCount, Is.EqualTo(3));
    }

    [Test]
    public void Release_DisposesValue_WhenRefCountReachesZero()
    {
        var fake = new Fake();
        var counter = new Counter<Fake>(fake, null);

        counter.AddRef();
        counter.Release();
        Assert.That(fake.DisposeCount, Is.EqualTo(0));

        counter.Release();
        Assert.That(fake.DisposeCount, Is.EqualTo(1));
        Assert.That(counter.RefCount, Is.EqualTo(0));
    }

    [Test]
    public void AddRef_AfterFullyReleased_Throws()
    {
        var counter = new Counter<Fake>(new Fake(), null);
        counter.Release();

        Assert.Throws<ObjectDisposedException>(() => counter.AddRef());
    }

    [Test]
    public void TryAddRef_AfterFullyReleased_ReturnsFalse()
    {
        var counter = new Counter<Fake>(new Fake(), null);
        counter.Release();

        Assert.That(counter.TryAddRef(), Is.False);
    }

    [Test]
    public void TryAddRef_WhileAlive_ReturnsTrueAndIncrementsRefCount()
    {
        var counter = new Counter<Fake>(new Fake(), null);

        Assert.That(counter.TryAddRef(), Is.True);
        Assert.That(counter.RefCount, Is.EqualTo(2));
    }

    [Test]
    public void Value_AfterFullyReleased_Throws()
    {
        var counter = new Counter<Fake>(new Fake(), null);
        counter.Release();

        Assert.Throws<ObjectDisposedException>(() => _ = counter.Value);
    }

    [Test]
    public void Release_PastZero_IsNoop()
    {
        var fake = new Fake();
        var counter = new Counter<Fake>(fake, null);

        counter.Release();
        counter.Release();
        counter.Release();

        Assert.That(fake.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void OnRelease_InvokedExactlyOnce()
    {
        int callCount = 0;
        var counter = new Counter<Fake>(new Fake(), () => Interlocked.Increment(ref callCount));

        counter.AddRef();
        counter.Release();
        Assert.That(callCount, Is.EqualTo(0));

        counter.Release();
        counter.Release(); // noop

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ParallelAddRefRelease_DoesNotLoseRef()
    {
        const int Threads = 8;
        const int Iterations = 5_000;

        var fake = new Fake();
        var counter = new Counter<Fake>(fake, null);

        await Task.WhenAll(Enumerable.Range(0, Threads).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < Iterations; i++)
            {
                counter.AddRef();
                counter.Release();
            }
        })));

        Assert.That(counter.RefCount, Is.EqualTo(1));
        Assert.That(fake.DisposeCount, Is.EqualTo(0));

        counter.Release();
        Assert.That(fake.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void ParallelTryAddRefRace_NeverObservesDisposedValue()
    {
        const int Trials = 20;

        for (int trial = 0; trial < Trials; trial++)
        {
            var fake = new Fake();
            var counter = new Counter<Fake>(fake, null);

            var ready = new ManualResetEventSlim();
            int observedDisposed = 0;

            var consumer = Task.Run(() =>
            {
                ready.Wait();
                for (int i = 0; i < 2_000; i++)
                {
                    if (!counter.TryAddRef())
                    {
                        break;
                    }

                    try
                    {
                        Fake value = counter.Value;
                        if (Volatile.Read(ref value.DisposeCount) != 0)
                        {
                            Interlocked.Increment(ref observedDisposed);
                        }
                    }
                    finally
                    {
                        counter.Release();
                    }
                }
            });

            var releaser = Task.Run(() =>
            {
                ready.Wait();
                Thread.Yield();
                counter.Release();
            });

            ready.Set();
            Task.WaitAll(consumer, releaser);

            Assert.That(observedDisposed, Is.Zero, $"Trial {trial}: TryAddRef succeeded but observed disposed value");
            Assert.That(fake.DisposeCount, Is.EqualTo(1), $"Trial {trial}: value not disposed exactly once");
        }
    }
}
