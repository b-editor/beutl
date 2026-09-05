using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

using Moq;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RenderTargetPoolTests
{
    [Test]
    public void Acquisition_DefinesNewAndReusedTargetsAsTransparent()
    {
        var factory = new TrackingTargetFactory(
            create: static (size, _) =>
            {
                var target = new TrackingRenderTarget(size.Width, size.Height);
                target.Value.Canvas.Clear(SKColors.Magenta);
                return target;
            });
        using var pool = new RenderTargetPool(factory);

        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            using RenderTargetLease lease = request.Acquire(new PixelSize(4, 3));
            AssertTargetIsTransparent(lease.Target);
            lease.Target.Value.Canvas.Clear(SKColors.Cyan);
        }

        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            using RenderTargetLease lease = request.Acquire(new PixelSize(4, 3));
            AssertTargetIsTransparent(lease.Target);
        }
    }

    [Test]
    public void EffectTargetClone_HoldsTheLeaseUntilTheLastReferenceIsDisposed()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Delivery);
        using RenderTarget sourceTarget = RenderTarget.CreateNull(4, 4);
        using var source = new EffectTarget(sourceTarget, new Rect(0, 0, 4, 4));
        RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
        EffectTarget pooled = source.CreateReplacement(lease);
        EffectTarget clone = pooled.Clone();

        Assert.That(clone.RenderTarget, Is.Not.SameAs(pooled.RenderTarget));

        pooled.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(lease.IsReleased, Is.False);
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
        });

        clone.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(lease.IsReleased, Is.True);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
        });
    }

    [Test]
    public void DeferredLease_RemainsUnavailableUntilTheGpuReclaimBoundary()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease lease = request.Acquire(new PixelSize(4, 4));

        pool.DeferRelease(lease);
        request.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(lease.State, Is.EqualTo(RenderTargetLeaseState.Deferred));
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
        });

        pool.CompleteDeferredRelease(lease);

        Assert.Multiple(() =>
        {
            Assert.That(lease.State, Is.EqualTo(RenderTargetLeaseState.Released));
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
        });
    }

    [Test]
    public void DeferredLease_CompletedAfterContextRetirement_IsEvicted()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
        var target = (TrackingRenderTarget)lease.Target;

        pool.DeferRelease(lease);
        session.Dispose();
        pool.RetireCurrentContext();
        pool.CompleteDeferredRelease(lease);

        Assert.Multiple(() =>
        {
            Assert.That(lease.State, Is.EqualTo(RenderTargetLeaseState.Evicted));
            Assert.That(target.IsDisposed, Is.True);
            Assert.That(target.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void EffectTargetFinalCloneRelease_DefersThePoolSlotUntilReclaim()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GpuResourceReclaimQueue.FlushAndDrain();
            using var pool = new RenderTargetPool(factory: null);
            RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview);
            RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
            if (lease.Target.Texture is null)
                Assert.Ignore("The backend fell back to a raster target, which needs no deferred release.");
            lease.Target.BeginDraw();
            lease.Target.Value.Canvas.Clear(SKColors.Transparent);

            EffectTarget target = EffectTarget.FromLease(
                lease,
                new Rect(0, 0, 4, 4),
                EffectiveScale.At(1),
                new PixelRect(0, 0, 4, 4));
            EffectTarget clone = target.Clone();

            target.Dispose();
            Assert.That(lease.IsReleased, Is.False);
            clone.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(lease.IsReleased, Is.True);
                Assert.That(lease.State, Is.EqualTo(RenderTargetLeaseState.Deferred));
                Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
                Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
                Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0));
            });

            session.Dispose();
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
            GpuResourceReclaimQueue.FlushAndDrain();

            Assert.Multiple(() =>
            {
                Assert.That(lease.State, Is.EqualTo(RenderTargetLeaseState.Released));
                Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
                Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public void StableExactSize_WarmsOnce_WhileChangingSizeMisses()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(factory);
        TrackingRenderTarget firstTarget;
        RenderTargetLease firstLease;

        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            firstLease = request.Acquire(new PixelSize(8, 6));
            firstTarget = (TrackingRenderTarget)firstLease.Target;
            firstLease.Dispose();
        }

        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            RenderTargetLease lease = request.Acquire(new PixelSize(8, 6));
            Assert.Multiple(() =>
            {
                Assert.That(lease.Target, Is.SameAs(firstTarget));
                Assert.That(lease, Is.Not.SameAs(firstLease));
            });
            lease.Dispose();
        }

        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            RenderTargetLease lease = request.Acquire(new PixelSize(9, 6));
            lease.Dispose();
        }

        RenderTargetPoolStatistics statistics = pool.Statistics;
        Assert.Multiple(() =>
        {
            Assert.That(statistics.Creates, Is.EqualTo(2));
            Assert.That(statistics.Misses, Is.EqualTo(2));
            Assert.That(statistics.Reuses, Is.EqualTo(1));
            Assert.That(statistics.AvailableTargets, Is.EqualTo(2));
        });
    }

    [Test]
    public void ByteCap_EvictsTheLeastRecentlyReleasedTarget()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions
            {
                MaximumRetainedBytes = 80,
                MaximumIdleRequests = int.MaxValue,
            });
        RenderTargetLease firstLease;
        RenderTargetLease secondLease;
        RenderTargetLease thirdLease;
        TrackingRenderTarget firstTarget;
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            firstLease = request.Acquire(new PixelSize(2, 2)); // 32 bytes
            secondLease = request.Acquire(new PixelSize(3, 2)); // 48 bytes
            thirdLease = request.Acquire(new PixelSize(1, 1)); // 8 bytes
            firstTarget = (TrackingRenderTarget)firstLease.Target;
            firstLease.Dispose();
            secondLease.Dispose();
            thirdLease.Dispose();
        }

        Assert.Multiple(() =>
        {
            Assert.That(firstLease.State, Is.EqualTo(RenderTargetLeaseState.Released));
            Assert.That(firstTarget.IsDisposed, Is.True);
            Assert.That(secondLease.State, Is.EqualTo(RenderTargetLeaseState.Released));
            Assert.That(thirdLease.State, Is.EqualTo(RenderTargetLeaseState.Released));
            Assert.That(pool.Statistics.RetainedBytes, Is.EqualTo(56));
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
        });
    }

    [Test]
    public void IdleLimit_EvictsOnlyAfterTheConfiguredNumberOfRequests()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions
            {
                MaximumRetainedBytes = long.MaxValue,
                MaximumIdleRequests = 1,
            });
        RenderTargetLease oldLease;
        TrackingRenderTarget oldTarget;
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            oldLease = request.Acquire(new PixelSize(2, 2));
            oldTarget = (TrackingRenderTarget)oldLease.Target;
            oldLease.Dispose();
        }

        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
            request.Acquire(new PixelSize(3, 3)).Dispose();

        Assert.That(oldTarget.IsDisposed, Is.False);
        using (pool.BeginSession(RenderIntent.Delivery))
        {
            Assert.Multiple(() =>
            {
                Assert.That(oldLease.State, Is.EqualTo(RenderTargetLeaseState.Released));
                Assert.That(oldTarget.IsDisposed, Is.True);
            });
        }
    }

    [Test]
    public void PreviewAllocationPressure_ReclaimsRetainedTargets_AndKeepsRenderingTheFrame()
    {
        var factory = new BudgetedTargetFactory(budgetBytes: 640);
        using var pool = new RenderTargetPool(factory);
        using (RenderTargetLeaseSession warmup = pool.BeginSession(
                   RenderIntent.Preview))
        {
            warmup.Acquire(new PixelSize(4, 4)).Dispose();
            warmup.Acquire(new PixelSize(2, 2)).Dispose();
        }

        Assert.That(pool.Statistics.RetainedBytes, Is.EqualTo(160));

        using RenderTargetLeaseSession frame = pool.BeginSession(
            RenderIntent.Preview);
        RenderTargetLease pressured = frame.Acquire(new PixelSize(8, 8));
        RenderTargetLease rest = frame.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(pressured.Target.Width, Is.EqualTo(8));
            Assert.That(rest.Target.Width, Is.EqualTo(4));
            Assert.That(factory.DeclinedRequests, Is.EqualTo(1));
            Assert.That(pool.Statistics.RetainedBytes, Is.Zero);
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(2));
        });
    }

    [Test]
    public void DeclinedAllocation_DegradesForPreview_AndFailsFastForDelivery()
    {
        using var pool = new RenderTargetPool(new SizeRejectingTargetFactory(rejectedWidth: 9));

        using (RenderTargetLeaseSession preview = pool.BeginSession(
                   RenderIntent.Preview))
        {
            Assert.That(preview.TryAcquire(new PixelSize(9, 9)), Is.Null);
            using RenderTargetLease rest = preview.Acquire(new PixelSize(4, 4));
            Assert.That(rest.Target.Width, Is.EqualTo(4));
        }

        using RenderTargetLeaseSession delivery = pool.BeginSession(
            RenderIntent.Delivery);
        Assert.Multiple(() =>
        {
            Assert.That(
                () => delivery.TryAcquire(new PixelSize(9, 9)),
                Throws.InvalidOperationException.With.Message.Contains("could not allocate 9x9 pixels"));
            Assert.DoesNotThrow(() => delivery.Acquire(new PixelSize(4, 4)).Dispose());
        });
    }

    [Test]
    public void IdleReclamation_ReleasesRetainedTargetsWithoutARequest_AndKeepsLeasedOnes()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(factory);
        TrackingRenderTarget idleTarget;
        using (RenderTargetLeaseSession session = pool.BeginSession(
                   RenderIntent.Preview))
        {
            RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
            idleTarget = (TrackingRenderTarget)lease.Target;
            lease.Dispose();
        }

        Assert.That(pool.Statistics.RetainedBytes, Is.EqualTo(4 * 4 * 8));

        long releasedBytes = pool.ReleaseRetainedTargets();

        Assert.Multiple(() =>
        {
            Assert.That(releasedBytes, Is.EqualTo(4 * 4 * 8));
            Assert.That(idleTarget.IsDisposed, Is.True);
            Assert.That(idleTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Statistics.RetainedBytes, Is.Zero);
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
        });

        using RenderTargetLeaseSession active = pool.BeginSession(
            RenderIntent.Preview);
        using RenderTargetLease leased = active.Acquire(new PixelSize(2, 2));
        var leasedTarget = (TrackingRenderTarget)leased.Target;

        Assert.Multiple(() =>
        {
            Assert.That(pool.ReleaseRetainedTargets(), Is.Zero);
            Assert.That(leasedTarget.IsDisposed, Is.False);
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
        });
    }

    [Test]
    public void Reuse_CreatesAFreshLease_AndReleasedLeasesStayIdempotent()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        RenderTargetLease first;
        RenderTarget firstTarget;
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            first = request.Acquire(new PixelSize(4, 4));
            firstTarget = first.Target;
            first.Dispose();
        }

        using RenderTargetLeaseSession secondRequest = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease second = secondRequest.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(second.Target, Is.SameAs(firstTarget));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.DoesNotThrow(first.Dispose);
            Assert.That(
                () => _ = first.Target,
                Throws.InvalidOperationException.With.Message.Contains("already been discharged"));
        });

        second.Dispose();
        Assert.DoesNotThrow(second.Dispose);
    }

    [Test]
    public void SessionDisposalFailure_EndsThePoolSession()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(factory);
        RenderTargetLeaseSession session = pool.BeginSession(
            RenderIntent.Preview);
        RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
        var staleTarget = (TrackingRenderTarget)lease.Target;
        lease.Slot.ActiveLease = null;

        Assert.That(
            session.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("lease is stale"));

        Assert.Multiple(() =>
        {
            Assert.That(staleTarget.IsDisposed, Is.True);
            Assert.That(staleTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.Zero);
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(() => pool.BeginSession(
            RenderIntent.Preview).Dispose());
    }

    [Test]
    public void CleanupFailureCheckpoint_ReturnsOnlyLaterFailuresInOrder()
    {
        using var pool = new RenderTargetPool(factory: null);
        using RenderTargetLeaseSession session = pool.BeginSession(
            RenderIntent.Preview);
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");
        var third = new InvalidOperationException("third");
        session.RecordCleanupFailure(first);
        RenderTargetCleanupFailureCheckpoint checkpoint = session.CaptureCleanupFailureCheckpoint();

        session.RecordCleanupFailure(second);
        session.RecordCleanupFailure(third);

        Assert.That(
            session.GetCleanupFailuresSince(checkpoint),
            Is.EqualTo(new[] { second, third }));
    }

    [Test]
    public void Session_CachesItsProgramContextIdentity()
    {
        using var pool = new RenderTargetPool(factory: null);
        using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview);

        object first = session.CacheDeviceContextIdentity.ContextIdentity;
        object second = session.CacheDeviceContextIdentity.ContextIdentity;

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void ReleasedSlot_DoesNotRetainItsSessionLeaseOrBorrowedDestination()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        (WeakReference Session, WeakReference Lease, WeakReference Destination) references =
            CreateReleasedWeakReferences(pool);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Multiple(() =>
        {
            Assert.That(references.Session.IsAlive, Is.False);
            Assert.That(references.Lease.IsAlive, Is.False);
            Assert.That(references.Destination.IsAlive, Is.False);
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
        });
        GC.KeepAlive(pool);
    }

    [Test]
    public void PoolDisposal_PreservesSessionAndTargetFailures()
    {
        var poolFailure = new InvalidOperationException("pool-target-cleanup");
        var factory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: size.Width == 3 ? poolFailure : null));
        var pool = new RenderTargetPool(factory);
        RenderTargetLeaseSession session = pool.BeginSession(
            RenderIntent.Preview);
        RenderTargetLease stale = session.Acquire(new PixelSize(4, 4));
        RenderTargetLease available = session.Acquire(new PixelSize(3, 3));
        available.Dispose();
        stale.Slot.ActiveLease = null;

        AggregateException? failure = Assert.Throws<AggregateException>(pool.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(
                failure!.InnerExceptions.Select(static exception => exception.Message),
                Is.EquivalentTo(new[] { "The render-target lease is stale.", poolFailure.Message }));
            Assert.That(
                factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.IsDisposed),
                Is.All.True);
            Assert.That(
                factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.DisposeCalls),
                Is.All.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
        });
        Assert.DoesNotThrow(pool.Dispose);
    }

    [Test]
    public void PoolDisposal_ReportsAPriorLeaseReleaseFailureOnce()
    {
        var pool = new RenderTargetPool(new TrackingTargetFactory());
        RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview);
        RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
        lease.Slot.ActiveLease = null;

        Assert.DoesNotThrow(lease.Dispose);
        Assert.That(session.CleanupFailures, Has.Exactly(1).Items);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(pool.Dispose);
        Assert.That(failure!.Message, Does.Contain("lease is stale"));
        Assert.DoesNotThrow(pool.Dispose);
    }

    [Test]
    public void SessionDisposalFailure_EvictsTheFailedLeaseAndContinuesCleanup()
    {
        var cleanup = new InvalidOperationException("stale-target-cleanup");
        var factory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: size.Width == 4 ? cleanup : null));
        using var pool = new RenderTargetPool(factory);
        RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease releasable = request.Acquire(new PixelSize(3, 3));
        RenderTargetLease stale = request.Acquire(new PixelSize(4, 4));
        var staleTarget = (TrackingRenderTarget)stale.Target;
        stale.Slot.ActiveLease = null;

        Assert.That(
            request.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("lease is stale"));

        Assert.Multiple(() =>
        {
            Assert.That(stale.State, Is.EqualTo(RenderTargetLeaseState.Evicted));
            Assert.That(releasable.State, Is.EqualTo(RenderTargetLeaseState.Released));
            Assert.That(staleTarget.IsDisposed, Is.True);
            Assert.That(staleTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(
                request.CleanupFailures.Select(static failure => failure.Message),
                Is.EqualTo(new[] { "The render-target lease is stale.", cleanup.Message }));
            Assert.That(pool.Statistics.OwnedTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.EqualTo(3 * 3 * 8));
            Assert.That(pool.Statistics.RetainedBytes, Is.EqualTo(3 * 3 * 8));
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(() => pool.BeginSession(RenderIntent.Delivery).Dispose());
    }

    [Test]
    public void SessionDisposal_AggregatesEveryLeaseReleaseFailure()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease first = session.Acquire(new PixelSize(3, 3));
        RenderTargetLease second = session.Acquire(new PixelSize(4, 4));
        first.Slot.ActiveLease = null;
        second.Slot.ActiveLease = null;

        AggregateException? failure = Assert.Throws<AggregateException>(session.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions.Select(static item => item.Message),
                Has.All.EqualTo("The render-target lease is stale."));
            Assert.That(session.CleanupFailures, Has.Count.EqualTo(2));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(2));
        });
    }

    [Test]
    public void PoolDisposal_ContinuesAfterActiveRequestFailure()
    {
        var factory = new TrackingTargetFactory();
        var pool = new RenderTargetPool(factory);
        using (RenderTargetLeaseSession warmup = pool.BeginSession(RenderIntent.Delivery))
            warmup.Acquire(new PixelSize(3, 3)).Dispose();
        RenderTargetLeaseSession active = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease stale = active.Acquire(new PixelSize(4, 4));
        stale.Slot.ActiveLease = null;

        Assert.That(
            pool.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("lease is stale"));

        Assert.Multiple(() =>
        {
            Assert.That(factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.IsDisposed),
                Is.All.True);
            Assert.That(factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.DisposeCalls),
                Is.All.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.Zero);
            Assert.That(pool.Statistics.RetainedBytes, Is.Zero);
        });
        Assert.DoesNotThrow(() => pool.Dispose());
    }

    [Test]
    public void PoolDisposal_AggregatesActiveRequestAndTargetCleanupFailures()
    {
        var targetCleanup = new InvalidOperationException("available-target-cleanup");
        var factory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: size.Width == 3 ? targetCleanup : null));
        var pool = new RenderTargetPool(factory);
        using (RenderTargetLeaseSession warmup = pool.BeginSession(RenderIntent.Delivery))
            warmup.Acquire(new PixelSize(3, 3)).Dispose();
        RenderTargetLeaseSession active = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease stale = active.Acquire(new PixelSize(4, 4));
        stale.Slot.ActiveLease = null;

        AggregateException? failure = Assert.Throws<AggregateException>(pool.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(
                failure!.Flatten().InnerExceptions.Select(static exception => exception.Message),
                Is.EquivalentTo(new[] { "The render-target lease is stale.", targetCleanup.Message }));
            Assert.That(
                factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.DisposeCalls),
                Is.All.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
        });
        Assert.DoesNotThrow(pool.Dispose);
    }

    [TestCase((int)RenderTargetPoolRegistrationStage.OwnedSlot)]
    [TestCase((int)RenderTargetPoolRegistrationStage.KnownTarget)]
    [TestCase((int)RenderTargetPoolRegistrationStage.KnownSurface)]
    public void FreshTargetRegistrationFailure_RollsBackEveryBookkeepingStageAndAllowsRetry(
        int failureStageValue)
    {
        var failureStage = (RenderTargetPoolRegistrationStage)failureStageValue;
        var primary = new InvalidOperationException($"target-registration-{failureStage}");
        bool failNextRegistration = true;
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions
            {
                AfterTargetRegistrationStep = stage =>
                {
                    if (failNextRegistration && stage == failureStage)
                    {
                        failNextRegistration = false;
                        throw primary;
                    }
                },
            });
        using RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => request.Acquire(new PixelSize(4, 4)));
        var rejected = (TrackingRenderTarget)factory.Created.Single();
        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(rejected.IsDisposed, Is.True);
            Assert.That(rejected.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Statistics.Creates, Is.Zero);
            Assert.That(pool.Statistics.Misses, Is.EqualTo(1));
            Assert.That(pool.Statistics.Evictions, Is.Zero);
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.Zero);
            Assert.That(pool.Statistics.RetainedBytes, Is.Zero);
            Assert.That(pool.Statistics.PeakLiveTargets, Is.Zero);
        });

        using RenderTargetLease retry = request.Acquire(new PixelSize(4, 4));
        Assert.Multiple(() =>
        {
            Assert.That(retry.Target, Is.Not.SameAs(rejected));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(1));
            Assert.That(pool.Statistics.Misses, Is.EqualTo(2));
            Assert.That(pool.Statistics.OwnedTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
        });
    }

    [Test]
    public void FreshLeaseRegistrationFailure_EvictsTheSlotAndAllowsRetry()
    {
        var primary = new InvalidOperationException("lease-registration-failure");
        var cleanup = new InvalidOperationException("lease-registration-cleanup");
        bool failNextRegistration = true;
        int leasedTargetsAtFailure = -1;
        RenderTargetPool? observedPool = null;
        var factory = new TrackingTargetFactory(
            (size, index) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: index == 0 ? cleanup : null));
        using var pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions
            {
                BeforeLeaseRegistration = () =>
                {
                    if (failNextRegistration)
                    {
                        failNextRegistration = false;
                        leasedTargetsAtFailure = observedPool!.Statistics.LeasedTargets;
                        throw primary;
                    }
                },
            });
        observedPool = pool;
        using RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => request.Acquire(new PixelSize(4, 4)));
        TrackingRenderTarget rejected = (TrackingRenderTarget)factory.Created.Single();
        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(leasedTargetsAtFailure, Is.EqualTo(1));
            Assert.That(rejected.IsDisposed, Is.True);
            Assert.That(rejected.DisposeCalls, Is.EqualTo(1));
            Assert.That(request.CleanupFailures, Is.EqualTo(new[] { cleanup }));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(1));
            Assert.That(pool.Statistics.Misses, Is.EqualTo(1));
            Assert.That(pool.Statistics.Reuses, Is.Zero);
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.Zero);
            Assert.That(pool.Statistics.RetainedBytes, Is.Zero);
            Assert.That(pool.Statistics.PeakLiveTargets, Is.EqualTo(1));
        });

        using RenderTargetLease retry = request.Acquire(new PixelSize(4, 4));
        Assert.Multiple(() =>
        {
            Assert.That(retry.Target, Is.Not.SameAs(rejected));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(2));
            Assert.That(pool.Statistics.Misses, Is.EqualTo(2));
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReusedLeaseRegistrationFailure_EvictsTheSlotAndAllowsRetry()
    {
        var primary = new InvalidOperationException("reused-lease-registration-failure");
        bool failNextRegistration = false;
        int leasedTargetsAtFailure = -1;
        RenderTargetPool? observedPool = null;
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions
            {
                BeforeLeaseRegistration = () =>
                {
                    if (failNextRegistration)
                    {
                        failNextRegistration = false;
                        leasedTargetsAtFailure = observedPool!.Statistics.LeasedTargets;
                        throw primary;
                    }
                },
            });
        observedPool = pool;
        RenderTargetLease available;
        TrackingRenderTarget rejected;
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            available = request.Acquire(new PixelSize(4, 4));
            rejected = (TrackingRenderTarget)available.Target;
            available.Dispose();
        }

        failNextRegistration = true;
        using RenderTargetLeaseSession retryRequest = pool.BeginSession(RenderIntent.Delivery);
        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => retryRequest.Acquire(new PixelSize(4, 4)));
        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(leasedTargetsAtFailure, Is.EqualTo(1));
            Assert.That(rejected.IsDisposed, Is.True);
            Assert.That(rejected.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(1));
            Assert.That(pool.Statistics.Misses, Is.EqualTo(1));
            Assert.That(pool.Statistics.Reuses, Is.EqualTo(1));
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(pool.Statistics.AvailableTargets, Is.Zero);
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.Zero);
            Assert.That(pool.Statistics.RetainedBytes, Is.Zero);
            Assert.That(pool.Statistics.PeakLiveTargets, Is.EqualTo(1));
        });

        using RenderTargetLease retry = retryRequest.Acquire(new PixelSize(4, 4));
        Assert.Multiple(() =>
        {
            Assert.That(retry.Target, Is.Not.SameAs(rejected));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(2));
            Assert.That(pool.Statistics.Misses, Is.EqualTo(2));
            Assert.That(pool.Statistics.Reuses, Is.EqualTo(1));
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.LeasedTargets, Is.EqualTo(1));
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void DeferredGpuDraw_PreservesSnapshotAcrossSameSlotReuse()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool(factory: null);
            using RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);
            RenderTargetLease source = request.Acquire(new PixelSize(4, 4));
            using RenderTargetLease destination = request.Acquire(new PixelSize(4, 4));
            RenderTarget releasedTarget = source.Target;
            releasedTarget.Value.Canvas.Clear(SKColors.Red);
            destination.Target.Value.Canvas.Clear(SKColors.Transparent);
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                destination.Target,
                density: 1f,
                maxWorkingScale: float.PositiveInfinity,
                logicalSize: new Size(4, 4),
                intent: RenderIntent.Preview);
            var observedFlushes = new List<ImmediateCanvasFlushKind>();

            using (ImmediateCanvas.ObserveFlushes(observedFlushes.Add))
            {
                canvas.DrawRenderTargetPixelsWithoutFlush(releasedTarget, 0, 0);
                source.Dispose();
                using RenderTargetLease reused = request.Acquire(new PixelSize(4, 4));
                Assert.That(reused.Target, Is.SameAs(releasedTarget));
                reused.Target.Value.Canvas.Clear(SKColors.Blue);
                Assert.That(observedFlushes, Is.Empty,
                    "Recording the draw and reusing its source slot must not add an executor-managed flush.");

                using Bitmap snapshot = destination.Target.Snapshot();
                ReadOnlySpan<ushort> pixels = snapshot.GetPixelSpan<ushort>();
                float red = (float)BitConverter.UInt16BitsToHalf(pixels[0]);
                float blue = (float)BitConverter.UInt16BitsToHalf(pixels[2]);
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[3]);
                Assert.Multiple(() =>
                {
                    Assert.That(red, Is.GreaterThan(0.99f));
                    Assert.That(blue, Is.LessThan(0.01f));
                    Assert.That(alpha, Is.GreaterThan(0.99f));
                });
            }
        });
    }

    [Test]
    public void DischargedLease_RejectsTargetAndDeviceSizeAccess()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        using RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
        lease.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => _ = lease.Target,
                Throws.InvalidOperationException.With.Message.Contains("already been discharged"));
            Assert.That(
                () => _ = lease.DeviceSize,
                Throws.InvalidOperationException.With.Message.Contains("already been discharged"));
        });
    }

    [Test]
    public void ContextRecreation_EvictsOldBucketsBeforeAllocation()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(factory);
        object firstContext = new();
        object secondContext = new();
        RenderTargetLease firstLease;
        TrackingRenderTarget firstTarget;
        using (RenderTargetLeaseSession request = pool.BeginSessionForContext(RenderIntent.Delivery, firstContext, 0))
        {
            firstLease = request.Acquire(new PixelSize(5, 5));
            firstTarget = (TrackingRenderTarget)firstLease.Target;
            firstLease.Dispose();
        }

        using RenderTargetLeaseSession secondRequest = pool.BeginSessionForContext(RenderIntent.Delivery, secondContext, 0);
        RenderTargetLease secondLease = secondRequest.Acquire(new PixelSize(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(firstLease.State, Is.EqualTo(RenderTargetLeaseState.Released));
            Assert.That(firstTarget.IsDisposed, Is.True);
            Assert.That(secondLease.Target, Is.Not.SameAs(firstTarget));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(2));
        });
    }

    [Test]
    public void BoundCpuContext_IsForwardedToSubsequentTargetlessFactoryMiss()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(factory);

        using (RenderTargetLeaseSession request = pool.BeginSessionForContext(RenderIntent.Delivery, new object(), 0))
            request.Acquire(new PixelSize(2, 2)).Dispose();
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
            request.Acquire(new PixelSize(3, 3)).Dispose();

        Assert.That(factory.Allocations, Has.Count.EqualTo(2));
        Assert.That(factory.Allocations, Has.All.Matches<RenderTargetAllocationDescriptor>(allocation =>
            allocation.PixelFormat == RenderTargetPixelFormat.LinearPremultipliedRgba16Float
            && allocation.GraphicsContext is null
            && allocation.GraphicsContextHandle == 0
            && allocation.GraphicsBackend is null));
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void TargetlessGpuBinding_ForwardsLiveContextOnLaterMissAndRecreation()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext recreatedContext = GraphicsContextFactory.CreateContext();
            var factory = new DescriptorTargetFactory();
            using var pool = new RenderTargetPool(factory);
            GRRecordingContext firstContext;

            using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
            {
                using RenderTargetLease lease = request.Acquire(new PixelSize(2, 2));
                firstContext = lease.Target.Value.Context
                    ?? throw new AssertionException("The first target-less allocation must bind a GPU context.");
            }

            factory.ExpectedContext = firstContext;
            using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
                request.Acquire(new PixelSize(3, 3)).Dispose();

            factory.ExpectedContext = recreatedContext.SkiaContext;
            using (RenderTargetLeaseSession request = pool.BeginSessionForContext(RenderIntent.Delivery,
                       recreatedContext.SkiaContext,
                       recreatedContext.SkiaContext.Handle))
            {
                request.Acquire(new PixelSize(4, 4)).Dispose();
            }

            Assert.Multiple(() =>
            {
                Assert.That(factory.Observations, Has.Count.EqualTo(3));
                Assert.That(factory.Observations, Has.All.Matches<AllocationObservation>(observation =>
                    observation.PixelFormat == RenderTargetPixelFormat.LinearPremultipliedRgba16Float
                    && observation.ContextMatchedExpectation));
                Assert.That(factory.Observations[0].HasGraphicsContext, Is.False);
                Assert.That(factory.Observations[0].GraphicsContextHandle, Is.Null);
                Assert.That(factory.Observations[0].GraphicsBackend, Is.Null);
                Assert.That(factory.Observations[1].HasGraphicsContext, Is.True);
                Assert.That(factory.Observations[1].GraphicsContextHandle, Is.EqualTo(firstContext.Handle));
                Assert.That(factory.Observations[1].GraphicsBackend, Is.EqualTo(firstContext.Backend));
                Assert.That(factory.Observations[2].HasGraphicsContext, Is.True);
                Assert.That(factory.Observations[2].GraphicsContextHandle,
                    Is.EqualTo(recreatedContext.SkiaContext.Handle));
                Assert.That(factory.Observations[2].GraphicsBackend,
                    Is.EqualTo(recreatedContext.SkiaContext.Backend));
            });
        });
    }

    [Test]
    public void FactoryTarget_MustMatchSizeAndRgba16fContract()
    {
        var wrongSizeFactory = new TrackingTargetFactory(
            (_, _) => new TrackingRenderTarget(2, 2));
        using (var pool = new RenderTargetPool(wrongSizeFactory))
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            Assert.That(
                () => request.Acquire(new PixelSize(3, 3)),
                Throws.InvalidOperationException.With.Message.Contains("exact device size"));
            Assert.That(wrongSizeFactory.Created.Single().IsDisposed, Is.True);
        }

        var wrongFormatFactory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(size.Width, size.Height, SKColorType.Rgba8888));
        using (var pool = new RenderTargetPool(wrongFormatFactory))
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            Assert.That(
                () => request.Acquire(new PixelSize(3, 3)),
                Throws.InvalidOperationException.With.Message.Contains("RGBA16F"));
            Assert.That(wrongFormatFactory.Created.Single().IsDisposed, Is.True);
        }
    }

    [Test]
    public void FactoryCannotReturnBorrowedDestination_AndPoolDoesNotDisposeIt()
    {
        using var external = new TrackingRenderTarget(4, 4);
        var factory = new TrackingTargetFactory((_, _) => external);
        using var pool = new RenderTargetPool(factory);
        using RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery, external);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => request.Acquire(new PixelSize(4, 4)),
                Throws.InvalidOperationException.With.Message.Contains("borrowed destination"));
            Assert.That(external.IsDisposed, Is.False);
            Assert.That(external.DisposeCalls, Is.Zero);
        });
    }

    [Test]
    public void FactoryCannotReturnAnAlreadyLeasedTarget()
    {
        TrackingRenderTarget? shared = null;
        var factory = new TrackingTargetFactory(
            (size, _) => shared ??= new TrackingRenderTarget(size.Width, size.Height));
        using var pool = new RenderTargetPool(factory);
        using RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease first = request.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => request.Acquire(new PixelSize(5, 4)),
                Throws.InvalidOperationException.With.Message.Contains("already owned"));
            Assert.That(first.Target.IsDisposed, Is.False);
            Assert.That(first.State, Is.EqualTo(RenderTargetLeaseState.Leased));
        });
    }

    [Test]
    public void AcceptedCacheTransfer_RemovesTargetFromPoolOwnershipExactlyOnce()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        TrackingRenderTarget target;
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            RenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
            target = (TrackingRenderTarget)lease.TransferToAcceptedCache();
            Assert.Multiple(() =>
            {
                Assert.That(lease.State, Is.EqualTo(RenderTargetLeaseState.CacheTransferred));
                Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
                Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
                Assert.That(
                    () => lease.TransferToAcceptedCache(),
                    Throws.InvalidOperationException.With.Message.Contains("already been discharged"));
            });
        }

        pool.Dispose();
        Assert.That(target.IsDisposed, Is.False);
        target.Dispose();
    }

    [Test]
    public void PoolDisposal_ContinuesAfterEveryTargetFailure()
    {
        var factory = new TrackingTargetFactory(
            (size, index) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: new InvalidOperationException($"dispose-{index}")));
        var pool = new RenderTargetPool(factory);
        using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Delivery))
        {
            request.Acquire(new PixelSize(2, 2)).Dispose();
            request.Acquire(new PixelSize(3, 3)).Dispose();
        }

        AggregateException? failure = Assert.Throws<AggregateException>(() => pool.Dispose());
        Assert.Multiple(() =>
        {
            Assert.That(
                failure!.InnerExceptions.Select(static exception => exception.Message),
                Is.EquivalentTo(new[] { "dispose-0", "dispose-1" }));
            Assert.That(factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.DisposeCalls),
                Is.All.EqualTo(1));
        });

        Assert.DoesNotThrow(() => pool.Dispose());
    }

    [Test]
    public void TargetlessDefaultAllocation_EvictsRetainedTargets_WhenTheSharedContextIsReplaced()
    {
        // GraphicsContextFactory.Shutdown is public, so the context a target-less request allocated on can be
        // replaced while this pool still retains that context's surfaces.
        IGraphicsContext first = Mock.Of<IGraphicsContext>();
        IGraphicsContext replacement = Mock.Of<IGraphicsContext>();
        using var pool = new RenderTargetPool(factory: null);
        RenderTarget retained;

        using (RenderTargetLeaseSession request = pool.BeginImplicitSession(RenderIntent.Delivery, first))
        {
            using RenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
            retained = lease.Target;
        }

        Assert.That(
            pool.Statistics.AvailableTargets,
            Is.EqualTo(1),
            "the fixture must retain a slot, or the eviction it asserts is unobservable");

        using RenderTargetLeaseSession replaced = pool.BeginImplicitSession(RenderIntent.Delivery, replacement);
        using RenderTargetLease reallocated = replaced.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(reallocated.Target, Is.Not.SameAs(retained));
            Assert.That(retained.IsDisposed, Is.True);
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(2));
        });
    }

    [Test]
    public void TargetlessDefaultAllocation_ReusesRetainedTargets_WhileTheSharedContextIsUnchanged()
    {
        IGraphicsContext shared = Mock.Of<IGraphicsContext>();
        using var pool = new RenderTargetPool(factory: null);
        RenderTarget retained;

        using (RenderTargetLeaseSession request = pool.BeginImplicitSession(RenderIntent.Delivery, shared))
        {
            using RenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
            retained = lease.Target;
        }

        using RenderTargetLeaseSession second = pool.BeginImplicitSession(RenderIntent.Delivery, shared);
        using RenderTargetLease reused = second.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(reused.Target, Is.SameAs(retained));
            Assert.That(pool.Statistics.Evictions, Is.Zero);
            Assert.That(pool.Statistics.Creates, Is.EqualTo(1));
        });
    }

    private sealed class TrackingTargetFactory(
        Func<PixelSize, int, RenderTarget>? create = null) : IRenderTargetFactory
    {
        public List<RenderTarget> Created { get; } = [];

        public List<RenderTargetAllocationDescriptor> Allocations { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            Allocations.Add(allocation);
            RenderTarget target = create?.Invoke(deviceSize, Created.Count)
                ?? new TrackingRenderTarget(deviceSize.Width, deviceSize.Height);
            Created.Add(target);
            return target;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Session, WeakReference Lease, WeakReference Destination)
        CreateReleasedWeakReferences(RenderTargetPool pool)
    {
        var destination = new TrackingRenderTarget(8, 8);
        RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview, destination);
        RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
        lease.Dispose();
        session.Dispose();
        return (new WeakReference(session), new WeakReference(lease), new WeakReference(destination));
    }

    private sealed class SizeRejectingTargetFactory(int rejectedWidth) : IRenderTargetFactory
    {
        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
            => allocation.DeviceSize.Width == rejectedWidth
                ? null
                : new TrackingRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class BudgetedTargetFactory(long budgetBytes) : IRenderTargetFactory
    {
        private readonly List<TrackingRenderTarget> _live = [];

        public int DeclinedRequests { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            _live.RemoveAll(static target => target.IsDisposed);
            long requested = (long)deviceSize.Width * deviceSize.Height * 8;
            long live = _live.Sum(static target => (long)target.Width * target.Height * 8);
            if (live + requested > budgetBytes)
            {
                DeclinedRequests++;
                return null;
            }

            var created = new TrackingRenderTarget(deviceSize.Width, deviceSize.Height);
            _live.Add(created);
            return created;
        }
    }

    private sealed class DescriptorTargetFactory : IRenderTargetFactory
    {
        public GRRecordingContext? ExpectedContext { get; set; }

        public List<AllocationObservation> Observations { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Observations.Add(new AllocationObservation(
                allocation.PixelFormat,
                allocation.GraphicsContext is not null,
                allocation.GraphicsContextHandle,
                allocation.GraphicsBackend,
                ReferenceEquals(allocation.GraphicsContext, ExpectedContext)));
            PixelSize size = allocation.DeviceSize;
            if (allocation.GraphicsContext is null)
                return RenderTarget.Create(size.Width, size.Height);

            SKSurface? surface = SKSurface.Create(
                allocation.GraphicsContext,
                false,
                new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()));
            return surface is null ? null : new TrackingRenderTarget(surface, size.Width, size.Height);
        }
    }

    private readonly record struct AllocationObservation(
        RenderTargetPixelFormat PixelFormat,
        bool HasGraphicsContext,
        nint? GraphicsContextHandle,
        GRBackend? GraphicsBackend,
        bool ContextMatchedExpectation);

    private sealed class TrackingRenderTarget : RenderTarget
    {
        private readonly Exception? _disposeFailure;

        public TrackingRenderTarget(SKSurface surface, int width, int height)
            : base(surface, width, height)
        {
        }

        public TrackingRenderTarget(
            int width,
            int height,
            SKColorType colorType = SKColorType.RgbaF16,
            Exception? disposeFailure = null)
            : base(
                SKSurface.Create(new SKImageInfo(
                    width,
                    height,
                    colorType,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear())),
                width,
                height)
        {
            _disposeFailure = disposeFailure;
        }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            bool fail = disposing && !IsDisposed && _disposeFailure is not null;
            if (disposing && !IsDisposed)
                DisposeCalls++;
            base.Dispose(disposing);
            if (fail)
                throw _disposeFailure!;
        }
    }

    private static unsafe void AssertTargetIsTransparent(RenderTarget target)
    {
        using Bitmap snapshot = target.Snapshot();
        for (int y = 0; y < snapshot.Height; y++)
        {
            var row = new ReadOnlySpan<Half>(
                (byte*)snapshot.Data + (long)y * snapshot.RowBytes,
                snapshot.Width * 4);
            Assert.That(row.ToArray(), Is.All.EqualTo((Half)0));
        }
    }
}
