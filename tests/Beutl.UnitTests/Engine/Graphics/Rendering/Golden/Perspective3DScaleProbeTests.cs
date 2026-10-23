using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A perspective child must render the same at every scale; a root scale applied on the wrong side of the
// projective matrix would change the foreshortening.
[NonParallelizable]
[TestFixture]
public class Perspective3DScaleProbeTests
{
    private static readonly PixelSize Frame = new(250, 250);

    private static Drawable.Resource MakePerspectiveShape()
    {
        // A gradient fill makes foreshortening visible; a flat color would hide it.
        var brush = new LinearGradientBrush();
        brush.StartPoint.Value = new RelativePoint(0, 0, RelativeUnit.Relative);
        brush.EndPoint.Value = new RelativePoint(1, 1, RelativeUnit.Relative);
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1));

        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 160;
        shape.Height.Value = 160;
        shape.Fill.Value = brush;
        shape.Transform.Value = new Rotation3DTransform(0f, 55f, 0f, 0f, 0f, 0f) { Depth = { Value = 400f } };
        return shape.ToResource(CompositionContext.Default);
    }

    [TestCase(0.5f, TestName = "Rotation3DPerspective_ReducedScale_IsConsistent")]
    [TestCase(2f, TestName = "Rotation3DPerspective_Supersample_IsConsistent")]
    public void Rotation3DPerspective_IsConsistentAcrossScales(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource fullResource = MakePerspectiveShape();
            using Bitmap full = GoldenImageHarness.RenderAtScale(fullResource, Frame, 1f);
            using Drawable.Resource scaledResource = MakePerspectiveShape();
            using Bitmap scaled = GoldenImageHarness.RenderAtScale(scaledResource, Frame, scale);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(
                scaled,
                new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, delivered);
            double mae = ImageMetrics.MeanAbsoluteError(full, delivered);
            TestContext.WriteLine($"[Rotation3D {scale:F1}x] SSIM={ssim:F4} MAE={mae:F4}");
            // An S·P≠P·S break would distort foreshortening per scale, a gross structural mismatch.
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"Rotation3D perspective diverged at scale {scale}: SSIM={ssim:F4}");
        });
    }
}
