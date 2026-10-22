using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Pins the scale-1 TransformEffect blit against the same transform applied as the drawable's own Transform.
[NonParallelizable]
[TestFixture]
public class TransformEffectEquivalenceTests
{
    private static readonly PixelSize Frame = new(240, 240);
    private static readonly PixelSize TransformBrightnessFrame = new(256, 144);

    [TestCase(TransformKind.FractionalScale)]
    [TestCase(TransformKind.Rotation5)]
    [TestCase(TransformKind.NearIdentityRotation)]
    [TestCase(TransformKind.QuarterTurnRotation)]
    public void FollowedByBrightness_AtScale1_MatchesCommutedOrder(TransformKind transformKind)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource transformedThenBrightened = MakeTransformBrightnessChain(
                transformKind,
                transformFirst: true);
            using Drawable.Resource brightenedThenTransformed = MakeTransformBrightnessChain(
                transformKind,
                transformFirst: false);
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                transformedThenBrightened,
                TransformBrightnessFrame,
                1f);
            using Bitmap commuted = GoldenImageHarness.RenderAtScale(
                brightenedThenTransformed,
                TransformBrightnessFrame,
                1f);

            double ssim = ImageMetrics.Ssim(actual, commuted);
            double mae = ImageMetrics.MeanAbsoluteError(actual, commuted);
            TestContext.WriteLine($"{transformKind}: commuted-order SSIM={ssim:F4} MAE={mae:F4}");
            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("actual", actual), ("commuted", commuted)),
                    Is.Null);
                Assert.That(ssim, Is.GreaterThan(0.99),
                    "TransformEffect followed by a current-pixel color effect must match the commuted rendering order within resampling tolerance.");
                Assert.That(mae, Is.LessThan(0.01),
                    "TransformEffect followed by a current-pixel color effect must not lose or shift the transformed raster.");
            });
        });
    }

    private static TransformGroup MakeGroup()
    {
        var g = new TransformGroup();
        var rot = new RotationTransform();
        rot.Rotation.Value = 45f;
        var scale = new ScaleTransform();
        scale.ScaleX.Value = 120f;
        scale.ScaleY.Value = 100f;
        g.Children.Add(rot);
        g.Children.Add(scale);
        return g;
    }

    private static RectShape BaseShape()
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 80;
        shape.Fill.Value = Brushes.White;
        var pen = new Pen();
        pen.Thickness.Value = 8;
        pen.Brush.Value = Brushes.Red;
        shape.Pen.Value = pen;
        return shape;
    }

    private static Drawable.Resource MakeViaDrawableTransform()
    {
        var shape = BaseShape();
        shape.Transform.Value = MakeGroup();
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeViaEffect()
    {
        var shape = BaseShape();
        var fx = new TransformEffect();
        fx.Transform.Value = MakeGroup();
        shape.FilterEffect.Value = fx;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeTransformBrightnessChain(
        TransformKind transformKind,
        bool transformFirst)
    {
        var shape = new RectShape
        {
            AlignmentX = { Value = AlignmentX.Center },
            AlignmentY = { Value = AlignmentY.Center },
            Width = { Value = 160 },
            Height = { Value = 104 },
            Fill = { Value = Brushes.OrangeRed },
        };
        var transform = new TransformEffect
        {
            ApplyToTarget = { Value = true },
            Transform =
            {
                Value = transformKind switch
                {
                    TransformKind.FractionalScale => new ScaleTransform(60, 60),
                    TransformKind.Rotation5 => new RotationTransform(5),
                    TransformKind.NearIdentityRotation => new RotationTransform(0.5f),
                    TransformKind.QuarterTurnRotation => new RotationTransform(90f),
                    _ => throw new ArgumentOutOfRangeException(nameof(transformKind)),
                },
            },
        };
        var brightness = new Brightness
        {
            Amount = { Value = 160 },
        };
        var group = new FilterEffectGroup();
        group.Children.Add(transformFirst ? transform : brightness);
        group.Children.Add(transformFirst ? brightness : transform);
        shape.FilterEffect.Value = group;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void ApplyToTarget_AtScale1_MatchesDrawableTransform()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap viaEffect = GoldenImageHarness.RenderAtScale(MakeViaEffect(), Frame, 1f);
            using Bitmap viaTransform = GoldenImageHarness.RenderAtScale(MakeViaDrawableTransform(), Frame, 1f);

            double ssim = ImageMetrics.Ssim(viaEffect, viaTransform);
            TestContext.WriteLine($"TransformEffect vs drawable.Transform @1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.97),
                "TransformEffect at w==1 diverged from the same transform as the drawable's own Transform — the w==1 blit/origin changed behaviour");
        });
    }

    public enum TransformKind
    {
        FractionalScale,
        Rotation5,
        NearIdentityRotation,
        QuarterTurnRotation,
    }
}
