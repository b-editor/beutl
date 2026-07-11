using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyGeneratorRegistryTests
{
    // Registry is static global state; track registrations so TearDown restores it.
    private readonly List<IProxyGeneratorFactory> _registered = [];

    [TearDown]
    public void TearDown()
    {
        foreach (IProxyGeneratorFactory factory in _registered)
            ProxyGeneratorRegistry.Unregister(factory);

        _registered.Clear();
    }

    [Test]
    public void Register_ThenEnumerate_ReturnsFactoryInOrder()
    {
        var first = new StubFactory();
        var second = new StubFactory();
        _registered.Add(first);
        _registered.Add(second);

        ProxyGeneratorRegistry.Register(first);
        ProxyGeneratorRegistry.Register(second);

        IReadOnlyList<IProxyGeneratorFactory> snapshot = ProxyGeneratorRegistry.Enumerate();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Does.Contain(first));
            Assert.That(snapshot, Does.Contain(second));
            Assert.That(
                Array.IndexOf([.. snapshot], first),
                Is.LessThan(Array.IndexOf([.. snapshot], second)),
                "registration order must be preserved");
        });
    }

    [Test]
    public void Unregister_RemovesFactory()
    {
        var factory = new StubFactory();
        _registered.Add(factory);
        ProxyGeneratorRegistry.Register(factory);

        bool removed = ProxyGeneratorRegistry.Unregister(factory);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(ProxyGeneratorRegistry.Enumerate(), Does.Not.Contain(factory));
        });

        // Don't double-unregister in TearDown.
        _registered.Remove(factory);
    }

    [Test]
    public void Enumerate_SnapshotIsImmutable()
    {
        var first = new StubFactory();
        _registered.Add(first);
        ProxyGeneratorRegistry.Register(first);

        IReadOnlyList<IProxyGeneratorFactory> snapshot = ProxyGeneratorRegistry.Enumerate();
        int snapshotCountBefore = snapshot.Count;

        var second = new StubFactory();
        _registered.Add(second);
        ProxyGeneratorRegistry.Register(second);

        IReadOnlyList<IProxyGeneratorFactory> snapshotAfter = ProxyGeneratorRegistry.Enumerate();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Count, Is.EqualTo(snapshotCountBefore), "the earlier snapshot must not mutate");
            Assert.That(snapshotAfter.Count, Is.EqualTo(snapshotCountBefore + 1), "a fresh enumeration reflects the new registration");
            Assert.That(snapshotAfter, Does.Contain(second));
            Assert.That(snapshot, Does.Not.Contain(second));
        });
    }

    [Test]
    public void Changed_FiresOnRegisterAndOnEffectiveUnregister()
    {
        var factory = new StubFactory();
        _registered.Add(factory);
        int changed = 0;
        void Handler(object? sender, EventArgs e) => Interlocked.Increment(ref changed);
        ProxyGeneratorRegistry.Changed += Handler;
        try
        {
            ProxyGeneratorRegistry.Register(factory);
            int afterRegister = changed;

            bool removed = ProxyGeneratorRegistry.Unregister(factory);
            int afterUnregister = changed;
            _registered.Remove(factory);

            // Unregistering something not registered must not fire Changed (no effective change).
            bool removedAgain = ProxyGeneratorRegistry.Unregister(factory);

            Assert.Multiple(() =>
            {
                Assert.That(afterRegister, Is.EqualTo(1), "Register must raise Changed");
                Assert.That(removed, Is.True);
                Assert.That(afterUnregister, Is.EqualTo(2), "an effective Unregister must raise Changed");
                Assert.That(removedAgain, Is.False);
                Assert.That(changed, Is.EqualTo(2), "a no-op Unregister must not raise Changed");
            });
        }
        finally
        {
            ProxyGeneratorRegistry.Changed -= Handler;
        }
    }

    [Test]
    public void Changed_ThrowingSubscriber_DoesNotAbortRegistryOrStarveOtherSubscribers()
    {
        var factory = new StubFactory();
        _registered.Add(factory);
        int goodRuns = 0;
        void Throwing(object? sender, EventArgs e) => throw new InvalidOperationException("bad subscriber");
        void Good(object? sender, EventArgs e) => Interlocked.Increment(ref goodRuns);
        ProxyGeneratorRegistry.Changed += Throwing;
        ProxyGeneratorRegistry.Changed += Good;
        try
        {
            Assert.DoesNotThrow(() => ProxyGeneratorRegistry.Register(factory),
                "a throwing Changed subscriber must not abort the registry mutation");

            Assert.Multiple(() =>
            {
                Assert.That(goodRuns, Is.EqualTo(1), "the second subscriber must still run after the first throws");
                Assert.That(ProxyGeneratorRegistry.Enumerate(), Does.Contain(factory));
            });

            Assert.DoesNotThrow(() => ProxyGeneratorRegistry.Unregister(factory));
            _registered.Remove(factory);
            Assert.That(goodRuns, Is.EqualTo(2), "Unregister must also isolate the throwing subscriber");
        }
        finally
        {
            ProxyGeneratorRegistry.Changed -= Throwing;
            ProxyGeneratorRegistry.Changed -= Good;
        }
    }

    private sealed class StubFactory : IProxyGeneratorFactory
    {
        public IProxyGenerator Create(IProxyStore store)
            => new StubGenerator();
    }

    private sealed class StubGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
            => ValueTask.CompletedTask;
    }
}
