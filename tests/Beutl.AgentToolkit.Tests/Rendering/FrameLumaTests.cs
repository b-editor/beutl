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

    private static Bitmap CreateUniform(Bgra8888 pixel)
    {
        var bitmap = new Bitmap(16, 16);
        bitmap.GetPixelSpan<Bgra8888>().Fill(pixel);
        return bitmap;
    }
}
