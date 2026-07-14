using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[TestFixture]
public class ClippingAutoClipAlphaThresholdTests
{
    [Test]
    public void BlurTailBelowAlpha8Quantization_DoesNotExpandDetectedMargins()
    {
        using var bitmap = new Bitmap(
            5, 1, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        Span<RgbaF16> row = bitmap.GetRow<RgbaF16>(0);
        row[0] = Pixel(0.25f / byte.MaxValue);
        row[1] = Pixel(0.75f / byte.MaxValue);
        row[3] = Pixel(1f);
        row[4] = Pixel(0.25f / byte.MaxValue);

        Thickness? margins = Clipping.FindTransparentMargins(bitmap);

        Assert.That(margins, Is.EqualTo(new Thickness(1, 0, 2, 1)),
            "sub-half-LSB soft tails must quantize to transparent exactly as the legacy Alpha8 scan did");
    }

    private static RgbaF16 Pixel(float alpha)
    {
        Half value = (Half)alpha;
        return new RgbaF16(value, value, value, value);
    }
}
