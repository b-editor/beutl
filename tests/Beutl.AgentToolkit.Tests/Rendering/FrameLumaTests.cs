using Beutl.AgentToolkit.Rendering;
using Beutl.Media;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class FrameLumaTests
{
    [Test]
    public void Near_black_frame_reports_near_black_luma()
    {
        // Regression: an opaque dark pixel used to report ~74 because alpha was averaged in.
        float dark = SrgbToLinear(15f / 255f);
        using Bitmap bitmap = CreateUniformHalf(dark, dark, dark, 1);

        StillFrameVisibilityAnalysis analysis = StillRenderer.AnalyzeFrameVisibility(bitmap);

        Assert.Multiple(() =>
        {
            Assert.That(analysis.MeanLuma, Is.EqualTo(15).Within(1));
            Assert.That(analysis.BackgroundLuma, Is.EqualTo(15).Within(1));
        });
    }

    [Test]
    public void Luma_is_weighted_so_green_reads_brighter_than_blue()
    {
        using Bitmap green = CreateUniformHalf(0, 1, 0, 1);
        using Bitmap blue = CreateUniformHalf(0, 0, 1, 1);

        double greenLuma = StillRenderer.AnalyzeFrameVisibility(green).MeanLuma;
        double blueLuma = StillRenderer.AnalyzeFrameVisibility(blue).MeanLuma;

        Assert.Multiple(() =>
        {
            Assert.That(greenLuma, Is.EqualTo(182).Within(2));
            Assert.That(blueLuma, Is.EqualTo(18).Within(2));
        });
    }

    [Test]
    public void Transparent_pixels_do_not_raise_luma()
    {
        float dark = SrgbToLinear(15f / 255f);
        using Bitmap opaque = CreateUniformHalf(dark, dark, dark, 1);
        using Bitmap transparent = CreateUniformHalf(dark, dark, dark, 0);

        Assert.That(
            StillRenderer.AnalyzeFrameVisibility(transparent).MeanLuma,
            Is.EqualTo(StillRenderer.AnalyzeFrameVisibility(opaque).MeanLuma).Within(0.001));
    }

    [Test]
    public void A_barely_visible_half_float_pixel_does_not_read_as_fully_lit()
    {
        // Premultiplied half-floats carry their own coverage; un-premultiplying them would turn a
        // 1%-alpha edge into a full-brightness pixel and inflate the foreground metrics.
        using Bitmap faint = CreateUniformHalf(0.01f, 0.01f, 0.01f, 0.01f);
        using Bitmap opaque = CreateUniformHalf(1f, 1f, 1f, 1f);

        double faintLuma = StillRenderer.AnalyzeFrameVisibility(faint).MeanLuma;
        double opaqueLuma = StillRenderer.AnalyzeFrameVisibility(opaque).MeanLuma;

        Assert.Multiple(() =>
        {
            Assert.That(opaqueLuma, Is.EqualTo(255).Within(1));
            Assert.That(faintLuma, Is.LessThan(40));
        });
    }

    [Test]
    public void Half_float_visibility_luma_is_monotonic()
    {
        float gray128 = SrgbToLinear(128f / 255f);
        using Bitmap black = CreateUniformHalf(0, 0, 0, 1);
        using Bitmap gray = CreateUniformHalf(gray128, gray128, gray128, 1);
        using Bitmap white = CreateUniformHalf(1, 1, 1, 1);

        StillFrameVisibilityAnalysis blackAnalysis = StillRenderer.AnalyzeFrameVisibility(black);
        StillFrameVisibilityAnalysis grayAnalysis = StillRenderer.AnalyzeFrameVisibility(gray);
        StillFrameVisibilityAnalysis whiteAnalysis = StillRenderer.AnalyzeFrameVisibility(white);

        Assert.Multiple(() =>
        {
            Assert.That(whiteAnalysis.MaxLuma, Is.GreaterThanOrEqualTo(250));
            Assert.That(whiteAnalysis.MaxLuma, Is.GreaterThan(grayAnalysis.MaxLuma));
            Assert.That(grayAnalysis.MaxLuma, Is.GreaterThan(blackAnalysis.MaxLuma));
        });
    }

    [Test]
    public void Half_float_white_foreground_is_detected_against_srgb_188_background()
    {
        float gray188 = SrgbToLinear(188f / 255f);
        using Bitmap bitmap = CreateHalfFloatBitmap(64, 64);
        FillHalfFloatRect(bitmap, new PixelRect(0, 0, 64, 64), gray188, gray188, gray188, 1);
        FillHalfFloatRect(bitmap, new PixelRect(24, 24, 16, 16), 1, 1, 1, 1);

        StillFrameVisibilityAnalysis analysis = StillRenderer.AnalyzeFrameVisibility(bitmap);

        Assert.Multiple(() =>
        {
            Assert.That(analysis.MaxLuma, Is.GreaterThanOrEqualTo(250));
            Assert.That(analysis.ForegroundPixels, Is.EqualTo(16 * 16));
            Assert.That(
                analysis.Warnings,
                Has.None.Contains("very low visual contrast"),
                "A white foreground over sRGB 188 has substantial contrast.");
        });
    }

    private static Bitmap CreateHalfFloatBitmap(int width, int height)
        => new(width, height, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

    private static Bitmap CreateUniformHalf(float r, float g, float b, float a)
    {
        Bitmap bitmap = CreateHalfFloatBitmap(16, 16);
        FillHalfFloatRect(bitmap, new PixelRect(0, 0, bitmap.Width, bitmap.Height), r, g, b, a);
        return bitmap;
    }

    private static void FillHalfFloatRect(Bitmap bitmap, PixelRect rect, float r, float g, float b, float a)
    {
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            Span<Half> row = bitmap.GetRow<Half>(y);
            for (int x = rect.X; x < rect.Right; x++)
            {
                int offset = x * 4;
                row[offset] = (Half)r;
                row[offset + 1] = (Half)g;
                row[offset + 2] = (Half)b;
                row[offset + 3] = (Half)a;
            }
        }
    }

    private static float SrgbToLinear(float value)
        => value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
}
