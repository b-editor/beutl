using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// US1 / T026: Skia-filter-mode effects (Blur etc.) scale via the root CTM. This empirically checks
// whether the current inline path already produces faithful reduced-scale output for a blurred shape.
[NonParallelizable]
[TestFixture]
public class Slice1SkiaFilterTests
{
    private static readonly PixelSize Frame = new(250, 250);

    private static Drawable.Resource MakeBlurredEllipse()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 110;
        shape.Fill.CurrentValue = Brushes.White;

        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(8, 8);
        shape.FilterEffect.CurrentValue = blur;

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
