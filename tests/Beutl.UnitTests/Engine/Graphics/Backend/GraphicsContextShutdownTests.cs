using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
[NonParallelizable]
public sealed class GraphicsContextShutdownTests
{
    [Test]
    public void ASucceedingShutdown_ClearsTheStateItDestroyed()
    {
        bool disposed = false;
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.Setup(value => value.Dispose()).Callback(() => disposed = true);

        RenderThread.Dispatcher.Invoke(() =>
        {
            using var scope = InstalledGraphicsScope.StandIn(context.Object);
            Assert.That(GraphicsContextFactory.Shutdown, Throws.Nothing);

            Assert.Multiple(() =>
            {
                Assert.That(disposed, Is.True);
                Assert.That(GraphicsContextFactory.SharedContext, Is.Null);
            });
        });
    }

    [Test]
    public void AFailingReclaimFlush_StillDischargesTheQueueBeforeDestroyingItsContext()
    {
        var deviceLoss = new InvalidOperationException("The shared context is abandoned.");
        var order = new List<string>();
        var abandoned = new Mock<IGraphicsContext>(MockBehavior.Strict);
        abandoned.SetupGet(context => context.SkiaContext).Throws(deviceLoss);
        abandoned.Setup(context => context.Dispose()).Callback(() => order.Add("context"));

        RenderThread.Dispatcher.Invoke(() =>
        {
            using var scope = InstalledGraphicsScope.StandIn(abandoned.Object, drainOnDispose: true);
            var deferred = new TrackedDisposable(() => order.Add("queued"));
            Assert.That(
                GpuResourceReclaimQueue.TryDefer(deferred, approximateBytes: 0),
                Is.True,
                "the fixture must give the queue something to flush, or the flush never runs");

            Assert.That(
                GraphicsContextFactory.Shutdown,
                Throws.Exception.SameAs(deviceLoss),
                "swallowing the flush failure would hide the device loss that caused it");

            Assert.Multiple(() =>
            {
                Assert.That(
                    order,
                    Is.EqualTo(new[] { "queued", "context" }),
                    "a queued resource destroys itself through the context that owns it, so releasing "
                    + "it after that context is destroyed reaches a device that no longer exists");
                Assert.That(
                    GpuResourceReclaimQueue.PendingCount,
                    Is.Zero,
                    "what the teardown leaves queued has no context left to be destroyed through");
                Assert.That(
                    GraphicsContextFactory.GetOrCreateShared(),
                    Is.Not.SameAs(abandoned.Object),
                    "a context that survives its own shutdown is handed straight back to the next caller");
            });
        });
    }

    [Test]
    public void AFailingContextRelease_ClearsTheInstalledStateAndRethrowsTheFailure()
    {
        var releaseFailure = new InvalidOperationException("The graphics context could not be released.");
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.Setup(value => value.Dispose()).Throws(releaseFailure);

        RenderThread.Dispatcher.Invoke(() =>
        {
            using var scope = InstalledGraphicsScope.StandIn(context.Object);
            Assert.That(
                GraphicsContextFactory.Shutdown,
                Throws.Exception.SameAs(releaseFailure));
            Assert.That(GraphicsContextFactory.SharedContext, Is.Null);
        });
    }

    [Test]
    public void AShutdown_RetiresAPooledTargetWhileTheDeviceItNamesIsStillInstalled()
    {
        var order = new List<string>();
        IGraphicsContext? contextWhenTargetWasReleased = null;
        bool queueWouldStillHaveTakenTheRelease = false;
        var installed = new Mock<IGraphicsContext>(MockBehavior.Strict);
        installed.Setup(value => value.Dispose()).Callback(() => order.Add("context"));

        RenderThread.Dispatcher.Invoke(() =>
        {
            var factory = new RetirementObservingTargetFactory(() =>
            {
                order.Add("pooled-target");
                contextWhenTargetWasReleased = GraphicsContextFactory.SharedContext;
                var probe = new TrackedDisposable();
                queueWouldStillHaveTakenTheRelease =
                    GpuResourceReclaimQueue.TryDefer(probe, approximateBytes: 0);
                // Hand the probe straight back, so the flush this shutdown still owes stays a no-op
                // against a stand-in context that has no Skia context to flush.
                GpuResourceReclaimQueue.DrainAfterContextSync();
                probe.Dispose();
            });

            using var pool = new RenderTargetPool(factory);

            // Acquire and release, so the pool retains the target for reuse rather than freeing it.
            using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Preview))
            {
                request.Acquire(new PixelSize(4, 4)).Dispose();
            }

            Assert.That(
                pool.Statistics.AvailableTargets,
                Is.EqualTo(1),
                "the fixture must leave the pool holding a target, or the shutdown has nothing to retire");

            using var scope = InstalledGraphicsScope.StandIn(installed.Object, drainOnDispose: true);
            Assert.That(GraphicsContextFactory.Shutdown, Throws.Nothing);

            Assert.Multiple(() =>
            {
                Assert.That(
                    order,
                    Is.EqualTo(new[] { "pooled-target", "context" }),
                    "a target released after its context is destroyed names a device vkDestroyDevice "
                    + "has taken, and the backend drops the destroy rather than issue it");
                Assert.That(
                    contextWhenTargetWasReleased,
                    Is.SameAs(installed.Object),
                    "the device the target's Vulkan objects belong to has to still be installed");
                Assert.That(
                    queueWouldStillHaveTakenTheRelease,
                    Is.True,
                    "a release the reclaim queue declines is the one that runs inline against a "
                    + "destroyed device");
                Assert.That(
                    pool.Statistics.AvailableTargets,
                    Is.Zero,
                    "a target the pool still holds outlives the only device that can destroy it");
            });
        });
    }

    [Test]
    public void AFailingPooledTargetRetirement_StillReleasesTheContextItCouldNotRetire()
    {
        var retirementFailure = new InvalidOperationException("The pooled target could not be destroyed.");
        bool disposed = false;
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.Setup(value => value.Dispose()).Callback(() => disposed = true);

        RenderThread.Dispatcher.Invoke(() =>
        {
            var factory = new RetirementObservingTargetFactory(() => throw retirementFailure);
            using var pool = new RenderTargetPool(factory);
            using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Preview))
            {
                request.Acquire(new PixelSize(4, 4)).Dispose();
            }

            using var scope = InstalledGraphicsScope.StandIn(context.Object);
            Assert.That(
                GraphicsContextFactory.Shutdown,
                Throws.Exception.SameAs(retirementFailure),
                "a retirement speaks for the same suspect device the flush does");

            Assert.Multiple(() =>
            {
                Assert.That(
                    disposed,
                    Is.True,
                    "the retirement added ahead of the release must not become a third way to strand it");
                Assert.That(
                    GraphicsContextFactory.SharedContext,
                    Is.Null,
                    "a context that survives its own shutdown is handed straight back to the next caller");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AShutdown_LeavesAPooledTargetsBackendReleaseOnALiveDevice()
    {
        VulkanTestEnvironment.EnsureAvailable();

        bool releaseRanWhenTheTargetWasRetired = false;
        bool releaseRanOnceTheContextWasDestroyed = false;

        RenderThread.Dispatcher.Invoke(() =>
        {
            IGraphicsContext standIn = GraphicsContextFactory.CreateContext();
            VulkanContext vulkan = VulkanTestEnvironment.ResolveVulkan(standIn);

            // Nothing records or submits through the stand-in, so its command pool runs a release the
            // moment it accepts one and drops it once the device is gone - the same two branches a pooled
            // texture's vkDestroyImageView / vkDestroyImage / vkFreeMemory take.
            var factory = new RetirementObservingTargetFactory(
                () => vulkan.DeferRelease(() => releaseRanWhenTheTargetWasRetired = true));

            using var pool = new RenderTargetPool(factory);
            using (RenderTargetLeaseSession request = pool.BeginSession(RenderIntent.Preview))
            {
                request.Acquire(new PixelSize(4, 4)).Dispose();
            }

            using var scope = InstalledGraphicsScope.StandIn(standIn);
            Assert.That(GraphicsContextFactory.Shutdown, Throws.Nothing);
            vulkan.DeferRelease(() => releaseRanOnceTheContextWasDestroyed = true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                releaseRanWhenTheTargetWasRetired,
                Is.True,
                "the pooled texture's destroys have to reach a device that still exists, or the backend "
                + "drops them and the images outlive every handle that could free them");
            Assert.That(
                releaseRanOnceTheContextWasDestroyed,
                Is.False,
                "the same release issued after this shutdown is dropped, which is what a pool retired "
                + "any later than this would have got");
        });
    }

    private sealed class TrackedDisposable(Action? onDispose = null) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            onDispose?.Invoke();
        }
    }

    /// <summary>Allocates raster targets that report the state the process was in when they were released.</summary>
    /// <remarks>
    /// The suite runs on whatever backend is available, so the released Vulkan objects cannot be counted
    /// directly. What decides whether they are destroyed or dropped is observable on every backend: whether
    /// the device that owns them is still installed when the release runs.
    /// </remarks>
    private sealed class RetirementObservingTargetFactory(Action onRelease) : IRenderTargetFactory
    {
        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
            => new ObservedRenderTarget(allocation.DeviceSize, onRelease);

        private sealed class ObservedRenderTarget(PixelSize size, Action onRelease) : RenderTarget(
            CpuRenderTargets.CreateSurface(size.Width, size.Height),
            size.Width,
            size.Height)
        {
            protected override void Dispose(bool disposing)
            {
                bool release = disposing && !IsDisposed;
                base.Dispose(disposing);
                if (release)
                    onRelease();
            }
        }
    }
}
