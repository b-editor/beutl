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
