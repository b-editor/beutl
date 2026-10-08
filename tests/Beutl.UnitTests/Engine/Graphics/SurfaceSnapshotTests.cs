using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture, NonParallelizable]
public sealed class SurfaceSnapshotTests
{
    // An unusual size keeps the resource cache from handing back a scratch texture of the same size.
    private const int Width = 509;
    private const int Height = 263;

    [Test]
    public void CrossingBlit_DoesNotLeaveASnapshotCopy()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        if (!SurfaceSnapshot.IsAvailable)
            Assert.Ignore("Only Beutl's macOS libSkiaSharp can release a surface's cached snapshot.");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GRContext skia = context.SkiaContext;
            using RenderTarget source = RenderTarget.Create(Width, Height, RenderTargetPixelFormat.LinearPremultipliedRgba16Float)!;
            using (var canvas = new ImmediateCanvas(source, RenderIntent.Delivery))
                canvas.Clear(Colors.Red);
            using RenderTarget destination = RenderTarget.Create(Width, Height)!;
            skia.Flush(true, true);
            skia.PurgeUnlockedResources(false);
            skia.GetResourceCacheUsage(out _, out long before);

            using (var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery))
                canvas.DrawRenderTarget(source, default);
            skia.Flush(true, true);
            skia.GetResourceCacheUsage(out _, out long after);

            // A copy of the linear source would add its whole RGBA16F texture to the cache.
            Assert.That(after - before, Is.LessThan(Width * Height * 8L));
        });
    }

    [Test]
    public void Release_KeepsTheSourcePixelsAndLaterWrites()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create(Width, Height, RenderTargetPixelFormat.LinearPremultipliedRgba16Float)!;
            using RenderTarget destination = RenderTarget.Create(Width, Height)!;
            using (var canvas = new ImmediateCanvas(source, RenderIntent.Delivery))
                canvas.Clear(Colors.Red);
            using (var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery))
                canvas.DrawRenderTarget(source, default);
            SKColor kept = ReadCenter(source);
            SKColor first = ReadCenter(destination);

            using (var canvas = new ImmediateCanvas(source, RenderIntent.Delivery))
                canvas.Clear(Colors.Blue);
            using (var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery))
                canvas.DrawRenderTarget(source, default);
            SKColor second = ReadCenter(destination);

            Assert.Multiple(() =>
            {
                Assert.That((kept.Red, kept.Blue), Is.EqualTo(((byte)255, (byte)0)));
                Assert.That((first.Red, first.Blue), Is.EqualTo(((byte)255, (byte)0)));
                Assert.That((second.Red, second.Blue), Is.EqualTo(((byte)0, (byte)255)));
            });
        });
    }

    private static SKColor ReadCenter(RenderTarget target)
    {
        using Bitmap snapshot = target.Snapshot();
        using Bitmap encoded = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        return encoded.SKBitmap.GetPixel(Width / 2, Height / 2);
    }
}
