using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="GraphicsContextFactory.Shutdown"/> is the only way back to a working device after the shared
/// context is abandoned or lost, and the reclaim flush it starts with speaks to exactly the device that is
/// suspect. These pin that the flush's failure reaches the caller without taking the teardown with it.
/// </summary>
/// <remarks>
/// The teardown destroys state the whole process shares, so each test stands in for that state, tears the
/// stand-in down, and puts the real state back.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class GraphicsContextShutdownTests
{
    [Test]
    public void AFailingReclaimFlush_StillReleasesTheContextItCouldNotFlush()
    {
        var deviceLoss = new InvalidOperationException("The shared context is abandoned.");
        bool abandonedWasDisposed = false;
        var abandoned = new Mock<IGraphicsContext>(MockBehavior.Strict);
        abandoned.SetupGet(context => context.SkiaContext).Throws(deviceLoss);
        abandoned.Setup(context => context.Dispose()).Callback(() => abandonedWasDisposed = true);

        RenderThread.Dispatcher.Invoke(() =>
        {
            // Flush what the live context still owes before standing in for it: a resource left queued
            // would be flushed against the stand-in instead, on a call it never declared.
            GpuResourceReclaimQueue.FlushAndDrain();
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(abandoned.Object, null, null, FailedToInitialize: false));
            var deferred = new TrackedDisposable();
            try
            {
                Assert.That(
                    GpuResourceReclaimQueue.TryDefer(deferred, approximateBytes: 0),
                    Is.True,
                    "the fixture must give the queue something to flush, or the flush never runs");

                Assert.That(
                    GraphicsContextFactory.Shutdown,
                    Throws.Exception.SameAs(deviceLoss),
                    "swallowing the flush failure would hide the device loss that caused it");

                IGraphicsContext? restarted = GraphicsContextFactory.GetOrCreateShared();

                Assert.Multiple(() =>
                {
                    Assert.That(
                        abandonedWasDisposed,
                        Is.True,
                        "the context the flush spoke for is still the one shutdown has to destroy");
                    Assert.That(
                        restarted,
                        Is.Not.SameAs(abandoned.Object),
                        "a context that survives its own shutdown is handed straight back to the next caller");
                });
            }
            finally
            {
                // A no-op once the teardown discharged the queue; kept so a regression that skips the
                // discharge cannot leak the stand-in resource into the rest of the run.
                GpuResourceReclaimQueue.DrainAfterContextSync();
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
                discarded.VulkanInstance?.Dispose();
            }

            Assert.That(deferred.IsDisposed, Is.True, "the queue still owns what the failing flush left");
        });
    }

    [Test]
    public void ASucceedingShutdown_ClearsTheStateItDestroyed()
    {
        bool disposed = false;
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.Setup(value => value.Dispose()).Callback(() => disposed = true);

        RenderThread.Dispatcher.Invoke(() =>
        {
            // Flush what the live context still owes before standing in for it: a resource left queued
            // would be flushed against the stand-in instead, on a call it never declared.
            GpuResourceReclaimQueue.FlushAndDrain();
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(context.Object, null, null, FailedToInitialize: false));
            try
            {
                Assert.That(GraphicsContextFactory.Shutdown, Throws.Nothing);

                Assert.Multiple(() =>
                {
                    Assert.That(disposed, Is.True);
                    Assert.That(GraphicsContextFactory.SharedContext, Is.Null);
                });
            }
            finally
            {
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
                discarded.VulkanInstance?.Dispose();
            }
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
            // Flush what the live context still owes before standing in for it: a resource left queued
            // would be flushed against the stand-in instead, on a call it never declared.
            GpuResourceReclaimQueue.FlushAndDrain();
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(abandoned.Object, null, null, FailedToInitialize: false));
            var deferred = new TrackedDisposable(() => order.Add("queued"));
            try
            {
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
                });
            }
            finally
            {
                // A no-op once the teardown discharged the queue; kept so a regression that skips the
                // discharge cannot leak the stand-in resource into the rest of the run.
                GpuResourceReclaimQueue.DrainAfterContextSync();
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
                discarded.VulkanInstance?.Dispose();
            }
        });
    }

    [Test]
    public void AFailingDischarge_StillReleasesTheContextItCouldNotDischarge()
    {
        var dischargeFailure = new InvalidOperationException("The reclaim queue could not be discharged.");
        bool disposed = false;
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.Setup(value => value.Dispose()).Callback(() => disposed = true);

        RenderThread.Dispatcher.Invoke(() =>
        {
            // Flush what the live context still owes before standing in for it: a resource left queued
            // would be flushed against the stand-in instead, on a call it never declared.
            GpuResourceReclaimQueue.FlushAndDrain();
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(context.Object, null, null, FailedToInitialize: false));
            Action previousDischarge = GraphicsContextFactory.ExchangeReclaimQueueDischarge(
                () => throw dischargeFailure);
            try
            {
                Assert.That(
                    GraphicsContextFactory.Shutdown,
                    Throws.Exception.SameAs(dischargeFailure),
                    "a discharge speaks for the same suspect device the flush does");

                Assert.Multiple(() =>
                {
                    Assert.That(
                        disposed,
                        Is.True,
                        "the discharge added ahead of the release must not become a second way to strand it");
                    Assert.That(
                        GraphicsContextFactory.SharedContext,
                        Is.Null,
                        "a context that survives its own shutdown is handed straight back to the next caller");
                });
            }
            finally
            {
                GraphicsContextFactory.ExchangeReclaimQueueDischarge(previousDischarge);
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
                discarded.VulkanInstance?.Dispose();
            }
        });
    }

    [Test]
    public void AFailingFlushAndDischarge_ReportBothAndStillRelease()
    {
        var deviceLoss = new InvalidOperationException("The shared context is abandoned.");
        var dischargeFailure = new InvalidOperationException("The reclaim queue could not be discharged.");
        bool disposed = false;
        var abandoned = new Mock<IGraphicsContext>(MockBehavior.Strict);
        abandoned.SetupGet(context => context.SkiaContext).Throws(deviceLoss);
        abandoned.Setup(context => context.Dispose()).Callback(() => disposed = true);

        RenderThread.Dispatcher.Invoke(() =>
        {
            // Flush what the live context still owes before standing in for it: a resource left queued
            // would be flushed against the stand-in instead, on a call it never declared.
            GpuResourceReclaimQueue.FlushAndDrain();
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(abandoned.Object, null, null, FailedToInitialize: false));
            Action previousDischarge = GraphicsContextFactory.ExchangeReclaimQueueDischarge(
                () => throw dischargeFailure);
            var deferred = new TrackedDisposable();
            try
            {
                Assert.That(
                    GpuResourceReclaimQueue.TryDefer(deferred, approximateBytes: 0),
                    Is.True,
                    "the fixture must give the queue something to flush, or the flush never runs");

                Assert.That(
                    GraphicsContextFactory.Shutdown,
                    Throws.TypeOf<AggregateException>()
                        .With.Property(nameof(AggregateException.InnerExceptions))
                        .EqualTo(new[] { deviceLoss, dischargeFailure }),
                    "neither failure is the other's to hide, and both speak for the same suspect device");

                Assert.Multiple(() =>
                {
                    Assert.That(
                        disposed,
                        Is.True,
                        "two failures ahead of the release are still not a vote on whether it happens");
                    Assert.That(
                        GraphicsContextFactory.SharedContext,
                        Is.Null,
                        "a context that survives its own shutdown is handed straight back to the next caller");
                });
            }
            finally
            {
                GraphicsContextFactory.ExchangeReclaimQueueDischarge(previousDischarge);
                // The stand-in discharge threw instead of draining, so the queued resource is still ours.
                GpuResourceReclaimQueue.DrainAfterContextSync();
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
                discarded.VulkanInstance?.Dispose();
            }

            Assert.That(deferred.IsDisposed, Is.True, "the queue still owns what the failing discharge left");
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
}
