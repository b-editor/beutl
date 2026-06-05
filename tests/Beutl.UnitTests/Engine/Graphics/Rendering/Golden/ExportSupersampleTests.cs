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
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(supersampledHi, Frame);

            // Export delivers exactly FrameSize (FR-026).
            Assert.That(delivered.Width, Is.EqualTo(Frame.Width), "delivered width == FrameSize");
            Assert.That(delivered.Height, Is.EqualTo(Frame.Height), "delivered height == FrameSize");

            double ssimSS = ImageMetrics.Ssim(delivered, truth);
            double ssim11 = ImageMetrics.Ssim(oneToOne, truth);
            double maeSS = ImageMetrics.MeanAbsoluteError(delivered, truth);
            double mae11 = ImageMetrics.MeanAbsoluteError(oneToOne, truth);
            TestContext.WriteLine($"vs truth @ {factor}x: SSIM ss={ssimSS:F4} 1:1={ssim11:F4} | MAE ss={maeSS:F4} 1:1={mae11:F4}");

            // Supersampled output is at least as close to ground truth as the 1:1 render (SC-009).
            Assert.That(maeSS, Is.LessThanOrEqualTo(mae11 + 1e-4), "supersampled MAE-to-truth not <= 1:1");
        });
    }
}
