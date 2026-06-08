using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// T045 (open part) / research D6: does a Rotation3DTransform perspective child render consistently across
// render scales? The S·P ≠ P·S trap: if the root device scale ends up on the wrong side of the projective
// matrix, perspective foreshortening differs per scale. A HIGH reduced-scale SSIM means the root scale is
// already composed correctly (append-after, not prepend-into the W column) and no fix is needed.
[NonParallelizable]
[TestFixture]
public class Perspective3DScaleProbeTests
{
    private static readonly PixelSize Frame = new(250, 250);

    private static Drawable.Resource MakePerspectiveShape()
    {
        // A gradient fill makes the perspective foreshortening visible (a flat color would hide it).
        var brush = new LinearGradientBrush();
        brush.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        brush.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 160;
        shape.Height.CurrentValue = 160;
        shape.Fill.CurrentValue = brush;
        shape.Transform.CurrentValue = new Rotation3DTransform(0f, 55f, 0f, 0f, 0f, 0f) { Depth = { CurrentValue = 400f } };
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void Rotation3DPerspective_ReducedScale_IsConsistent()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(MakePerspectiveShape(), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(MakePerspectiveShape(), Frame, 0.5f);
            using Bitmap upscaled = GoldenImageHarness.MitchellResampleTo(half, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, upscaled);
            double mae = ImageMetrics.MeanAbsoluteError(full, upscaled);
            TestContext.WriteLine($"[Rotation3D 0.5x] SSIM={ssim:F4} MAE={mae:F4}");
            // A perspective S·P≠P·S break would distort foreshortening per scale (gross structural mismatch).
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"Rotation3D perspective diverged across scale: SSIM={ssim:F4}");
        });
    }

    [Test]
    public void Rotation3DPerspective_Supersample_IsConsistent()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(MakePerspectiveShape(), Frame, 1f);
            using Bitmap ss = GoldenImageHarness.RenderAtScale(MakePerspectiveShape(), Frame, 2f);
            using Bitmap down = GoldenImageHarness.MitchellResampleTo(ss, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, down);
            double mae = ImageMetrics.MeanAbsoluteError(full, down);
            TestContext.WriteLine($"[Rotation3D 2.0x] SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"Rotation3D perspective diverged under SSAA: SSIM={ssim:F4}");
        });
    }
}
