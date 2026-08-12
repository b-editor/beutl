using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderResourceOwnershipTests
{

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
                () => ownedRegistry.RegisterBorrowed(ownedValue),
                Throws.TypeOf<InvalidOperationException>());
        });

        ownedRegistry.Rollback(owned);
        Assert.That(ownedValue.DisposeCount, Is.EqualTo(1));

        var borrowedValue = new TrackedDisposable();
        using var borrowedRegistry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> borrowed = borrowedRegistry.RegisterBorrowed(borrowedValue);

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
                () => registry.RegisterBorrowed(value),
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
    public void OwnedResource_CanTransferToPersistentCacheWithoutRequestDisposal()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value);
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


    private sealed class TrackedDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
