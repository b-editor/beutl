using System.Runtime.ExceptionServices;

using Beutl.Graphics;
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
    public void LinearThreeAndTenStagePlans_HaveTheSameTwoTargetPeak()
    {
        ResourcePlan three = CreateLinearPlan(3, new PixelSize(8, 8));
        ResourcePlan ten = CreateLinearPlan(10, new PixelSize(8, 8));

        Assert.Multiple(() =>
        {
            Assert.That(three.PeakLiveIntermediates, Is.EqualTo(2));
            Assert.That(ten.PeakLiveIntermediates, Is.EqualTo(2));
            Assert.That(three.PhysicalSlotCount, Is.EqualTo(2));
            Assert.That(ten.PhysicalSlotCount, Is.EqualTo(2));
            Assert.That(
                ten.Allocations.Select(static allocation => allocation.SlotId.Value),
                Is.EqualTo(new[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2 }));
        });
    }

    [Test]
    public void FanOut_KeepsValueLiveThroughItsLastConsumer()
    {
        PixelSize size = new(4, 4);
        ResourcePlan plan = ResourcePlan.Create(
        [
            Requirement(1, size, 0, 1, 4),
            Requirement(2, size, 1, 2),
            Requirement(3, size, 5, 6),
        ]);

        ResourcePlanAllocation fanOut = plan.GetAllocation(new ResourcePlanValueId(1));
        ResourcePlanAllocation overlapping = plan.GetAllocation(new ResourcePlanValueId(2));
        ResourcePlanAllocation afterFanOut = plan.GetAllocation(new ResourcePlanValueId(3));
        Assert.Multiple(() =>
        {
            Assert.That(fanOut.LastUsePosition, Is.EqualTo(4));
            Assert.That(overlapping.SlotId, Is.Not.EqualTo(fanOut.SlotId));
            Assert.That(afterFanOut.SlotId, Is.EqualTo(fanOut.SlotId));
            Assert.That(plan.PeakLiveIntermediates, Is.EqualTo(2));
        });
    }

    [Test]
    public void Planning_NeverAliasesDifferentExactSizes()
    {
        ResourcePlan plan = ResourcePlan.Create(
        [
            Requirement(1, new PixelSize(4, 4), 0, 1),
            Requirement(2, new PixelSize(5, 4), 2, 3),
            Requirement(3, new PixelSize(4, 4), 2, 3),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(plan.GetAllocation(new ResourcePlanValueId(1)).SlotId,
                Is.EqualTo(plan.GetAllocation(new ResourcePlanValueId(3)).SlotId));
            Assert.That(plan.GetAllocation(new ResourcePlanValueId(2)).SlotId,
                Is.Not.EqualTo(plan.GetAllocation(new ResourcePlanValueId(1)).SlotId));
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
    public void CleanupFailure_IsRecordedWithoutReplacingPrimaryFailure()
    {
        var cleanup = new InvalidOperationException("dispose-failure");
        var factory = new TrackingTargetFactory(
            (size, _) => new TrackingRenderTarget(size.Width, size.Height, disposeFailure: cleanup));
        using var pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions
            {
                MaximumRetainedBytes = 0,
                MaximumIdleRequests = int.MaxValue,
            });
        var primary = new InvalidOperationException("primary-failure");
        using RenderTargetPoolRequest request = pool.BeginRequest();
        PooledRenderTargetLease lease = request.Acquire(new PixelSize(4, 4));
        lease.Dispose();
        request.Dispose();

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(
            () => request.ThrowAfterCleanup(ExceptionDispatchInfo.Capture(primary)));
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(primary));
            Assert.That(request.CleanupFailures, Is.EqualTo(new[] { cleanup }));
            Assert.That(lease.State, Is.EqualTo(PooledRenderTargetLeaseState.Evicted));
        });
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

    [Test]
    public void ResourceExecution_ReusesAfterLastUse_AndTransfersOnlyAcceptedPayload()
    {
        PixelSize size = new(4, 4);
        ResourcePlan plan = ResourcePlan.Create(
        [
            Requirement(1, size, 0, 1),
            Requirement(2, size, 1, 2),
            Requirement(3, size, 2, 2, transferToCache: true),
        ]);
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        using RenderTargetPoolRequest request = pool.BeginRequest();
        using ResourcePlanExecution execution = plan.BeginExecution(request);

        execution.BeginPosition(0);
        RenderTarget first = execution.GetTarget(new ResourcePlanValueId(1));
        execution.CompletePosition(0);

        execution.BeginPosition(1);
        RenderTarget second = execution.GetTarget(new ResourcePlanValueId(2));
        execution.CompletePosition(1);

        execution.BeginPosition(2);
        RenderTarget third = execution.GetTarget(new ResourcePlanValueId(3));
        Assert.That(third, Is.SameAs(first));
        execution.CompletePosition(2, static (_, _) => true);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(execution.PeakLiveIntermediates, Is.EqualTo(2));
            Assert.That(execution.PeakLiveIntermediates, Is.EqualTo(plan.PeakLiveIntermediates));
            Assert.That(execution.CacheTransfers.Select(static item => item.ValueId.Value), Is.EqualTo(new[] { 3 }));
            Assert.That(pool.Statistics.Creates, Is.EqualTo(2));
            Assert.That(pool.Statistics.Reuses, Is.EqualTo(1));
            Assert.That(pool.Statistics.OwnedTargets, Is.EqualTo(1));
        });

        RenderTarget transferred = execution.CacheTransfers.Single().Target;
        request.Dispose();
        pool.Dispose();
        Assert.That(transferred.IsDisposed, Is.False);
        transferred.Dispose();
    }

    [Test]
    public void ResourceExecution_RejectedCacheCaptureReturnsTargetToPool()
    {
        ResourcePlan plan = ResourcePlan.Create(
        [
            Requirement(1, new PixelSize(2, 2), 0, 0, transferToCache: true),
        ]);
        using var pool = new RenderTargetPool(new TrackingTargetFactory());
        using RenderTargetPoolRequest request = pool.BeginRequest();
        using ResourcePlanExecution execution = plan.BeginExecution(request);

        execution.BeginPosition(0);
        RenderTarget target = execution.GetTarget(new ResourcePlanValueId(1));
        execution.CompletePosition(0, static (_, _) => false);

        Assert.Multiple(() =>
        {
            Assert.That(execution.CacheTransfers, Is.Empty);
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
            Assert.That(target.IsDisposed, Is.False);
        });
    }

    private static ResourcePlan CreateLinearPlan(int stages, PixelSize size)
        => ResourcePlan.Create(Enumerable.Range(0, stages)
            .Select(index => Requirement(index + 1, size, index, index + 1)));

    private static ResourcePlanRequirement Requirement(
        int id,
        PixelSize size,
        int acquisition,
        int consumer,
        bool transferToCache = false)
        => new(
            new ResourcePlanValueId(id),
            size,
            acquisition,
            [consumer],
            transferToCache);

    private static ResourcePlanRequirement Requirement(
        int id,
        PixelSize size,
        int acquisition,
        int firstConsumer,
        int secondConsumer)
        => new(
            new ResourcePlanValueId(id),
            size,
            acquisition,
            [firstConsumer, secondConsumer]);

    private sealed class TrackingTargetFactory(
        Func<PixelSize, int, RenderTarget>? create = null) : IRenderTargetFactory
    {
        public List<RenderTarget> Created { get; } = [];

        public RenderTarget? Create(PixelSize deviceSize)
        {
            RenderTarget target = create?.Invoke(deviceSize, Created.Count)
                ?? new TrackingRenderTarget(deviceSize.Width, deviceSize.Height);
            Created.Add(target);
            return target;
        }
    }

    private sealed class TrackingRenderTarget : RenderTarget
    {
        private readonly Exception? _disposeFailure;

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
