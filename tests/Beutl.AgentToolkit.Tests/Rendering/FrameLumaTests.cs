using Beutl.AgentToolkit.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class FrameLumaTests
{
    [Test]
    public void Near_black_frame_reports_near_black_luma()
    {
        // Regression: an opaque dark pixel used to report ~74 because alpha was averaged in.
        using Bitmap bitmap = CreateUniform(new Bgra8888(15, 15, 15, 255));

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
        using Bitmap green = CreateUniform(new Bgra8888(0, 255, 0, 255));
        using Bitmap blue = CreateUniform(new Bgra8888(0, 0, 255, 255));

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
        using Bitmap opaque = CreateUniform(new Bgra8888(15, 15, 15, 255));
        using Bitmap transparent = CreateUniform(new Bgra8888(15, 15, 15, 0));

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

    private static Bitmap CreateUniformHalf(float r, float g, float b, float a)
    {
        var bitmap = new Bitmap(16, 16, BitmapColorType.RgbaF16, BitmapAlphaType.Premul);
        Span<byte> data = bitmap.GetPixelSpan();
        Span<byte> pixel = stackalloc byte[8];
        BitConverter.TryWriteBytes(pixel[..2], (Half)r);
        BitConverter.TryWriteBytes(pixel[2..4], (Half)g);
        BitConverter.TryWriteBytes(pixel[4..6], (Half)b);
        BitConverter.TryWriteBytes(pixel[6..8], (Half)a);
        for (int offset = 0; offset + 8 <= data.Length; offset += 8)
        {
            pixel.CopyTo(data.Slice(offset, 8));
        }

        return bitmap;
    }

    private static Bitmap CreateUniform(Bgra8888 pixel)
    {
        var bitmap = new Bitmap(16, 16);
        bitmap.GetPixelSpan<Bgra8888>().Fill(pixel);
        return bitmap;
    }
}
