using Beutl.Graphics;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// CPU unit tests for the US4 preview-quality -> s_out mapping (RenderScale.ToFloat). Runs without a GPU, so it
// guards the mapping in CI even when the Vulkan golden suite is skipped. The result must always be in (0, 1].
public class RenderScaleTests
{
    private static readonly PixelSize Frame = new(1920, 1080);
    private static readonly Size Preview = new(960, 540);

    [TestCase(RenderScale.Full, 1.0f)]
    [TestCase(RenderScale.Half, 0.5f)]
    [TestCase(RenderScale.Quarter, 0.25f)]
    public void ToFloat_FixedLevels(RenderScale scale, float expected)
    {
        Assert.That(scale.ToFloat(Frame, Preview), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void ToFloat_FitToPreviewer_FitsTheSmallerAxis()
    {
        // 960/1920 = 0.5, 540/1080 = 0.5 -> 0.5
        Assert.That(RenderScale.FitToPreviewer.ToFloat(Frame, Preview), Is.EqualTo(0.5f).Within(1e-4));
    }

    [Test]
    public void ToFloat_FitToPreviewer_NeverUpscalesBeyondFull()
    {
        // previewer larger than the frame would fit at >1; must clamp to 1 (preview never upscales).
        float s = RenderScale.FitToPreviewer.ToFloat(new PixelSize(100, 100), new Size(1000, 1000));
        Assert.That(s, Is.EqualTo(1f).Within(1e-6));
    }

    [TestCase(0, 100)]
    [TestCase(100, 0)]
    public void ToFloat_FitToPreviewer_DegenerateSizes_FallBackToFull(int pw, int ph)
    {
        float s = RenderScale.FitToPreviewer.ToFloat(Frame, new Size(pw, ph));
        Assert.That(s, Is.EqualTo(1f).Within(1e-6));
    }

    [Test]
    public void ToFloat_FitToPreviewer_TinyPreviewer_FloorsAtMinScaleNotZero()
    {
        // 1px previewer onto a huge frame would fit at ~1/6400; the [MinScale, 1] clamp must keep it > 0.
        float s = RenderScale.FitToPreviewer.ToFloat(new PixelSize(6400, 6400), new Size(1, 1));
        Assert.That(s, Is.GreaterThan(0f), "s_out must never be 0 (would crash the renderer)");
        Assert.That(s, Is.EqualTo(1f / 64f).Within(1e-6));
    }
}
