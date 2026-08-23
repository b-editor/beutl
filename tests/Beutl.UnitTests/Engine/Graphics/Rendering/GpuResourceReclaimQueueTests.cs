using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Skia records a draw from one render target into another without owning the source image, so a
/// GPU target released between recording and submission would leave the driver reading a destroyed
/// image. These tests pin the deferral that keeps the source alive without a per-draw flush.
/// </summary>
[NonParallelizable]
public sealed class GpuResourceReclaimQueueTests
{
    /// <remarks>
    /// Draining flushes the shared context only. A caller about to sample a surface skips its own flush
    /// when told a context-wide flush covered it, so claiming that for a target from a caller-supplied
    /// factory living on another context would let a snapshot read work that was never submitted.
    /// </remarks>
    [Test]
    public void Draining_ForASurfaceOnAnotherContext_DoesNotClaimToHaveFlushedIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext foreign = GraphicsContextFactory.CreateContext();
            using RenderTarget destination = CreateBackendTarget(64, 64);
            using var canvas = new ImmediateCanvas(destination);
            canvas.Clear(Colors.Black);

            RenderTarget source = CreateBackendTarget(32, 32);
            using (var sourceCanvas = new ImmediateCanvas(source))
            {
                sourceCanvas.Clear(Colors.Red);
            }

            GpuResourceReclaimQueue.FlushAndDrain();
            canvas.DrawRenderTargetPixelsWithoutFlush(source, 0, 0);
            source.Dispose();
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0), "precondition");

            bool claimedForeign = GpuResourceReclaimQueue.FlushAndDrain(foreign.SkiaContext);

            Assert.Multiple(() =>
            {
                Assert.That(claimedForeign, Is.False);
                Assert.That(
                    GpuResourceReclaimQueue.PendingCount,
                    Is.Zero,
                    "The queue is drained either way; only the caller's flush is not substituted.");
            });
        });
    }

    [Test]
    public void Draining_ForASurfaceOnTheSharedContext_ReplacesItsOwnFlush()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget destination = CreateBackendTarget(64, 64);
            using var canvas = new ImmediateCanvas(destination);
            canvas.Clear(Colors.Black);

            RenderTarget source = CreateBackendTarget(32, 32);
            using (var sourceCanvas = new ImmediateCanvas(source))
            {
                sourceCanvas.Clear(Colors.Red);
            }

            GpuResourceReclaimQueue.FlushAndDrain();
            canvas.DrawRenderTargetPixelsWithoutFlush(source, 0, 0);
            source.Dispose();
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0), "precondition");

            bool claimedShared = GpuResourceReclaimQueue.FlushAndDrain(
                GraphicsContextFactory.SharedContext!.SkiaContext);

            Assert.That(claimedShared, Is.True);
        });
    }

    [Test]
    public void ReleasingATargetReadByUnsubmittedWork_DefersItsDestruction()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget destination = CreateBackendTarget(64, 64);
            using var canvas = new ImmediateCanvas(destination);
            canvas.Clear(Colors.Black);

            RenderTarget source = CreateBackendTarget(32, 32);
            using (var sourceCanvas = new ImmediateCanvas(source))
            {
                sourceCanvas.Clear(Colors.Red);
            }

            GpuResourceReclaimQueue.FlushAndDrain();
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.Zero, "precondition");

            canvas.DrawRenderTargetPixelsWithoutFlush(source, 0, 0);
            source.Dispose();

            Assert.That(
                GpuResourceReclaimQueue.PendingCount,
                Is.GreaterThan(0),
                "A target still read by unsubmitted work must outlive its last managed reference.");

            using Bitmap _ = destination.Snapshot();

            Assert.That(
                GpuResourceReclaimQueue.PendingCount,
                Is.Zero,
                "Reading the destination back submits and synchronizes, so the source can be destroyed.");
        });
    }

    [Test]
    public void ClosingAFlushingCanvas_DrainsDeferredTargets()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget destination = CreateBackendTarget(64, 64);
            RenderTarget source = CreateBackendTarget(32, 32);
            using (var sourceCanvas = new ImmediateCanvas(source))
            {
                sourceCanvas.Clear(Colors.Red);
            }

            GpuResourceReclaimQueue.FlushAndDrain();

            var canvas = new ImmediateCanvas(destination);
            canvas.Clear(Colors.Black);
            canvas.DrawRenderTargetPixelsWithoutFlush(source, 0, 0);
            source.Dispose();
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0), "precondition");

            canvas.Dispose();

            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.Zero);
        });
    }

    private static RenderTarget CreateBackendTarget(int width, int height)
    {
        RenderTarget? target = RenderTarget.Create(width, height);
        Assert.That(target, Is.Not.Null);
        if (target!.Texture is null)
        {
            target.Dispose();
            Assert.Ignore("The backend fell back to a raster surface, which needs no deferral.");
        }

        return target;
    }
}
