using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards SplitEffect combined with TransformEffect(ApplyToTarget=false) in both orders.
//
// The two orders reach different branches of FilterEffectContext.Transform. SplitEffect is a CustomEffect, so
// it leaves the context bounds symbolic: a TransformEffect placed after it resolves one shared matrix from the
// combined target bounds at execution time, while a TransformEffect placed before it resolves from the still
// concrete bounds immediately. Each order therefore gets an oracle rendered in this same process, which keeps
// the gate free of any checked-in baseline, machine-local snapshot directory, or inter-test ordering.
[NonParallelizable]
[TestFixture]
public class SplitTransformEffectCombinationTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private const float ShapeRotation = 21f;
    private const float EffectRotation = 45f;
    private const float EffectScaleX = 120f;
    private const float EffectScaleY = 100f;

    // Each oracle reaches the same geometry through a different blit, so resampling differs at tile edges.
    // TransformEffectEquivalenceTests uses the same bound against the same drawable-transform oracle.
    // Measured on Vulkan: 0.9964 for the deferred order, 0.9995 for the concrete order.
    private const double MinimumOracleSsim = 0.97;

    // Measured order divergence is 0.2633, two orders of magnitude above this floor.
    private const double MinimumOrderDivergence = 0.01;

    // A transformed 140x90 shape split into nine tiles fills well over this share of a 200x200 frame.
    private const double MinimumCoverageRatio = 0.05;

    // The transform resolves at execution time here, so its oracle is the same transform applied once to the
    // whole split result through the drawable's own Transform, a path with no deferred resolution at all.
    [Test]
    public void SplitThenTransformFilter_ExecutionTimeBoundsMatchTheDrawableTransform()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap viaEffect = Render(SplitThenTransform(), ShapeTransform());
            using Bitmap viaDrawableTransform = Render(MakeSplit(), ShapeThenEffectTransform());

            AssertOracleMatch(viaEffect, viaDrawableTransform, "SplitThenTransform");
        });
    }

    // The transform resolves from concrete bounds here, so ApplyToTarget=true — which computes the same matrix
    // from the single target it is handed — is an equivalent independent path.
    [Test]
    public void TransformFilterThenSplit_ConcreteBoundsMatchTheApplyToTargetPath()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap deferred = Render(TransformThenSplit(applyToTarget: false), ShapeTransform());
            using Bitmap eager = Render(TransformThenSplit(applyToTarget: true), ShapeTransform());

            AssertOracleMatch(deferred, eager, "TransformThenSplit");
        });
    }

    [Test]
    public void SplitTransformCombination_IsDeterministicAndOrderSensitive()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap splitFirst = Render(SplitThenTransform(), ShapeTransform());
            using Bitmap splitFirstAgain = Render(SplitThenTransform(), ShapeTransform());
            using Bitmap transformFirst = Render(TransformThenSplit(applyToTarget: false), ShapeTransform());

            double divergence = ImageMetrics.MeanAbsoluteError(splitFirst, transformFirst);
            TestContext.WriteLine($"order divergence MAE={divergence:F6}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("split-first", splitFirst), ("transform-first", transformFirst)),
                    Is.Null);
                GoldenImageHarness.AssertByteIdentical(splitFirst, splitFirstAgain);
                Assert.That(CoveredPixelRatio(splitFirst), Is.GreaterThan(MinimumCoverageRatio));
                Assert.That(
                    divergence,
                    Is.GreaterThan(MinimumOrderDivergence),
                    "the two effect orders must not collapse onto one image.");
            });
        });
    }

    private static void AssertOracleMatch(Bitmap actual, Bitmap oracle, string label)
    {
        double ssim = ImageMetrics.Ssim(actual, oracle);
        double mae = ImageMetrics.MeanAbsoluteError(actual, oracle);
        TestContext.WriteLine($"{label} vs oracle SSIM={ssim:F4} MAE={mae:F6}");

        Assert.Multiple(() =>
        {
            Assert.That(ImageMetrics.FirstNonFinite((label, actual), ($"{label}-oracle", oracle)), Is.Null);

            // Without this both sides could agree on a blank frame.
            Assert.That(
                CoveredPixelRatio(actual),
                Is.GreaterThan(MinimumCoverageRatio),
                $"{label} must produce substantial coverage.");
            Assert.That(
                ssim,
                Is.GreaterThan(MinimumOracleSsim),
                $"{label} diverged from its independently rendered oracle");
        });
    }

    private static FilterEffect SplitThenTransform()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(MakeSplit());
        group.Children.Add(MakeTransform(applyToTarget: false));
        return group;
    }

    private static FilterEffect TransformThenSplit(bool applyToTarget)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(MakeTransform(applyToTarget));
        group.Children.Add(MakeSplit());
        return group;
    }

    private static SplitEffect MakeSplit()
    {
        var effect = new SplitEffect();
        effect.HorizontalDivisions.CurrentValue = 3;
        effect.VerticalDivisions.CurrentValue = 3;
        effect.HorizontalSpacing.CurrentValue = 12;
        effect.VerticalSpacing.CurrentValue = 12;
        return effect;
    }

    private static TransformEffect MakeTransform(bool applyToTarget)
    {
        var effect = new TransformEffect();
        effect.Transform.CurrentValue = EffectTransformGroup();
        effect.TransformOrigin.CurrentValue = RelativePoint.Center;
        effect.ApplyToTarget.CurrentValue = applyToTarget;
        return effect;
    }

    private static TransformGroup EffectTransformGroup()
    {
        var group = new TransformGroup();
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = EffectRotation;
        var scale = new ScaleTransform();
        scale.ScaleX.CurrentValue = EffectScaleX;
        scale.ScaleY.CurrentValue = EffectScaleY;
        group.Children.Add(rotation);
        group.Children.Add(scale);
        return group;
    }

    private static Transform ShapeTransform()
    {
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = ShapeRotation;
        return rotation;
    }

    private static Transform ShapeThenEffectTransform()
    {
        var group = new TransformGroup();
        group.Children.Add(ShapeTransform());
        foreach (Transform child in EffectTransformGroup().Children)
            group.Children.Add(child);

        return group;
    }

    private static Bitmap Render(FilterEffect effect, Transform transform)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;
        shape.Transform.CurrentValue = transform;
        shape.FilterEffect.CurrentValue = effect;

        return GoldenImageHarness.RenderAtScale(shape.ToResource(CompositionContext.Default), Frame, 1f);
    }

    private static double CoveredPixelRatio(Bitmap bitmap)
    {
        long covered = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if ((float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]) > 0.5f)
                    covered++;
            }
        }

        return covered / ((double)bitmap.Width * bitmap.Height);
    }
}
