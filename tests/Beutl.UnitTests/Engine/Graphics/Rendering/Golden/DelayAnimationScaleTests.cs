using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// feature 003: DelayAnimationEffect re-applies its child effect through a NESTED FilterEffectContext +
// FilterEffectActivator. Those must carry the working density (WorkingScale) so a delay-wrapped buffer
// effect behaves the same as the un-wrapped effect under the resolution pipeline — byte-identical at
// scale 1, logical-equivalent under supersampling, and consistent with the direct effect (the buffer is
// not silently collapsed to w=1 inside the nested re-application). Guards the WorkingScale-threading fix.
[NonParallelizable]
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

    [Test]
    public void DelayWrapped_ScaleOne_IsDeterministic()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // The nested re-application at w == 1 keeps the pre-feature path, so the same scene renders
            // byte-identically twice (the scale-1 byte-identity anchor for the delay-wrapped path).
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

            // (1) the delay-wrapped supersampled-then-downscaled result keeps the SAME logical image as the
            //     wrapped 1:1 render — the nested re-application did not corrupt the logical appearance.
            double ssimLogical = ImageMetrics.Ssim(wrapped1, wrappedDelivered);
            TestContext.WriteLine($"Delay-wrapped 2x-delivered vs 1:1 SSIM={ssimLogical:F4}");
            Assert.That(ssimLogical, Is.GreaterThan(0.95),
                "delay-wrapped supersample diverged from its own 1:1 — the nested re-application broke the logical result");

            // (2) the wrapped path TRACKS the un-wrapped effect at supersample: because WorkingScale is carried
            //     into the nested FilterEffectContext/activator, the delay-wrapped buffer effect is not collapsed
            //     to w = 1, so it matches the direct effect (a regression dropping WorkingScale would diverge here).
            double ssimVsDirect = ImageMetrics.Ssim(wrappedDelivered, directDelivered);
            TestContext.WriteLine($"Delay-wrapped vs direct @2x SSIM={ssimVsDirect:F4}");
            Assert.That(ssimVsDirect, Is.GreaterThan(0.98),
                "delay-wrapped diverged from the direct effect at supersample — nested re-application lost the working density");
        });
    }
}
