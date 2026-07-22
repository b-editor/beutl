using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderRequestOwnerTests
{
    [Test]
    public void Cleanup_UsesStrictLifoAndContinuesAfterFault()
    {
        var order = new List<int>();
        using var owner = new RenderRequestOwner();
        owner.Register(() => order.Add(1));
        owner.Register(() =>
        {
            order.Add(2);
            throw new InvalidOperationException("cleanup-2");
        });
        owner.Register(() => order.Add(3));

        owner.Cleanup();

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(owner.CleanupFailures.Select(static item => item.Message), Is.EqualTo(new[] { "cleanup-2" }));
            Assert.That(owner.PrimaryFailure?.SourceException.Message, Is.EqualTo("cleanup-2"));
            Assert.That(owner.IsCleanedUp, Is.True);
        });

        owner.Cleanup();
        Assert.That(order, Is.EqualTo(new[] { 3, 2, 1 }));
    }

    [Test]
    public void PrimaryFailure_IsPreservedAndLaterFailuresAreSecondary()
    {
        var primary = new ApplicationException("render-primary");
        var later = new InvalidOperationException("render-secondary");
        var cleanup = new IOException("cleanup-secondary");
        using var owner = new RenderRequestOwner();
        owner.Register(() => throw cleanup);

        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(later);
        owner.Cleanup();

        Exception thrown = Assert.Throws<ApplicationException>(() => owner.ThrowIfFailed())!;
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(primary));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
            Assert.That(owner.SecondaryFailures, Is.EqualTo(new Exception[] { later, cleanup }));
            Assert.That(owner.CleanupFailures, Is.EqualTo(new Exception[] { cleanup }));
        });
    }

    [Test]
    public void DischargeAndAcceptedCacheTransfer_PreventCleanupExactlyOnce()
    {
        int cleanupCount = 0;
        using var owner = new RenderRequestOwner();
        RenderOwnershipToken discharged = owner.Register(() => cleanupCount++);
        RenderOwnershipToken transferred = owner.Register(() => cleanupCount++);
        RenderOwnershipToken pending = owner.Register(() => cleanupCount++);

        owner.Discharge(discharged);
        owner.DischargeAfterAcceptedCacheTransfer(transferred);
        owner.Cleanup();

        Assert.Multiple(() =>
        {
            Assert.That(cleanupCount, Is.EqualTo(1));
            Assert.That(discharged.State, Is.EqualTo(RenderOwnershipState.Discharged));
            Assert.That(transferred.State, Is.EqualTo(RenderOwnershipState.CacheTransferred));
            Assert.That(pending.State, Is.EqualTo(RenderOwnershipState.Discharged));
            Assert.That(() => owner.Discharge(discharged), Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => owner.DischargeAfterAcceptedCacheTransfer(transferred),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void ResourceCleanupAndCacheTransfer_HaveDistinctOwnershipOutcomes()
    {
        var releasedValue = new TrackedDisposable();
        var transferredValue = new TrackedDisposable();
        using var registry = new RenderRequestResourceRegistry();
        using var owner = new RenderRequestOwner();
        RenderResource<TrackedDisposable> released = registry.RegisterOwned(releasedValue);
        RenderResource<TrackedDisposable> transferred = registry.RegisterOwned(transferredValue);
        registry.Commit(released);
        registry.Commit(transferred);
        owner.Register(() => registry.Release(released));
        RenderOwnershipToken transferToken = owner.Register(() => registry.Release(transferred));

        TrackedDisposable cachePayload = registry.TransferOwned(transferred);
        owner.DischargeAfterAcceptedCacheTransfer(transferToken);
        owner.Cleanup();

        Assert.Multiple(() =>
        {
            Assert.That(releasedValue.DisposeCount, Is.EqualTo(1));
            Assert.That(transferredValue.DisposeCount, Is.Zero);
            Assert.That(cachePayload, Is.SameAs(transferredValue));
        });

        cachePayload.Dispose();
        Assert.That(transferredValue.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void TokenFromAnotherOwner_IsRejected()
    {
        using var first = new RenderRequestOwner();
        using var second = new RenderRequestOwner();
        RenderOwnershipToken token = first.Register(static () => { });

        Assert.That(() => second.Discharge(token), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RegisterAfterCleanup_IsRejected()
    {
        using var owner = new RenderRequestOwner();
        owner.Cleanup();

        Assert.That(() => owner.Register(static () => { }), Throws.TypeOf<InvalidOperationException>());
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
