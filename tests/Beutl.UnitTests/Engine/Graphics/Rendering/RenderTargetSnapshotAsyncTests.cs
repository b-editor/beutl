using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// RenderTarget.SnapshotAsync queues a readback behind the rendering submitted so far and completes once the GPU
// context reports it, instead of making the render thread wait the way Snapshot does.
[NonParallelizable]
[TestFixture]
public class RenderTargetSnapshotAsyncTests
{
    [Test]
    public async Task SnapshotAsync_ReadsTheSamePixelsAsSnapshot()
    {
        VulkanTestEnvironment.EnsureAvailable();
        (RenderTarget target, Bitmap expected, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RenderTarget target = CreateGpuTarget(64, 48);
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(16, 12, 32, 24), Brushes.Resource.White, null);
            }

            return (target, target.Snapshot(), target.SnapshotAsync());
        });

        try
        {
            // Awaited off the render thread, so only the render thread's polling can complete it.
            using Bitmap actual = await pending;
            Assert.That(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()), Is.True);
        }
        finally
        {
            expected.Dispose();
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public async Task SnapshotAsync_ReturnsWhatTheSurfaceHeldWhenRequested()
    {
        VulkanTestEnvironment.EnsureAvailable();
        (RenderTarget target, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RenderTarget target = CreateGpuTarget(16, 16);
            target.Value.Canvas.Clear(SKColors.White);
            Task<Bitmap> pending = target.SnapshotAsync();
            // Drawn after the request, while the GPU may still be working on it.
            target.Value.Canvas.Clear(SKColors.Black);
            return (target, pending);
        });

        try
        {
            using Bitmap requested = await pending;
            using Bitmap current = VulkanTestEnvironment.InvokeOnRenderThread(target.Snapshot);
            Assert.Multiple(() =>
            {
                Assert.That(ReadRed(requested, 8, 8), Is.EqualTo(1f).Within(1e-3f), "the read must keep the white it was queued behind");
                Assert.That(ReadRed(current, 8, 8), Is.EqualTo(0f).Within(1e-3f), "the target itself must hold the later black");
            });
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public async Task SnapshotAsync_SubmitsWithoutWaitingForTheGpu()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var flushes = new List<ImmediateCanvasFlushKind>();
        (RenderTarget target, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GpuResourceReclaimQueue.FlushAndDrain();
            RenderTarget target = CreateGpuTarget(8, 8);
            target.Value.Canvas.Clear(SKColors.CornflowerBlue);
            Task<Bitmap> pending;
            using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            {
                pending = target.SnapshotAsync();
            }

            return (target, pending);
        });

        try
        {
            using Bitmap snapshot = await pending;
            Assert.That(flushes,
                Is.EqualTo(new[] { ImmediateCanvasFlushKind.PrepareForSamplingSubmit }),
                "An asynchronous readback must submit the surface without waiting for the GPU to finish it.");
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public void SnapshotAsync_OnACpuSurface_HasCompletedWhenItReturns()
    {
        // Off the render thread, RenderTarget.Create rasters on the CPU, where Skia reads synchronously.
        using RenderTarget target = RenderTarget.Create(16, 16)
                                    ?? throw new InvalidOperationException("Could not create a raster render target.");
        target.Value.Canvas.Clear(SKColors.White);

        Task<Bitmap> pending = target.SnapshotAsync();

        Assert.That(pending.IsCompletedSuccessfully, Is.True);
        using Bitmap snapshot = pending.Result;
        Assert.That(ReadRed(snapshot, 8, 8), Is.EqualTo(1f).Within(1e-3f));
    }

    private static RenderTarget CreateGpuTarget(int width, int height)
    {
        return RenderTarget.Create(width, height)
               ?? throw new InvalidOperationException("Could not create a GPU render target.");
    }

    private static float ReadRed(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> channels = bitmap.GetPixelSpan<ushort>();
        return (float)BitConverter.UInt16BitsToHalf(channels[((y * bitmap.Width) + x) * 4]);
    }
}
