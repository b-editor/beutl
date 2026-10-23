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
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 150;
        shape.Height.Value = 60;
        shape.Fill.Value = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.Value = 24f;
        shape.Transform.Value = rotation;
        return shape;
    }

    private static MosaicEffect MakeMosaic()
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.Value = new Size(14, 14);
        return mosaic;
    }

    // Mosaic applied directly to the shape.
    private static Drawable.Resource MakeDirectMosaic()
    {
        var shape = BaseShape();
        shape.FilterEffect.Value = MakeMosaic();
        return shape.ToResource(CompositionContext.Default);
    }

    // Mosaic applied THROUGH a zero-delay DelayAnimationEffect (a passthrough re-application of the child).
    private static Drawable.Resource MakeDelayWrappedMosaic()
    {
        var shape = BaseShape();
        var delay = new DelayAnimationEffect();
        delay.Delay.Value = 0f;
        delay.Effect.Value = MakeMosaic();
        shape.FilterEffect.Value = delay;
        return shape.ToResource(CompositionContext.Default);
    }

    // A bounds-inflating DropShadow wrapped in DelayAnimation: guards the footprint fix.
    private static Drawable.Resource MakeDelayWrappedDropShadow()
    {
        var shape = BaseShape();
        var shadow = new DropShadow();
        shadow.Position.Value = new Point(14, 14);
        shadow.Sigma.Value = new Size(8, 8);
        shadow.Color.Value = Colors.Black;
        var delay = new DelayAnimationEffect();
        delay.Delay.Value = 0f;
        delay.Effect.Value = shadow;
        shape.FilterEffect.Value = delay;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void DelayWrapped_Supersampled_KeepsLogicalResult_AndTracksDirect()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource wrapped1Resource = MakeDelayWrappedMosaic();
            using Bitmap wrapped1 = GoldenImageHarness.RenderAtScale(wrapped1Resource, Frame, 1f);
            using Drawable.Resource wrappedHiResource = MakeDelayWrappedMosaic();
            using Bitmap wrappedHi = GoldenImageHarness.RenderAtScale(wrappedHiResource, Frame, 2f);
            using Bitmap wrappedDelivered = GoldenImageHarness.MitchellResampleTo(wrappedHi, Frame);

            using Drawable.Resource directHiResource = MakeDirectMosaic();
            using Bitmap directHi = GoldenImageHarness.RenderAtScale(directHiResource, Frame, 2f);
            using Bitmap directDelivered = GoldenImageHarness.MitchellResampleTo(directHi, Frame);

            // (1) Supersampled-then-downscaled must match the wrapped 1:1 render.
            double ssimLogical = ImageMetrics.Ssim(wrapped1, wrappedDelivered);
            Assert.That(ssimLogical, Is.GreaterThan(0.95),
                "delay-wrapped supersample diverged from its own 1:1 — the nested re-application broke the logical result");

            // (2) Wrapped path must track the direct effect at supersample.
            double ssimVsDirect = ImageMetrics.Ssim(wrappedDelivered, directDelivered);
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
            using Drawable.Resource r1Resource = MakeDelayWrappedDropShadow();
            using Bitmap r1 = GoldenImageHarness.RenderAtScale(r1Resource, Frame, 1f);
            using Drawable.Resource hiResource = MakeDelayWrappedDropShadow();
            using Bitmap hi = GoldenImageHarness.RenderAtScale(hiResource, Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);

            // Supersampled result must keep the same logical size as 1:1 (no enlargement/clipping).
            double ssim = ImageMetrics.Ssim(r1, delivered);
            Assert.That(ssim, Is.GreaterThan(0.95),
                "delay-wrapped bounds-inflating child rendered too large at s_out>1 — the source buffer was stretched to the inflated OriginalBounds");
        });
    }
}
