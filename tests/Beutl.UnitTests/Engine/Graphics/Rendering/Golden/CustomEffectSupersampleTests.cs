using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// feature 003 (#3 / FR-009 / FR-013): a render-target ("Custom") effect allocates ceil(bounds × w)
// buffers and scales its absolute-length pixel params by w, so supersampled export gains REAL device
// density without changing logical appearance (mosaic tiles stay logical size). This is the faithfulness
// gate EffectScaleSurvey cannot provide: that one passes purely via the root CTM and would pass unchanged
// even with the WorkingScale machinery deleted.
[NonParallelizable]
[TestFixture]
public class CustomEffectSupersampleTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static Drawable.Resource MakeMosaicShape()
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
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(14, 14);
        shape.FilterEffect.CurrentValue = mosaic;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void Mosaic_Supersampled_KeepsLogicalTiles_AndGainsDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Ground truth: a very high render downsampled to FrameSize.
            using Bitmap truthHi = GoldenImageHarness.RenderAtScale(MakeMosaicShape(), Frame, 8f);
            using Bitmap truth = GoldenImageHarness.MitchellResampleTo(truthHi, Frame);

            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(MakeMosaicShape(), Frame, 1f);
            using Bitmap superHi = GoldenImageHarness.RenderAtScale(MakeMosaicShape(), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(superHi, Frame);

            // 1) LOGICAL appearance preserved: supersampled-then-downscaled mosaic keeps the SAME tile grid
            //    as 1:1 because tileSize scaled by the working density. Unscaled tileSize would give the 2x
            //    render 2x-finer tiles -> a structurally different image -> low SSIM.
            double ssimVs11 = ImageMetrics.Ssim(oneToOne, delivered);
            TestContext.WriteLine($"Mosaic 2x-delivered vs 1:1 SSIM={ssimVs11:F4}");
            Assert.That(ssimVs11, Is.GreaterThan(0.95),
                "supersampled mosaic diverged from 1:1 — tileSize did not scale with the working density");

            // 2) REAL density gain: the supersampled mosaic's tile edges are at least as close to ground
            //    truth as 1:1 — buffer activation actually raised the internal density.
            double maeSS = ImageMetrics.MeanAbsoluteError(delivered, truth);
            double mae11 = ImageMetrics.MeanAbsoluteError(oneToOne, truth);
            TestContext.WriteLine($"Mosaic vs truth: MAE ss={maeSS:F4} 1:1={mae11:F4}");
            Assert.That(maeSS, Is.LessThan(mae11),
                "supersampled mosaic not strictly closer to ground truth than 1:1 — buffer activation gave no density");
        });
    }

    // A spatially-varying displacement map (default RadialGradientBrush) plus a non-zero translate — the
    // case a constant-map control cannot catch: the map is laid out in LOGICAL space but cross-sampled at
    // the base's device-px coord, so without the per-effect local-matrix x w the warp is misaligned/zoomed
    // at w != 1 (a structurally different image, not a denser one).
    private static Drawable.Resource MakeDisplacedShape()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 40;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 21f;
        shape.Transform.CurrentValue = rotation;

        var effect = new DisplacementMapEffect();          // default DisplacementMap = RadialGradientBrush (spatially varying)
        var transform = new DisplacementMapTranslateTransform();
        transform.X.CurrentValue = 40;
        transform.Y.CurrentValue = 40;
        effect.Transform.CurrentValue = transform;
        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void DisplacementMap_Supersampled_KeepsLogicalWarp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(MakeDisplacedShape(), Frame, 1f);
            using Bitmap superHi = GoldenImageHarness.RenderAtScale(MakeDisplacedShape(), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(superHi, Frame);

            // The supersampled-then-downscaled warp must be the SAME logical image as 1:1, since the
            // displacement map shares the base texture's coord space. The map-vs-base sampling-space bug
            // drops this well below 0.95 (empirically ~0.84) for a spatially-varying map.
            double ssim = ImageMetrics.Ssim(oneToOne, delivered);
            TestContext.WriteLine($"DisplacementMap 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                "supersampled displacement warp diverged from 1:1 — the displacement map is sampled in the wrong space at w != 1");
        });
    }

    // feature 003: the Scale and Rotation displacement transforms carry the same device-space uPivot × w
    // logic as Translate (DisplacementMapTransform.cs), but only Translate was covered. A non-zero Center
    // exercises the pivot: an unscaled uPivot would pivot the warp around the wrong device point at w != 1,
    // making the supersampled-then-downscaled image diverge from the 1:1 warp.
    private static Drawable.Resource MakeDisplacedShape(DisplacementMapTransform transform)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 40;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 21f;
        shape.Transform.CurrentValue = rotation;

        var effect = new DisplacementMapEffect();          // default DisplacementMap = RadialGradientBrush (spatially varying)
        effect.Transform.CurrentValue = transform;
        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static DisplacementMapScaleTransform MakeScaleTransform()
    {
        var t = new DisplacementMapScaleTransform();
        t.ScaleX.CurrentValue = 160f;
        t.ScaleY.CurrentValue = 70f;
        t.CenterX.CurrentValue = 35f;   // device-space pivot -> × w
        t.CenterY.CurrentValue = 20f;
        return t;
    }

    private static DisplacementMapRotationTransform MakeRotationTransform()
    {
        var t = new DisplacementMapRotationTransform();
        t.Rotation.CurrentValue = 35f;
        t.CenterX.CurrentValue = 35f;   // device-space pivot -> × w
        t.CenterY.CurrentValue = 20f;
        return t;
    }

    [Test]
    public void DisplacementMapScale_Supersampled_KeepsLogicalWarp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(MakeDisplacedShape(MakeScaleTransform()), Frame, 1f);
            using Bitmap superHi = GoldenImageHarness.RenderAtScale(MakeDisplacedShape(MakeScaleTransform()), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(superHi, Frame);

            double ssim = ImageMetrics.Ssim(oneToOne, delivered);
            TestContext.WriteLine($"DisplacementMapScale 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                "supersampled scale-displacement warp diverged from 1:1 — the pivot is sampled in the wrong space at w != 1");
        });
    }

    [Test]
    public void DisplacementMapRotation_Supersampled_KeepsLogicalWarp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(MakeDisplacedShape(MakeRotationTransform()), Frame, 1f);
            using Bitmap superHi = GoldenImageHarness.RenderAtScale(MakeDisplacedShape(MakeRotationTransform()), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(superHi, Frame);

            double ssim = ImageMetrics.Ssim(oneToOne, delivered);
            TestContext.WriteLine($"DisplacementMapRotation 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                "supersampled rotation-displacement warp diverged from 1:1 — the pivot is sampled in the wrong space at w != 1");
        });
    }
}
