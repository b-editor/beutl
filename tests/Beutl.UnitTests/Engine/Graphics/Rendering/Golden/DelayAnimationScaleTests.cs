using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// DelayAnimationEffect must carry WorkingScale into its nested re-application.
[NonParallelizable]
[TestFixture]
public class DelayAnimationScaleTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static RectShape BaseShape()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 60;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 24f;
        shape.Transform.CurrentValue = rotation;
        return shape;
    }

    private static MosaicEffect MakeMosaic()
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(14, 14);
        return mosaic;
    }

    // Mosaic applied directly to the shape.
    private static Drawable.Resource MakeDirectMosaic()
    {
        var shape = BaseShape();
        shape.FilterEffect.CurrentValue = MakeMosaic();
        return shape.ToResource(CompositionContext.Default);
    }

    // Mosaic applied THROUGH a zero-delay DelayAnimationEffect (a passthrough re-application of the child).
    private static Drawable.Resource MakeDelayWrappedMosaic()
    {
        var shape = BaseShape();
        var delay = new DelayAnimationEffect();
        delay.Delay.CurrentValue = 0f;
        delay.Effect.CurrentValue = MakeMosaic();
        shape.FilterEffect.CurrentValue = delay;
        return shape.ToResource(CompositionContext.Default);
    }

    // A bounds-inflating DropShadow wrapped in DelayAnimation: guards the footprint fix.
    private static Drawable.Resource MakeDelayWrappedDropShadow()
    {
        var shape = BaseShape();
        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(14, 14);
        shadow.Sigma.CurrentValue = new Size(8, 8);
        shadow.Color.CurrentValue = Colors.Black;
        var delay = new DelayAnimationEffect();
        delay.Delay.CurrentValue = 0f;
        delay.Effect.CurrentValue = shadow;
        shape.FilterEffect.CurrentValue = delay;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void DelayWrapped_ScaleOne_IsDeterministic()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // At w==1 the nested path is inert: deterministic render.
            using Bitmap a = GoldenImageHarness.RenderAtScale(MakeDelayWrappedMosaic(), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(MakeDelayWrappedMosaic(), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    [Test]
    public void DelayWrapped_Supersampled_KeepsLogicalResult_AndTracksDirect()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap wrapped1 = GoldenImageHarness.RenderAtScale(MakeDelayWrappedMosaic(), Frame, 1f);
            using Bitmap wrappedHi = GoldenImageHarness.RenderAtScale(MakeDelayWrappedMosaic(), Frame, 2f);
            using Bitmap wrappedDelivered = GoldenImageHarness.MitchellResampleTo(wrappedHi, Frame);

            using Bitmap directHi = GoldenImageHarness.RenderAtScale(MakeDirectMosaic(), Frame, 2f);
            using Bitmap directDelivered = GoldenImageHarness.MitchellResampleTo(directHi, Frame);

            // (1) Supersampled-then-downscaled must match the wrapped 1:1 render.
            double ssimLogical = ImageMetrics.Ssim(wrapped1, wrappedDelivered);
            TestContext.WriteLine($"Delay-wrapped 2x-delivered vs 1:1 SSIM={ssimLogical:F4}");
            Assert.That(ssimLogical, Is.GreaterThan(0.95),
                "delay-wrapped supersample diverged from its own 1:1 — the nested re-application broke the logical result");

            // (2) Wrapped path must track the direct effect at supersample.
            double ssimVsDirect = ImageMetrics.Ssim(wrappedDelivered, directDelivered);
            TestContext.WriteLine($"Delay-wrapped vs direct @2x SSIM={ssimVsDirect:F4}");
            Assert.That(ssimVsDirect, Is.GreaterThan(0.98),
                "delay-wrapped diverged from the direct effect at supersample — nested re-application lost the working density");
        });
    }

    [Test]
    public void DelayWrapped_BoundsInflatingChild_KeepsLogicalSize()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap r1 = GoldenImageHarness.RenderAtScale(MakeDelayWrappedDropShadow(), Frame, 1f);
            using Bitmap hi = GoldenImageHarness.RenderAtScale(MakeDelayWrappedDropShadow(), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);

            // Supersampled result must keep the same logical size as 1:1 (no enlargement/clipping).
            double ssim = ImageMetrics.Ssim(r1, delivered);
            TestContext.WriteLine($"Delay+DropShadow 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                "delay-wrapped bounds-inflating child rendered too large at s_out>1 — the source buffer was stretched to the inflated OriginalBounds");
        });
    }
}
