using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderNodeRendererDeviceBoundsTests
{
    [Test]
    public void Rasterize_FractionalOriginReportsTheBoundsTheBitmapActuallyOccupies()
    {
        var bounds = new Rect(10.25f, 20.25f, 3.5f, 2.5f);
        const float outputScale = 2;
        using RenderNode root = ScaleRecordingTestHelper.Source(EffectiveScale.At(1), bounds);
        var factory = new CpuTargetFactory();
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = outputScale,
                    MaxWorkingScale = 2,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The fractional-origin fixture must produce a bitmap.");
        PixelRect reportedDeviceBounds = PixelRect.FromRect(rasterization.Bounds, outputScale);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds.X * outputScale, Is.EqualTo((float)reportedDeviceBounds.X),
                "The reported origin must land on a device pixel.");
            Assert.That(rasterization.Bounds.Y * outputScale, Is.EqualTo((float)reportedDeviceBounds.Y),
                "The reported origin must land on a device pixel.");
            Assert.That(rasterization.Bounds.Width * outputScale, Is.EqualTo((float)bitmap.Width),
                "The reported width must be the width of the returned pixels.");
            Assert.That(rasterization.Bounds.Height * outputScale, Is.EqualTo((float)bitmap.Height),
                "The reported height must be the height of the returned pixels.");
            Assert.That(rasterization.Bounds.Contains(bounds), Is.True,
                "The reported bounds must cover the selected logical output.");
        });
    }

    [Test]
    public void Rasterize_SelectionWhoseScaledEdgesCollapseStillProducesItsDevicePixel()
    {
        // 2^24 is the float magnitude at which adding 0.5 no longer changes the value, so the selection's
        // right edge is indistinguishable from its left edge.
        const float origin = 16777216f;
        var bounds = new Rect(origin, 0, 0.5f, 4);
        Assert.That(bounds.Right, Is.EqualTo(bounds.X), "the fixture requires a float-collapsed right edge");

        using RenderNode root = ScaleRecordingTestHelper.Source(EffectiveScale.At(1), bounds);
        var factory = new CpuTargetFactory();
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("A selection with positive area must produce a bitmap.");

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(bitmap.Width, Is.EqualTo(1));
            Assert.That(bitmap.Height, Is.EqualTo(4));
            Assert.That(rasterization.Bounds, Is.EqualTo(new Rect(origin, 0, 1, 4)));
        });
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                                   deviceSize.Width,
                                   deviceSize.Height,
                                   SKColorType.RgbaF16,
                                   SKAlphaType.Premul,
                                   SKColorSpace.CreateSrgbLinear()))
                               ?? throw new InvalidOperationException(
                                   "Could not create the CPU device-bounds test surface.");
            return new CpuRenderTarget(surface, deviceSize);
        }

        private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
            : RenderTarget(surface, size.Width, size.Height);
    }
}
