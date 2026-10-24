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
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 150;
        shape.Height.Value = 60;
        shape.Fill.Value = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.Value = 24f;
        shape.Transform.Value = rotation;
        var mosaic = new MosaicEffect();
        mosaic.TileSize.Value = new Size(14, 14);
        shape.FilterEffect.Value = mosaic;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeMosaicRoiShape()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 1, RelativeUnit.Relative) },
        };
        for (int index = 0; index <= 10; index++)
        {
            gradient.GradientStops.Add(new GradientStop(
                index % 2 == 0 ? Colors.Red : Colors.Blue,
                index / 10f));
        }

        var shape = new RectShape
        {
            AlignmentX = { Value = AlignmentX.Center },
            AlignmentY = { Value = AlignmentY.Center },
            Width = { Value = 180 },
            Height = { Value = 160 },
            Fill = { Value = gradient },
        };
        shape.FilterEffect.Value = new MosaicEffect
        {
            TileSize = { Value = new Size(14, 14) },
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
            using Drawable.Resource truthHiResource = MakeMosaicShape();
            using Bitmap truthHi = GoldenImageHarness.RenderAtScale(truthHiResource, Frame, 8f);
            using Bitmap truth = GoldenImageHarness.MitchellResampleTo(truthHi, Frame);

            using Drawable.Resource oneToOneResource = MakeMosaicShape();
            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(oneToOneResource, Frame, 1f);
            using Drawable.Resource superHiResource = MakeMosaicShape();
            using Bitmap superHi = GoldenImageHarness.RenderAtScale(superHiResource, Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(superHi, Frame);

            // 1) LOGICAL appearance preserved: supersampled-then-downscaled mosaic keeps the SAME tile grid
            //    as 1:1 because tileSize scaled by the working density. Unscaled tileSize would give the 2x
            //    render 2x-finer tiles -> a structurally different image -> low SSIM.
            double ssimVs11 = ImageMetrics.Ssim(oneToOne, delivered);
            Assert.That(ssimVs11, Is.GreaterThan(0.95),
                "supersampled mosaic diverged from 1:1 — tileSize did not scale with the working density");

            // 2) REAL density gain: the supersampled mosaic's tile edges are at least as close to ground
            //    truth as 1:1 — buffer activation actually raised the internal density.
            double maeSS = ImageMetrics.MeanAbsoluteError(delivered, truth);
            double mae11 = ImageMetrics.MeanAbsoluteError(oneToOne, truth);
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
            using Drawable.Resource fullResource = MakeMosaicRoiShape();
            using Bitmap full = GoldenImageHarness.RenderAtScale(
                fullResource,
                Frame,
                1,
                requestedRegion: null);
            using Drawable.Resource croppedResource = MakeMosaicRoiShape();
            using Bitmap cropped = GoldenImageHarness.RenderAtScale(
                croppedResource,
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

    // A spatially-varying displacement map (default RadialGradientBrush) plus a non-zero transform — the
    // case a constant-map control cannot catch: the map is laid out in LOGICAL space but cross-sampled at
    // the base's device-px coord, so without the per-effect local-matrix x w the warp is misaligned/zoomed
    // at w != 1 (a structurally different image, not a denser one). Scale/Rotation transforms must also
    // scale uPivot by w.
    private static Drawable.Resource MakeDisplacedShape(DisplacementMapTransform transform)
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 150;
        shape.Height.Value = 40;
        shape.Fill.Value = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.Value = 21f;
        shape.Transform.Value = rotation;

        var effect = new DisplacementMapEffect();          // default DisplacementMap = RadialGradientBrush (spatially varying)
        effect.Transform.Value = transform;
        shape.FilterEffect.Value = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static DisplacementMapScaleTransform MakeScaleTransform()
    {
        var t = new DisplacementMapScaleTransform();
        t.ScaleX.Value = 160f;
        t.ScaleY.Value = 70f;
        t.CenterX.Value = 35f;   // device-space pivot -> × w
        t.CenterY.Value = 20f;
        return t;
    }

    private static DisplacementMapRotationTransform MakeRotationTransform()
    {
        var t = new DisplacementMapRotationTransform();
        t.Rotation.Value = 35f;
        t.CenterX.Value = 35f;   // device-space pivot -> × w
        t.CenterY.Value = 20f;
        return t;
    }

    [TestCaseSource(nameof(DisplacementTransforms))]
    public void DisplacementMap_Supersampled_KeepsLogicalWarp(Func<DisplacementMapTransform> transformFactory)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource oneToOneResource = MakeDisplacedShape(transformFactory());
            using Bitmap oneToOne = GoldenImageHarness.RenderAtScale(oneToOneResource, Frame, 1f);
            using Drawable.Resource superHiResource = MakeDisplacedShape(transformFactory());
            using Bitmap superHi = GoldenImageHarness.RenderAtScale(superHiResource, Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(superHi, Frame);

            // The supersampled-then-downscaled warp must be the SAME logical image as 1:1, since the
            // displacement map shares the base texture's coord space. The map-vs-base sampling-space bug
            // drops this well below 0.95 (empirically ~0.84) for a spatially-varying map.
            double ssim = ImageMetrics.Ssim(oneToOne, delivered);
            Assert.That(ssim, Is.GreaterThan(0.95),
                "supersampled displacement warp diverged from 1:1 — the map or its pivot is sampled in the wrong space at w != 1");
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
            using Drawable.Resource fullResource = MakeDisplacedShape(transformFactory());
            using Bitmap full = GoldenImageHarness.RenderAtScale(
                fullResource,
                Frame,
                1,
                requestedRegion: null);
            using Drawable.Resource croppedResource = MakeDisplacedShape(transformFactory());
            using Bitmap cropped = GoldenImageHarness.RenderAtScale(
                croppedResource,
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
                X = { Value = 40 },
                Y = { Value = 40 },
            })).SetArgDisplayNames("Translate");
        yield return new TestCaseData((Func<DisplacementMapTransform>)MakeScaleTransform)
            .SetArgDisplayNames("Scale");
        yield return new TestCaseData((Func<DisplacementMapTransform>)MakeRotationTransform)
            .SetArgDisplayNames("Rotation");
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
