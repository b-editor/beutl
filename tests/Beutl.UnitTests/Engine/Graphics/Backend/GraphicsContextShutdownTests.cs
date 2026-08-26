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
                // The flush threw before draining, so the queued resource is still the queue's to destroy.
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

    private sealed class TrackedDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
