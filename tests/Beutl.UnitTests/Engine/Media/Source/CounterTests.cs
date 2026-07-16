using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Media.Source;

public class CounterTests
{
    private sealed class Fake : IDisposable
    {
        public int DisposeCount;
        public Action? OnDispose;

        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
            OnDispose?.Invoke();
        }
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
        var fake = new Fake();
        var counter = new Counter<Fake>(fake, null);

        Assert.That(counter.TryAddRef(), Is.True);
        Assert.That(counter.RefCount, Is.EqualTo(2));
        // Contract used by VideoSource/ImageSource/SoundSource: a successful
        // TryAddRef must guarantee Value is safe to read until matching Release.
        Assert.That(counter.Value, Is.SameAs(fake));
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
    public void OnRelease_InvokedBeforeUnderlyingDispose()
    {
        var order = new List<string>();
        var fake = new Fake();
        fake.OnDispose = () => { lock (order) order.Add("dispose"); };
        var counter = new Counter<Fake>(fake, () => { lock (order) order.Add("onRelease"); });

        counter.Release();

        Assert.That(order, Is.EqualTo(new[] { "onRelease", "dispose" }));
    }

    [Test]
    public void OnReleaseFailure_StillDisposesValueAndPreservesFailure()
    {
        var failure = new InvalidOperationException("release callback failed");
        var fake = new Fake();
        var counter = new Counter<Fake>(fake, () => throw failure);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(counter.Release);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(fake.DisposeCount, Is.EqualTo(1));
            Assert.That(counter.RefCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => _ = counter.Value);
        });
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
        // Two threads race against each other through a Barrier so both
        // actually contend on the same Counter:
        //  - consumer: TryAddRef -> read Value -> Release (the call-site
        //    pattern in VideoSource/ImageSource/SoundSource)
        //  - releaser: drops the initial reference at an unpredictable
        //    point during the consumer's loop, forcing the TOCTOU window
        //    the volatile+CAS implementation could not close.
        const int Trials = 200;
        const int ConsumerIterations = 5_000;
        int totalTryAddRefSuccesses = 0;

        for (int trial = 0; trial < Trials; trial++)
        {
            var fake = new Fake();
            var counter = new Counter<Fake>(fake, null);

            var barrier = new Barrier(2);
            int observedDisposed = 0;
            int tryAddRefSuccesses = 0;

            var consumer = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < ConsumerIterations; i++)
                {
                    if (!counter.TryAddRef())
                    {
                        continue;
                    }

                    Interlocked.Increment(ref tryAddRefSuccesses);
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
                barrier.SignalAndWait();
                // Yield once so the consumer typically reaches its TryAddRef
                // loop before the unbalanced Release lands. This keeps the
                // TOCTOU window observable on schedulers that would otherwise
                // let the releaser win the lock first on every trial.
                Thread.Yield();
                counter.Release();
            });

            Task.WaitAll(consumer, releaser);

            Assert.That(observedDisposed, Is.Zero, $"Trial {trial}: TryAddRef succeeded but observed disposed value");
            Assert.That(fake.DisposeCount, Is.EqualTo(1), $"Trial {trial}: value not disposed exactly once");
            totalTryAddRefSuccesses += tryAddRefSuccesses;
        }

        // Sanity guard that the race actually exercised the TryAddRef path
        // rather than the consumer always losing to the unbalanced Release
        // (which would make the assertions above pass vacuously). How many
        // hits land is a pure scheduling artifact — the consumer's head start
        // depends on thread-pool injection timing and core count, so it swings
        // from a handful to hundreds of thousands across machines. Requiring a
        // proportional threshold here couples the test to the scheduler and is
        // flaky on warm-pool / low-core CI runners; one success across the
        // Trials x ConsumerIterations attempts is enough to prove the window
        // is reachable.
        Assert.That(totalTryAddRefSuccesses, Is.GreaterThan(0),
            $"Race window collapsed: no TryAddRef hits across {Trials} trials ({(long)Trials * ConsumerIterations} attempts)");
    }
}
