using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderTargetImageLeaseTests
{
    [Test]
    public void RepeatedLeasesAndSharedTargetsRetainTheImageUntilTheLastDisposal()
    {
        using var target = new CpuRenderTarget(8, 8);
        target.RawValue.Canvas.Clear(SKColors.Red);
        using var alias = target.ShallowCopy();
        using var first = target.AcquireImage();
        using var second = alias.AcquireImage();
        target.Dispose();
        alias.Dispose();
        first.Dispose();
        Assert.That(second.Image.Handle, Is.Not.EqualTo(IntPtr.Zero));
        using var bitmap = SKBitmap.FromImage(second.Image);
        Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
        second.Dispose();
        Assert.That(() => second.Image, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void CanvasDrawingDoesNotInvalidateAnOutstandingImage()
    {
        using var source = new CpuRenderTarget(8, 8);
        using var destination = new CpuRenderTarget(8, 8);
        source.RawValue.Canvas.Clear(SKColors.Red);
        using var image = source.AcquireImage();
        using (var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery))
            canvas.DrawRenderTarget(source, default);
        Assert.That(image.Image.Handle, Is.Not.EqualTo(IntPtr.Zero));
        using var pixels = SKBitmap.FromImage(image.Image);
        Assert.That(pixels.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
    }

    [Test]
    public void EarlierImageRemainsImmutableWhenTheTargetChanges()
    {
        using var target = new CpuRenderTarget(8, 8);
        target.RawValue.Canvas.Clear(SKColors.Red);
        using var red = target.AcquireImage();
        target.RawValue.Canvas.Clear(SKColors.Blue);
        using var blue = target.AcquireImage();
        using var first = SKBitmap.FromImage(red.Image);
        using var second = SKBitmap.FromImage(blue.Image);
        Assert.That(first.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
        Assert.That(second.GetPixel(0, 0), Is.EqualTo(SKColors.Blue));
    }
}
