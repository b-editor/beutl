using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Self-tests for ImageMetrics on synthetic RgbaF16 bitmaps. No GPU required.
[TestFixture]
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
    public void AlphaMeanAbsoluteError_Identical_IsZero()
    {
        using var a = Flat(16, 16, 0.4f, 0.6f, 0.8f, a: 0.7f);
        using var b = Flat(16, 16, 0.4f, 0.6f, 0.8f, a: 0.7f);

        Assert.That(ImageMetrics.AlphaMeanAbsoluteError(a, b), Is.EqualTo(0.0).Within(1e-6));
    }

    // The parity-gate hole this metric closes: RGB MAE and luminance SSIM both read only the color channels, so an
    // alpha-only regression sails through them.
    [Test]
    public void AlphaMeanAbsoluteError_SeesAlphaOnlyDrift_ThatRgbMetricsMiss()
    {
        using var a = Flat(16, 16, 0.2f, 0.4f, 0.6f, a: 1f);
        using var b = Flat(16, 16, 0.2f, 0.4f, 0.6f, a: 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(ImageMetrics.MeanAbsoluteError(a, b), Is.EqualTo(0.0).Within(1e-6),
                "RGB MAE is blind to an alpha-only difference");
            Assert.That(ImageMetrics.Ssim(a, b), Is.EqualTo(1.0).Within(1e-6),
                "luminance SSIM is blind to an alpha-only difference");
            Assert.That(ImageMetrics.AlphaMeanAbsoluteError(a, b), Is.EqualTo(0.5).Within(2e-3),
                "the alpha metric must expose the drift the RGB metrics miss");
        });
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

    [Test]
    public void FirstNonFinite_AllFinite_IsNull()
    {
        using var a = Flat(8, 8, 0.4f, 0.6f, 0.8f);
        using var b = Checkerboard(8, 8);
        Assert.That(ImageMetrics.FirstNonFinite(("a", a), ("b", b)), Is.Null);
    }

    [Test]
    public void FirstNonFinite_ReportsNanPixelAndLabel()
    {
        using var clean = Flat(8, 8, 0.5f, 0.5f, 0.5f);
        using var dirty = Flat(8, 8, 0.5f, 0.5f, 0.5f);
        // NaN in the green channel of pixel (3, 2), mirroring a GPU blur emitting a non-finite pixel.
        Span<ushort> px = dirty.GetPixelSpan<ushort>();
        px[(2 * 8 + 3) * 4 + 1] = BitConverter.HalfToUInt16Bits(Half.NaN);

        string? result = ImageMetrics.FirstNonFinite(("clean", clean), ("dirty", dirty));
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("dirty").And.Contain("x=3").And.Contain("y=2"));
    }

    [Test]
    public void Ssim_NonFiniteInput_IsNaN()
    {
        // Why the parity test gates on FirstNonFinite: one non-finite pixel poisons SSIM to NaN,
        // which would otherwise surface as a misleading "scale diverged" failure.
        using var clean = Flat(8, 8, 0.5f, 0.5f, 0.5f);
        using var dirty = Flat(8, 8, 0.5f, 0.5f, 0.5f);
        Span<ushort> px = dirty.GetPixelSpan<ushort>();
        px[0] = BitConverter.HalfToUInt16Bits(Half.PositiveInfinity);
        Assert.That(double.IsNaN(ImageMetrics.Ssim(clean, dirty)), Is.True);
    }

    [Test]
    public void WindowedSsim_Identical_IsOne()
    {
        using var a = Checkerboard(64, 64);
        using var b = Checkerboard(64, 64);
        Assert.That(ImageMetrics.WindowedSsim(a, b, 16), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void WindowedSsim_CatchesLocalizedDefect_GlobalSsimDilutesIt()
    {
        // The failure mode global single-window SSIM cannot see: a small localized defect (a 14×14 flat-gray
        // block in a 128×128 checkerboard, < 1.2% of pixels) leaves global SSIM high because its mean/variance
        // is dominated by the matching background, yet windowed (min-tile) SSIM craters on the defect tile.
        // This is the localized-defect sensitivity the parity gate's global SSIM lacks.
        using Bitmap baseImg = Checkerboard(128, 128);
        using Bitmap defect = Checkerboard(128, 128);
        Span<ushort> span = defect.GetPixelSpan<ushort>();
        ushort gray = BitConverter.HalfToUInt16Bits((Half)0.5f);
        for (int y = 0; y < 14; y++)
        {
            for (int x = 0; x < 14; x++)
            {
                int o = (y * 128 + x) * 4;
                span[o] = span[o + 1] = span[o + 2] = gray;
            }
        }

        double global = ImageMetrics.Ssim(baseImg, defect);
        double windowed = ImageMetrics.WindowedSsim(baseImg, defect, 16);
        Assert.That(global, Is.GreaterThan(0.95),
            "the global single-window SSIM dilutes a small localized defect into the matching background");
        Assert.That(windowed, Is.LessThan(global), "windowed SSIM is strictly more sensitive to a localized defect");
        Assert.That(windowed, Is.LessThan(0.95),
            "windowed SSIM must catch the localized defect the global metric misses");
    }
}
