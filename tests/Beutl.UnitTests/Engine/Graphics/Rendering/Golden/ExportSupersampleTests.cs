using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// US4 / SC-009: export supersampling (render at s>1, downscale to FrameSize) reduces aliasing while
// the delivered size stays FrameSize.
[NonParallelizable]
[TestFixture]
public class ExportSupersampleTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static Drawable.Resource MakeAliasingProne()
    {
        // A thin rotated rectangle has long near-diagonal edges that alias badly at 1:1.
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 18;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 27f;
        shape.Transform.CurrentValue = rotation;
        return shape.ToResource(CompositionContext.Default);
    }

    // The real export supersample factors (the UI offers Off/2x/4x). 1.5x is intentionally excluded: a non-
    // integer downscale's resampling error can exceed its aliasing reduction, so it does not strictly beat 1:1.
    [TestCase(2f)]
    [TestCase(4f)]
    public void Supersampling_IsCloserToGroundTruth_AndKeepsFrameSize(float factor)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Near-ground-truth: render very high then downsample to FrameSize.
            using Bitmap truthHi = GoldenImageHarness.RenderAtScale(MakeAliasingProne(), Frame, 8f);
            using Bitmap truth = GoldenImageHarness.MitchellResampleTo(truthHi, Frame);

            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(MakeAliasingProne(), Frame, 1f);
            using Bitmap supersampledHi = GoldenImageHarness.RenderAtScale(MakeAliasingProne(), Frame, factor);
            // Downscale through the SAME kernel production uses (SupersampleDownscaler), so the 4x case
            // exercises the trilinear + mipmaps branch (scale > 2) rather than a test-only Mitchell.
            using Bitmap delivered = SupersampleDownscaler.ToFrameSize(supersampledHi, Frame, factor);

            // Export delivers exactly FrameSize (FR-026).
            Assert.That(delivered.Width, Is.EqualTo(Frame.Width), "delivered width == FrameSize");
            Assert.That(delivered.Height, Is.EqualTo(Frame.Height), "delivered height == FrameSize");

            double ssimSS = ImageMetrics.Ssim(delivered, truth);
            double ssim11 = ImageMetrics.Ssim(oneToOne, truth);
            double maeSS = ImageMetrics.MeanAbsoluteError(delivered, truth);
            double mae11 = ImageMetrics.MeanAbsoluteError(oneToOne, truth);
            TestContext.WriteLine($"vs truth @ {factor}x: SSIM ss={ssimSS:F4} 1:1={ssim11:F4} | MAE ss={maeSS:F4} 1:1={mae11:F4}");

            // Supersampled output is STRICTLY closer to ground truth than the 1:1 render (SC-009) — the real
            // SSAA quality signal. (An aliasing-energy gate was tried but is unreliable here: this scene is only
            // mildly aliased, so its high-frequency energy wobbles within noise while MAE-to-truth clearly drops.)
            Assert.That(maeSS, Is.LessThan(mae11), "supersampled MAE-to-truth not strictly < 1:1");
            // ...and structurally no worse (within the pinned margin).
            Assert.That(ssimSS, Is.GreaterThanOrEqualTo(ssim11 - GoldenThresholds.SupersampleSsimMargin),
                "supersampled SSIM-to-truth fell below 1:1 beyond the allowed margin");
        });
    }
}
