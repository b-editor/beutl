using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Skia-filter-mode effects (Blur etc.) scale via the root CTM. Empirically checks that the inline path
// produces faithful reduced-scale output for a blurred shape.
[NonParallelizable]
[TestFixture]
public class Slice1SkiaFilterTests
{
    private static readonly PixelSize Frame = new(250, 250);

    private static Drawable.Resource MakeBlurredEllipse()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 150;
        shape.Height.Value = 110;
        shape.Fill.Value = Brushes.White;

        var blur = new Blur();
        blur.Sigma.Value = new Size(8, 8);
        shape.FilterEffect.Value = blur;

        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void BlurredShape_ScaleOne_IsDeterministic()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(MakeBlurredEllipse(), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(MakeBlurredEllipse(), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    [Test]
    public void BlurredShape_HalfScale_UpscaledMatchesFull()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(MakeBlurredEllipse(), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(MakeBlurredEllipse(), Frame, 0.5f);
            GoldenImageHarness.AssertReducedScaleExact(full, half);
        });
    }
}
