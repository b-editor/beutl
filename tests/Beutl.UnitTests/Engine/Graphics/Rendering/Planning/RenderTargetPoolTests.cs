using System.Runtime.ExceptionServices;

using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RenderTargetPoolTests
{
    [Test]
    public void StableExactSize_WarmsOnce_WhileChangingSizeMisses()
    {
        var factory = new TrackingTargetFactory();
        using var pool = new RenderTargetPool(factory);
        TrackingRenderTarget firstTarget;
        long firstGeneration;

        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            PooledRenderTargetLease lease = request.Acquire(new PixelSize(8, 6));
            firstTarget = (TrackingRenderTarget)lease.Target;
            firstGeneration = lease.Generation;
            Assert.That(lease.WasReused, Is.False);
            lease.Dispose();
        }

        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            PooledRenderTargetLease lease = request.Acquire(new PixelSize(8, 6));
            Assert.Multiple(() =>
            {
                Assert.That(lease.Target, Is.SameAs(firstTarget));
                Assert.That(lease.Generation, Is.GreaterThan(firstGeneration));
                Assert.That(lease.WasReused, Is.True);
            });
            lease.Dispose();
        }

        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            PooledRenderTargetLease lease = request.Acquire(new PixelSize(9, 6));
            Assert.That(lease.WasReused, Is.False);
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
        PooledRenderTargetLease firstLease;
        PooledRenderTargetLease secondLease;
        PooledRenderTargetLease thirdLease;
        TrackingRenderTarget firstTarget;
        using (RenderTargetPoolRequest request = pool.BeginRequest())
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
            Assert.That(firstLease.State, Is.EqualTo(PooledRenderTargetLeaseState.Evicted));
            Assert.That(firstTarget.IsDisposed, Is.True);
            Assert.That(secondLease.State, Is.EqualTo(PooledRenderTargetLeaseState.Available));
            Assert.That(thirdLease.State, Is.EqualTo(PooledRenderTargetLeaseState.Available));
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
        PooledRenderTargetLease oldLease;
        TrackingRenderTarget oldTarget;
        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            oldLease = request.Acquire(new PixelSize(2, 2));
            oldTarget = (TrackingRenderTarget)oldLease.Target;
            oldLease.Dispose();
        }

        using (RenderTargetPoolRequest request = pool.BeginRequest())
            request.Acquire(new PixelSize(3, 3)).Dispose();

        Assert.That(oldTarget.IsDisposed, Is.False);
        using (pool.BeginRequest())
        {
            Assert.Multiple(() =>
            {
                Assert.That(oldLease.State, Is.EqualTo(PooledRenderTargetLeaseState.Evicted));
                Assert.That(oldTarget.IsDisposed, Is.True);
            });
        }
    }

    [Test]
    public void PreviewAllocationPressure_ReclaimsRetainedTargets_AndKeepsRenderingTheFrame()
    {
        var factory = new BudgetedTargetFactory(budgetBytes: 640);
        using var registry = new RenderTargetLeaseRegistry(factory);
        using (RenderTargetLeaseSession warmup = registry.BeginSession(RenderIntent.Preview))
        {
            warmup.Acquire(new PixelSize(4, 4)).Dispose();
            warmup.Acquire(new PixelSize(2, 2)).Dispose();
        }

        Assert.That(registry.Statistics.RetainedBytes, Is.EqualTo(160));

        using RenderTargetLeaseSession frame = registry.BeginSession(RenderIntent.Preview);
        RenderTargetLease pressured = frame.Acquire(new PixelSize(8, 8));
        RenderTargetLease rest = frame.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(pressured.Target.Width, Is.EqualTo(8));
            Assert.That(rest.Target.Width, Is.EqualTo(4));
            Assert.That(factory.DeclinedRequests, Is.EqualTo(1));
            Assert.That(registry.Statistics.RetainedBytes, Is.Zero);
            Assert.That(registry.Statistics.Evictions, Is.EqualTo(2));
        });
    }

    [Test]
    public void DeclinedAllocation_DegradesForPreview_AndFailsFastForDelivery()
    {
        using var registry = new RenderTargetLeaseRegistry(new SizeRejectingTargetFactory(rejectedWidth: 9));

        using (RenderTargetLeaseSession preview = registry.BeginSession(RenderIntent.Preview))
        {
            Assert.That(preview.TryAcquire(new PixelSize(9, 9)), Is.Null);
            using RenderTargetLease rest = preview.Acquire(new PixelSize(4, 4));
            Assert.That(rest.Target.Width, Is.EqualTo(4));
        }

        using RenderTargetLeaseSession delivery = registry.BeginSession(RenderIntent.Delivery);
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
        using var registry = new RenderTargetLeaseRegistry(factory);
        TrackingRenderTarget idleTarget;
        using (RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview))
        {
            RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
            idleTarget = (TrackingRenderTarget)lease.Target;
            lease.Dispose();
        }

        Assert.That(registry.Statistics.RetainedBytes, Is.EqualTo(4 * 4 * 8));

        long releasedBytes = registry.ReleaseRetainedTargets();

        Assert.Multiple(() =>
        {
            Assert.That(releasedBytes, Is.EqualTo(4 * 4 * 8));
            Assert.That(idleTarget.IsDisposed, Is.True);
            Assert.That(idleTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(registry.Statistics.RetainedBytes, Is.Zero);
            Assert.That(registry.Statistics.OwnedTargets, Is.Zero);
        });

        using RenderTargetLeaseSession active = registry.BeginSession(RenderIntent.Preview);
        using RenderTargetLease leased = active.Acquire(new PixelSize(2, 2));
        var leasedTarget = (TrackingRenderTarget)leased.Target;

        Assert.Multiple(() =>
        {
            Assert.That(registry.ReleaseRetainedTargets(), Is.Zero);
            Assert.That(leasedTarget.IsDisposed, Is.False);
            Assert.That(registry.Statistics.LeasedTargets, Is.EqualTo(1));
        });
    }

    [Test]
    public void Reuse_IncrementsGeneration_AndOldOrDoubleReleaseFails()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        PooledRenderTargetLease first;
        RenderTarget firstTarget;
        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            first = request.Acquire(new PixelSize(4, 4));
            firstTarget = first.Target;
            first.Dispose();
        }

        using RenderTargetPoolRequest secondRequest = pool.BeginRequest();
        PooledRenderTargetLease second = secondRequest.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(second.Target, Is.SameAs(firstTarget));
            Assert.That(second.Generation, Is.GreaterThan(first.Generation));
            Assert.That(
                () => first.Dispose(),
                Throws.InvalidOperationException.With.Message.Contains("already been discharged"));
        });

        second.Dispose();
        Assert.That(
            () => second.Dispose(),
            Throws.InvalidOperationException.With.Message.Contains("already been discharged"));
    }

    [Test]
    public void SessionDisposalFailure_EndsBothSessionAndPoolRequest()
    {
        var factory = new TrackingTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        RenderTargetLease lease = session.Acquire(new PixelSize(4, 4));
        var staleTarget = (TrackingRenderTarget)lease.Target;
        lease.PooledLease.Slot.Generation++;

        Assert.That(
            session.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("generation is stale"));

        Assert.Multiple(() =>
        {
            Assert.That(staleTarget.IsDisposed, Is.True);
            Assert.That(staleTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(registry.Statistics.OwnedTargets, Is.Zero);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
            Assert.That(registry.Statistics.OwnedBytes, Is.Zero);
            Assert.That(registry.Statistics.Evictions, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(() => registry.BeginSession(RenderIntent.Preview).Dispose());
    }

    [Test]
    public void CleanupFailureCheckpoint_TracksSessionAndRequestFailuresIndependently()
    {
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        var priorSessionFailure = new InvalidOperationException("prior-session");
        var priorRequestFailure = new InvalidOperationException("prior-request");
        var nextSessionFailure = new InvalidOperationException("next-session");
        var nextRequestFailure = new InvalidOperationException("next-request");
        session.RecordCleanupFailure(priorSessionFailure);
        session.Request.RecordCleanupFailure(priorRequestFailure);
        RenderTargetCleanupFailureCheckpoint checkpoint = session.CaptureCleanupFailureCheckpoint();

        session.RecordCleanupFailure(nextSessionFailure);
        session.Request.RecordCleanupFailure(nextRequestFailure);

        Assert.That(
            session.GetCleanupFailuresSince(checkpoint),
            Is.EqualTo(new[] { nextSessionFailure, nextRequestFailure }));
    }

    [Test]
    public void RegistryDisposal_PreservesSessionAndPoolFailures()
    {
        var poolFailure = new InvalidOperationException("pool-target-cleanup");
        var factory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: size.Width == 3 ? poolFailure : null));
        var registry = new RenderTargetLeaseRegistry(factory);
        RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        RenderTargetLease stale = session.Acquire(new PixelSize(4, 4));
        RenderTargetLease available = session.Acquire(new PixelSize(3, 3));
        available.Dispose();
        stale.PooledLease.Slot.Generation++;

        AggregateException? failure = Assert.Throws<AggregateException>(registry.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(
                failure!.InnerExceptions.Select(static exception => exception.Message),
                Is.EquivalentTo(new[] { "The render-target lease generation is stale.", poolFailure.Message }));
            Assert.That(
                factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.IsDisposed),
                Is.All.True);
            Assert.That(
                factory.Created.Cast<TrackingRenderTarget>().Select(static target => target.DisposeCalls),
                Is.All.EqualTo(1));
            Assert.That(registry.Statistics.OwnedTargets, Is.Zero);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });
        Assert.DoesNotThrow(registry.Dispose);
    }

    [Test]
    public void RequestDisposalFailure_EvictsTheFailedLeaseAndContinuesCleanup()
    {
        var cleanup = new InvalidOperationException("stale-target-cleanup");
        var factory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(
                size.Width,
                size.Height,
                disposeFailure: size.Width == 4 ? cleanup : null));
        using var pool = new RenderTargetPool(factory);
        RenderTargetPoolRequest request = pool.BeginRequest();
        PooledRenderTargetLease releasable = request.Acquire(new PixelSize(3, 3));
        PooledRenderTargetLease stale = request.Acquire(new PixelSize(4, 4));
        var staleTarget = (TrackingRenderTarget)stale.Target;
        stale.Slot.Generation++;

        Assert.That(
            request.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("generation is stale"));

        Assert.Multiple(() =>
        {
            Assert.That(stale.State, Is.EqualTo(PooledRenderTargetLeaseState.Evicted));
            Assert.That(releasable.State, Is.EqualTo(PooledRenderTargetLeaseState.Available));
            Assert.That(staleTarget.IsDisposed, Is.True);
            Assert.That(staleTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(request.CleanupFailures, Is.EqualTo(new[] { cleanup }));
            Assert.That(pool.Statistics.OwnedTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            Assert.That(pool.Statistics.OwnedBytes, Is.EqualTo(3 * 3 * 8));
            Assert.That(pool.Statistics.RetainedBytes, Is.EqualTo(3 * 3 * 8));
            Assert.That(pool.Statistics.Evictions, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(() => pool.BeginRequest().Dispose());
    }

    [Test]
    public void PoolDisposal_ContinuesAfterActiveRequestFailure()
    {
        var factory = new TrackingTargetFactory();
        var pool = new RenderTargetPool(factory);
        using (RenderTargetPoolRequest warmup = pool.BeginRequest())
            warmup.Acquire(new PixelSize(3, 3)).Dispose();
        RenderTargetPoolRequest active = pool.BeginRequest();
        PooledRenderTargetLease stale = active.Acquire(new PixelSize(4, 4));
        stale.Slot.Generation++;

        Assert.That(
            pool.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("generation is stale"));

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
        using (RenderTargetPoolRequest warmup = pool.BeginRequest())
            warmup.Acquire(new PixelSize(3, 3)).Dispose();
        RenderTargetPoolRequest active = pool.BeginRequest();
        PooledRenderTargetLease stale = active.Acquire(new PixelSize(4, 4));
        stale.Slot.Generation++;

        AggregateException? failure = Assert.Throws<AggregateException>(pool.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(
                failure!.Flatten().InnerExceptions.Select(static exception => exception.Message),
                Is.EquivalentTo(new[] { "The render-target lease generation is stale.", targetCleanup.Message }));
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
        using RenderTargetPoolRequest request = pool.BeginRequest();

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

        using PooledRenderTargetLease retry = request.Acquire(new PixelSize(4, 4));
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
        using RenderTargetPoolRequest request = pool.BeginRequest();

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

        using PooledRenderTargetLease retry = request.Acquire(new PixelSize(4, 4));
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
        PooledRenderTargetLease available;
        TrackingRenderTarget rejected;
        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            available = request.Acquire(new PixelSize(4, 4));
            rejected = (TrackingRenderTarget)available.Target;
            available.Dispose();
        }

        failNextRegistration = true;
        using RenderTargetPoolRequest retryRequest = pool.BeginRequest();
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

        using PooledRenderTargetLease retry = retryRequest.Acquire(new PixelSize(4, 4));
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
            using RenderTargetPoolRequest request = pool.BeginRequest();
            PooledRenderTargetLease source = request.Acquire(new PixelSize(4, 4));
            using PooledRenderTargetLease destination = request.Acquire(new PixelSize(4, 4));
            RenderTarget releasedTarget = source.Target;
            releasedTarget.Value.Canvas.Clear(SKColors.Red);
            destination.Target.Value.Canvas.Clear(SKColors.Transparent);
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                destination.Target,
                logicalSize: new Size(4, 4));
            var observedFlushes = new List<ImmediateCanvasFlushKind>();

            using (ImmediateCanvas.ObserveFlushes(observedFlushes.Add))
            {
                canvas.DrawRenderTargetPixelsWithoutFlush(releasedTarget, 0, 0);
                source.Dispose();
                using PooledRenderTargetLease reused = request.Acquire(new PixelSize(4, 4));
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
        using RenderTargetPoolRequest request = pool.BeginRequest();
        PooledRenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
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
        PooledRenderTargetLease firstLease;
        TrackingRenderTarget firstTarget;
        using (RenderTargetPoolRequest request = pool.BeginRequestForContext(firstContext, 0))
        {
            firstLease = request.Acquire(new PixelSize(5, 5));
            firstTarget = (TrackingRenderTarget)firstLease.Target;
            firstLease.Dispose();
        }

        using RenderTargetPoolRequest secondRequest = pool.BeginRequestForContext(secondContext, 0);
        PooledRenderTargetLease secondLease = secondRequest.Acquire(new PixelSize(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(firstLease.State, Is.EqualTo(PooledRenderTargetLeaseState.Evicted));
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

        using (RenderTargetPoolRequest request = pool.BeginRequestForContext(new object(), 0))
            request.Acquire(new PixelSize(2, 2)).Dispose();
        using (RenderTargetPoolRequest request = pool.BeginRequest())
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

            using (RenderTargetPoolRequest request = pool.BeginRequest())
            {
                using PooledRenderTargetLease lease = request.Acquire(new PixelSize(2, 2));
                firstContext = lease.Target.Value.Context
                    ?? throw new AssertionException("The first target-less allocation must bind a GPU context.");
            }

            factory.ExpectedContext = firstContext;
            using (RenderTargetPoolRequest request = pool.BeginRequest())
                request.Acquire(new PixelSize(3, 3)).Dispose();

            factory.ExpectedContext = recreatedContext.SkiaContext;
            using (RenderTargetPoolRequest request = pool.BeginRequestForContext(
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
        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            Assert.That(
                () => request.Acquire(new PixelSize(3, 3)),
                Throws.InvalidOperationException.With.Message.Contains("exact device size"));
            Assert.That(wrongSizeFactory.Created.Single().IsDisposed, Is.True);
        }

        var wrongFormatFactory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(size.Width, size.Height, SKColorType.Rgba8888));
        using (var pool = new RenderTargetPool(wrongFormatFactory))
        using (RenderTargetPoolRequest request = pool.BeginRequest())
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
        using RenderTargetPoolRequest request = pool.BeginRequest(external);

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
        using RenderTargetPoolRequest request = pool.BeginRequest();
        PooledRenderTargetLease first = request.Acquire(new PixelSize(4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => request.Acquire(new PixelSize(5, 4)),
                Throws.InvalidOperationException.With.Message.Contains("already owned"));
            Assert.That(first.Target.IsDisposed, Is.False);
            Assert.That(first.State, Is.EqualTo(PooledRenderTargetLeaseState.Leased));
        });
    }

    [Test]
    public void AcceptedCacheTransfer_RemovesTargetFromPoolOwnershipExactlyOnce()
    {
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        TrackingRenderTarget target;
        using (RenderTargetPoolRequest request = pool.BeginRequest())
        {
            PooledRenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
            target = (TrackingRenderTarget)lease.TransferToAcceptedCache();
            Assert.Multiple(() =>
            {
                Assert.That(lease.State, Is.EqualTo(PooledRenderTargetLeaseState.CacheTransferred));
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
        using (RenderTargetPoolRequest request = pool.BeginRequest())
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
}
