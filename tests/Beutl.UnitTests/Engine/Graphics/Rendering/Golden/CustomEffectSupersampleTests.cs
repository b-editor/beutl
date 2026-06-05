using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// feature 003 (#3 / FR-009 / FR-013): a render-target ("Custom") effect now allocates ceil(bounds × w)
// buffers and scales its absolute-length pixel params by w, so under supersampled export it gains REAL
// device density (crisper) WITHOUT changing the logical appearance (the mosaic tiles stay logical size).
// This is the faithfulness gate the EffectScaleSurvey could not provide — that one passes purely via the
// root CTM and would pass unchanged even if the whole WorkingScale machinery were deleted.
[NonParallelizable]
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

            // 1) LOGICAL appearance preserved: the supersampled-then-downscaled mosaic has the SAME tile
            //    grid as the 1:1 mosaic because tileSize scaled by the working density. Had tileSize NOT
            //    scaled, the 2x render would carry 2x-finer tiles -> a structurally different image -> low SSIM.
            double ssimVs11 = ImageMetrics.Ssim(oneToOne, delivered);
            TestContext.WriteLine($"Mosaic 2x-delivered vs 1:1 SSIM={ssimVs11:F4}");
            Assert.That(ssimVs11, Is.GreaterThan(0.95),
                "supersampled mosaic diverged from 1:1 — tileSize did not scale with the working density");

            // 2) REAL density gain: the supersampled mosaic's tile edges are at least as close to ground
            //    truth as the 1:1 render — i.e. buffer activation actually raised the internal density.
            double maeSS = ImageMetrics.MeanAbsoluteError(delivered, truth);
            double mae11 = ImageMetrics.MeanAbsoluteError(oneToOne, truth);
            TestContext.WriteLine($"Mosaic vs truth: MAE ss={maeSS:F4} 1:1={mae11:F4}");
            Assert.That(maeSS, Is.LessThanOrEqualTo(mae11 + 1e-4),
                "supersampled mosaic not closer to ground truth than 1:1 — buffer activation gave no density");
        });
    }
}
