using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Self-tests for ImageMetrics on synthetic RgbaF16 bitmaps. No GPU required.
public class ImageMetricsTests
{
    private static Bitmap Flat(int w, int h, float r, float g, float b, float a = 1f)
    {
        var bmp = new Bitmap(w, h, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        Span<ushort> span = bmp.GetPixelSpan<ushort>();
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            span[o] = BitConverter.HalfToUInt16Bits((Half)r);
            span[o + 1] = BitConverter.HalfToUInt16Bits((Half)g);
            span[o + 2] = BitConverter.HalfToUInt16Bits((Half)b);
            span[o + 3] = BitConverter.HalfToUInt16Bits((Half)a);
        }

        return bmp;
    }

    private static Bitmap Checkerboard(int w, int h)
    {
        var bmp = new Bitmap(w, h, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        Span<ushort> span = bmp.GetPixelSpan<ushort>();
        ushort one = BitConverter.HalfToUInt16Bits((Half)1f);
        ushort zero = BitConverter.HalfToUInt16Bits((Half)0f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                ushort v = ((x + y) & 1) == 0 ? one : zero;
                span[o] = v;
                span[o + 1] = v;
                span[o + 2] = v;
                span[o + 3] = one;
            }
        }

        return bmp;
    }

    [Test]
    public void Ssim_Identical_IsOne()
    {
        using var a = Flat(16, 16, 0.4f, 0.6f, 0.8f);
        using var b = Flat(16, 16, 0.4f, 0.6f, 0.8f);
        Assert.That(ImageMetrics.Ssim(a, b), Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void Mae_Identical_IsZero()
    {
        using var a = Flat(16, 16, 0.4f, 0.6f, 0.8f);
        using var b = Flat(16, 16, 0.4f, 0.6f, 0.8f);
        Assert.That(ImageMetrics.MeanAbsoluteError(a, b), Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void Mae_ConstantOffset_EqualsOffset()
    {
        using var a = Flat(16, 16, 0.5f, 0.5f, 0.5f);
        using var b = Flat(16, 16, 0.6f, 0.6f, 0.6f);
        // half precision near 0.5..0.6 is ~1e-3.
        Assert.That(ImageMetrics.MeanAbsoluteError(a, b), Is.EqualTo(0.1).Within(2e-3));
    }

    [Test]
    public void AliasingEnergy_FlatIsZero_CheckerboardIsHigh()
    {
        using var flat = Flat(16, 16, 0.5f, 0.5f, 0.5f);
        using var checker = Checkerboard(16, 16);
        Assert.That(ImageMetrics.AliasingEnergy(flat), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(ImageMetrics.AliasingEnergy(checker), Is.GreaterThan(0.1));
    }

    [Test]
    public void Ssim_DifferentContent_IsLowerThanOne()
    {
        using var flat = Flat(16, 16, 0.5f, 0.5f, 0.5f);
        using var checker = Checkerboard(16, 16);
        Assert.That(ImageMetrics.Ssim(flat, checker), Is.LessThan(0.5));
    }
}
