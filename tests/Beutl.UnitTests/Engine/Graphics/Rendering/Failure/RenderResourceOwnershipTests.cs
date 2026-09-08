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
            Assert.That(registry.ActiveResourceCount, Is.EqualTo(1));
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
            Assert.That(resource.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(resource.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.Discharged));
            Assert.That(
                () => registry.Use(resource, static _ => true),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not committed"));
            Assert.That(() => registry.TransferOwned(resource), Throws.TypeOf<InvalidOperationException>());
        });

        transferred.Dispose();
        Assert.That(value.DisposeCount, Is.EqualTo(1));
    }


    // A pending registration can still be rolled back, so it stays unreadable from everywhere except the
    // recording that owns it - the only reader whose own work a rollback would discard as well.
    [Test]
    public void PendingResource_IsReadableOnlyFromTheRecordingThatOwnsIt()
    {
        var owner = new RecordingScopeStub();
        var other = new RecordingScopeStub();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource =
            registry.RegisterBorrowed(new TrackedDisposable(), owner);
        RenderResource<TrackedDisposable> unscoped = registry.RegisterBorrowed(new TrackedDisposable());

        Assert.Multiple(() =>
        {
            Assert.That(Read(registry, resource), Is.True, "the owning recording reads its own registration");
            Assert.That(
                () => Read(registry, unscoped),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not committed"),
                "a registration no recording owns is unreadable while pending");
        });

        owner.IsRecording = false;
        other.IsRecording = true;

        Assert.That(
            () => Read(registry, resource),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not committed"),
            "another recording cannot read it, and neither can the owner once it has ended");

        registry.Commit(resource);
        Assert.That(Read(registry, resource), Is.True, "a committed registration is readable regardless");
    }

    [Test]
    public void PendingResource_IsUnreadableOnceItsRegistrationRollsBack()
    {
        var owner = new RecordingScopeStub();
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value, owner);

        Assert.That(Read(registry, resource), Is.True);

        registry.Rollback(resource);

        Assert.Multiple(() =>
        {
            Assert.That(value.DisposeCount, Is.EqualTo(1), "the read did not keep the rollback from running");
            Assert.That(
                () => Read(registry, resource),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not committed"),
                "the recording is still live, but the registration it rolled back is gone");
        });
    }

    // Reading is the only thing a pending registration allows. Taking the raw value out of the request is a
    // mutation, and a rollback would have to undo it, so it still requires a committed registration.
    [Test]
    public void PendingResource_StillRefusesToTransferOwnership()
    {
        var owner = new RecordingScopeStub();
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterOwned(value, owner);

        Assert.Multiple(() =>
        {
            Assert.That(Read(registry, resource), Is.True);
            Assert.That(
                () => registry.TransferOwned(resource),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not committed"));
        });
    }

    // One composable definition can reach the same declared resource twice: the outer lease owns the slot
    // state and the inner one reads through it. A pending read has to compose the same way and hand the
    // slot back to its pending state, or the recording could no longer roll the registration back.
    [Test]
    public void PendingResource_ReadsReentrantlyAndReturnsItsLease()
    {
        var owner = new RecordingScopeStub();
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> resource = registry.RegisterBorrowed(value, owner);

        RenderResourceOwnershipState innerState = registry.Use(
            resource,
            _ => registry.Use(resource, _ => resource.OwnershipState));

        Assert.Multiple(() =>
        {
            Assert.That(innerState, Is.EqualTo(RenderResourceOwnershipState.LeasedToCallback));
            Assert.That(
                resource.OwnershipState,
                Is.EqualTo(RenderResourceOwnershipState.BorrowedPending),
                "the outermost lease returned the slot to the pending state it borrowed it from");
        });

        registry.Rollback(resource);
        Assert.Multiple(() =>
        {
            Assert.That(resource.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(value.DisposeCount, Is.Zero, "a borrowed resource is never disposed by the request");
        });
    }

    [Test]
    public void MultipleBorrowsOfTheSameRawValue_HaveIndependentLifecycles()
    {
        var value = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<TrackedDisposable> first = registry.RegisterBorrowed(value);
        RenderResource<TrackedDisposable> second = registry.RegisterBorrowed(value);

        Assert.That(first, Is.Not.SameAs(second));
        registry.Commit(first);
        registry.Commit(second);

        bool releasedSecond = registry.Use(first, current =>
        {
            Assert.That(current, Is.SameAs(value));
            Assert.That(first.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.LeasedToCallback));
            registry.Release(second);
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(releasedSecond, Is.True);
            Assert.That(first.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.RequestBorrowed));
            Assert.That(second.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(
                () => registry.Use(second, static _ => true),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not committed"));
            Assert.That(registry.Use(first, current => ReferenceEquals(current, value)), Is.True);
            Assert.That(registry.ActiveResourceCount, Is.EqualTo(1));
        });

        registry.Release(first);
        RenderResource<TrackedDisposable> third = registry.RegisterBorrowed(value);
        registry.Commit(third);

        Assert.Multiple(() =>
        {
            Assert.That(() => registry.RegisterOwned(value), Throws.TypeOf<InvalidOperationException>());
            Assert.That(registry.Use(third, current => ReferenceEquals(current, value)), Is.True);
        });

        registry.Release(third);
        Assert.Multiple(() =>
        {
            Assert.That(registry.ActiveResourceCount, Is.Zero);
            Assert.That(value.DisposeCount, Is.Zero);
        });
    }

    private static bool Read(
        RenderRequestResourceRegistry registry,
        RenderResource<TrackedDisposable> resource)
        => registry.Use(resource, static value => value is not null);

    private sealed class RecordingScopeStub : IRenderResourceRecordingScope
    {
        public bool IsRecording { get; set; } = true;
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
