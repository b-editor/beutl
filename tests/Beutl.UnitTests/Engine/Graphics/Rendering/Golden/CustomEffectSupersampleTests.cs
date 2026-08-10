using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Deferred whole-source shaders use canonical PixelRect.FromRect(bounds, w) buffers and scale absolute lengths by w.
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

    private static Drawable.Resource MakeMosaicRoiShape()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative) },
            EndPoint = { CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative) },
        };
        for (int index = 0; index <= 10; index++)
        {
            gradient.GradientStops.Add(new GradientStop(
                index % 2 == 0 ? Colors.Red : Colors.Blue,
                index / 10f));
        }

        var shape = new RectShape
        {
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
            Width = { CurrentValue = 180 },
            Height = { CurrentValue = 160 },
            Fill = { CurrentValue = gradient },
        };
        shape.FilterEffect.CurrentValue = new MosaicEffect
        {
            TileSize = { CurrentValue = new Size(14, 14) },
        };
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

    [Test]
    public void Mosaic_CroppedExecution_MatchesFullRenderInsideRequestedRegion()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var requestedRegion = new Rect(30, 40, 60, 50);
            PixelRect requestedPixels = PixelRect.FromRect(requestedRegion, 1);
            using Bitmap full = GoldenImageHarness.RenderAtScale(
                MakeMosaicRoiShape(),
                Frame,
                1,
                requestedRegion: null);
            using Bitmap cropped = GoldenImageHarness.RenderAtScale(
                MakeMosaicRoiShape(),
                Frame,
                1,
                requestedRegion);
            using Bitmap expected = full.ExtractSubset(requestedPixels);
            using Bitmap actual = cropped.ExtractSubset(requestedPixels);

            double ssim = ImageMetrics.Ssim(expected, actual);
            double mae = ImageMetrics.MeanAbsoluteError(expected, actual);
            Assert.Multiple(() =>
            {
                Assert.That(
                    HasNonBlackRgb(expected),
                    Is.True,
                    "the requested-region fixture must contain visible gradient pixels");
                Assert.That(
                    ssim,
                    Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin),
                    "ROI execution must preserve the complete-frame relative mosaic origin");
                Assert.That(
                    mae,
                    Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax),
                    "ROI execution must keep the full-render tile phase");
            });
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

    // Scale/Rotation displacement transforms must also scale uPivot by w.
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

    [TestCaseSource(nameof(DisplacementTransforms))]
    public void DisplacementMap_CroppedExecution_MatchesFullRenderInsideRequestedRegion(
        Func<DisplacementMapTransform> transformFactory)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var requestedRegion = new Rect(80, 60, 40, 80);
            PixelRect requestedPixels = PixelRect.FromRect(requestedRegion, 1);
            using Bitmap full = GoldenImageHarness.RenderAtScale(
                MakeDisplacedShape(transformFactory()),
                Frame,
                1,
                requestedRegion: null);
            using Bitmap cropped = GoldenImageHarness.RenderAtScale(
                MakeDisplacedShape(transformFactory()),
                Frame,
                1,
                requestedRegion);
            using Bitmap expected = full.ExtractSubset(requestedPixels);
            using Bitmap actual = cropped.ExtractSubset(requestedPixels);

            double ssim = ImageMetrics.Ssim(expected, actual);
            double mae = ImageMetrics.MeanAbsoluteError(expected, actual);
            Assert.Multiple(() =>
            {
                Assert.That(
                    HasNonBlackRgb(expected),
                    Is.True,
                    "the requested-region fixture must contain visible displaced pixels");
                Assert.That(
                    ssim,
                    Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin),
                    "ROI execution must preserve the complete displacement-map layout and pivot");
                Assert.That(
                    mae,
                    Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax),
                    "ROI execution must match the full render inside the requested region");
            });
        });
    }

    private static IEnumerable<TestCaseData> DisplacementTransforms()
    {
        yield return new TestCaseData((Func<DisplacementMapTransform>)(() =>
            new DisplacementMapTranslateTransform
            {
                X = { CurrentValue = 40 },
                Y = { CurrentValue = 40 },
            })).SetName("DisplacementMapTranslate_CroppedExecution_MatchesFullRender");
        yield return new TestCaseData((Func<DisplacementMapTransform>)MakeScaleTransform)
            .SetName("DisplacementMapScale_CroppedExecution_MatchesFullRender");
        yield return new TestCaseData((Func<DisplacementMapTransform>)MakeRotationTransform)
            .SetName("DisplacementMapRotation_CroppedExecution_MatchesFullRender");
    }

    private static bool HasNonBlackRgb(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0 || pixels[index + 1] != 0 || pixels[index + 2] != 0)
                return true;
        }

        return false;
    }
}
