using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderResourceOwnershipTests
{
    [Test]
    public void OwnedRegistration_TransfersToRequestAndDischargesExactlyOnce()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();

        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value, "owned", version: 4);
        registry.Commit(resource);

        Assert.Multiple(() =>
        {
            Assert.That(resource.CacheIdentity, Is.EqualTo(new RenderResourceIdentity("owned", 4)));
            Assert.That(resource.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.RequestOwned));
            Assert.That(registry.Use(resource, static item => item), Is.SameAs(value));
            Assert.That(value.DisposeCount, Is.Zero);
        });

        registry.Release(resource);
        registry.Release(resource);

        Assert.Multiple(() =>
        {
            Assert.That(value.DisposeCount, Is.EqualTo(1));
            Assert.That(resource.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.Discharged));
            Assert.That(() => _ = resource.CacheIdentity, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = resource.SlotIdentity, Throws.TypeOf<InvalidOperationException>(),
                "A released public token must not retain the request slot or raw resource.");
            Assert.That(() => registry.Use(resource, static item => item), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void DuplicateOwnedAndOwnedBorrowedConflicts_AreRejectedBeforeAnotherTransfer()
    {
        var ownedValue = new TrackedDisposable();
        using var ownedRegistry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> owned = ownedRegistry.RegisterOwned(ownedValue);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ownedRegistry.RegisterOwned(ownedValue),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => ownedRegistry.RegisterBorrowed(ownedValue, "borrow", 0),
                Throws.TypeOf<InvalidOperationException>());
        });

        ownedRegistry.Rollback(owned);
        Assert.That(ownedValue.DisposeCount, Is.EqualTo(1));

        var borrowedValue = new TrackedDisposable();
        using var borrowedRegistry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> borrowed = borrowedRegistry.RegisterBorrowed(borrowedValue, "borrow", 0);

        Assert.That(
            () => borrowedRegistry.RegisterOwned(borrowedValue),
            Throws.TypeOf<InvalidOperationException>());

        borrowedRegistry.Rollback(borrowed);
        Assert.That(borrowedValue.DisposeCount, Is.Zero);
    }

    [Test]
    public void RolledBackOwnership_RemainsATombstoneForTheRequestFamily()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value);
        registry.Rollback(resource);

        Assert.Multiple(() =>
        {
            Assert.That(value.DisposeCount, Is.EqualTo(1));
            Assert.That(() => registry.RegisterOwned(value), Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => registry.RegisterBorrowed(value, "disposed", 0),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void RolledBackBorrow_RemainsATombstoneForTheRequestFamily()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterBorrowed(value);
        registry.Rollback(resource);

        Assert.Multiple(() =>
        {
            Assert.That(value.DisposeCount, Is.Zero);
            Assert.That(() => registry.RegisterOwned(value), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void FinalRelease_DuringUseIsRejectedBeforeOwnershipMutation()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value);
        registry.Commit(resource);

        Assert.That(
            () => registry.Use(resource, _ =>
            {
                registry.Release(resource);
                return 0;
            }),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(resource.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Committed));
            Assert.That(resource.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.RequestOwned));
            Assert.That(registry.Slots, Has.Count.EqualTo(1));
            Assert.That(value.DisposeCount, Is.Zero);
        });

        registry.Release(resource);
        Assert.That(value.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void ExplicitBorrowIdentity_CoalescesOnlyForMatchingKeyAndVersion()
    {
        var value = new object();
        using var registry = new RenderRequestResourceRegistry();

        RenderResource<object> first = registry.RegisterBorrowed(value, "stable", version: 7);
        RenderResource<object> second = registry.RegisterBorrowed(value, "stable", version: 7);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.SlotIdentity, Is.SameAs(first.SlotIdentity));
            Assert.That(second.CacheIdentity, Is.EqualTo(first.CacheIdentity));
            Assert.That(
                () => registry.RegisterBorrowed(value, "different", version: 7),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => registry.RegisterBorrowed(value, "stable", version: 8),
                Throws.TypeOf<InvalidOperationException>());
        });

        registry.Commit(first);
        registry.Commit(second);
        registry.Release(first);

        Assert.That(registry.Use(second, static item => item), Is.SameAs(value));

        registry.Release(second);
        Assert.That(second.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.ReleasedToken));
    }

    [Test]
    public void NullBorrowKeys_CreateDistinctRequestLocalIdentities()
    {
        var value = new object();
        using var firstRegistry = new RenderRequestResourceRegistry();
        using var secondRegistry = new RenderRequestResourceRegistry();

        RenderResource<object> first = firstRegistry.RegisterBorrowed(value);
        RenderResource<object> second = firstRegistry.RegisterBorrowed(value);
        RenderResource<object> otherRequest = secondRegistry.RegisterBorrowed(value);

        Assert.Multiple(() =>
        {
            Assert.That(first.SlotIdentity, Is.Not.SameAs(second.SlotIdentity));
            Assert.That(first.CacheIdentity.Key, Is.Not.Null);
            Assert.That(first.CacheIdentity, Is.Not.EqualTo(second.CacheIdentity));
            Assert.That(first.CacheIdentity, Is.Not.EqualTo(otherRequest.CacheIdentity));
        });
    }

    [Test]
    public void OwnedResource_CanTransferToPersistentCacheWithoutRequestDisposal()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value, "cache", 1);
        registry.Commit(resource);

        TrackedDisposable transferred = registry.TransferOwned(resource);
        registry.Release(resource);

        Assert.Multiple(() =>
        {
            Assert.That(transferred, Is.SameAs(value));
            Assert.That(value.DisposeCount, Is.Zero);
            Assert.That(resource.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.Discharged));
            Assert.That(() => _ = resource.SlotIdentity, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => registry.TransferOwned(resource), Throws.TypeOf<InvalidOperationException>());
        });

        transferred.Dispose();
        Assert.That(value.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void RuntimeIdentity_RejectsDefaultWhenValidated()
    {
        var identity = new RenderRuntimeIdentity(("frame", 3));

        Assert.Multiple(() =>
        {
            Assert.That(identity.Key, Is.EqualTo(("frame", 3)));
            Assert.That(() => identity.ThrowIfUninitialized("identity"), Throws.Nothing);
            Assert.That(
                () => default(RenderRuntimeIdentity).ThrowIfUninitialized("identity"),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("identity"));
        });
    }

    [Test]
    public void Registration_RejectsKeysThatCannotBeRetainedSafely()
    {
        int captured = 1;
        Func<int> closure = () => captured;
        var ownedValue = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => registry.RegisterOwned(ownedValue, ownedValue),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => registry.RegisterBorrowed(new object(), new byte[4]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => registry.RegisterBorrowed(new object(), closure),
                Throws.TypeOf<ArgumentException>());
            Assert.That(ownedValue.DisposeCount, Is.Zero);
        });
    }

    private sealed class TrackedDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
