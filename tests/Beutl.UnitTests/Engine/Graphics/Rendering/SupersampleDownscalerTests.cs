using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// CPU unit tests for the single-source-of-truth downscale kernel (SupersampleDownscaler). No GPU needed, so it
// guards the kernel choice + the same-size fast path in CI even when the Vulkan golden suite is skipped.
public class SupersampleDownscalerTests
{
    private static Bitmap MakeBitmap(int w, int h)
        => new(new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul)));

    // <= 2× uses Mitchell cubic; > 2× switches to trilinear + mipmaps to avoid undersampling (SC-009).
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void SamplingFor_UpToTwoX_IsMitchellCubic(float scale)
    {
        Assert.That(SupersampleDownscaler.SamplingFor(scale).UseCubic, Is.True);
    }

    [TestCase(2.5f)]
    [TestCase(4f)]
    public void SamplingFor_AboveTwoX_IsTrilinearMipmap(float scale)
    {
        SKSamplingOptions s = SupersampleDownscaler.SamplingFor(scale);
        Assert.Multiple(() =>
        {
            Assert.That(s.UseCubic, Is.False);
            Assert.That(s.Filter, Is.EqualTo(SKFilterMode.Linear));
            Assert.That(s.Mipmap, Is.EqualTo(SKMipmapMode.Linear));
        });
    }

    [Test]
    public void ToFrameSize_AlreadyTargetSize_ReturnsSameInstance()
    {
        using Bitmap bmp = MakeBitmap(64, 48);
        Bitmap result = SupersampleDownscaler.ToFrameSize(bmp, new PixelSize(64, 48), 1f);
        Assert.That(ReferenceEquals(result, bmp), Is.True, "same-size resample must be a no-op (no copy)");
    }

    [Test]
    public void ToFrameSize_DifferentSize_ReturnsResizedCopy()
    {
        using Bitmap bmp = MakeBitmap(128, 96);
        using Bitmap result = SupersampleDownscaler.ToFrameSize(bmp, new PixelSize(64, 48), 2f);
        Assert.Multiple(() =>
        {
            Assert.That(ReferenceEquals(result, bmp), Is.False);
            Assert.That(result.Width, Is.EqualTo(64));
            Assert.That(result.Height, Is.EqualTo(48));
        });
    }
}
