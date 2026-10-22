using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards TransformEffect(ApplyToTarget=false), the one Skia item that is not translation-invariant, over offset inputs.
//  - DrawableGroup pushes the effect node outside OnDraw, so each child's placement lands inside the effect space.
//  - Two children give the segment several values, so it runs through the executor's pending-target frame.
//  - Ordinary shapes keep the recorded bounds concrete, so FilterEffectContext.Transform takes its non-deferred branch.
// Oracle: the group's own Transform, which builds the same matrix over the same bounds; the children do not overlap.
[NonParallelizable]
[TestFixture]
public class NoTargetTransformOffsetInputTests
{
    private static readonly PixelSize s_frame = new(240, 240);

    private static readonly Rect s_firstChild = new(40, 50, 80, 60);
    private static readonly Rect s_secondChild = new(130, 110, 60, 50);

    private const float EffectRotation = 25f;
    private const float EffectScaleX = 120f;
    private const float EffectScaleY = 100f;

    // The effect resamples each rasterized child while the oracle transforms the recorded geometry, so the two
    // differ at the rotated edges. SplitTransformEffectCombinationTests holds the same bound against the same
    // kind of oracle. Measured on Vulkan: 0.9986.
    private const double MinimumOracleSsim = 0.97;

    // The two transformed children together cover about a sixth of the frame. This floor only rules out a
    // blank-versus-blank agreement; SSIM is what carries the regression signal.
    private const double MinimumCoverageRatio = 0.05;

    [Test]
    public void NoTargetTransform_OverOffsetGroupChildren_MatchesTheDrawableTransform()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap viaEffect = Render(MakeNoTargetTransform(), groupTransform: null);
            using Bitmap viaDrawableTransform = Render(filterEffect: null, MakeEffectTransformGroup());
            using Bitmap untransformed = Render(filterEffect: null, groupTransform: null);

            double ssim = ImageMetrics.Ssim(viaEffect, viaDrawableTransform);
            double mae = ImageMetrics.MeanAbsoluteError(viaEffect, viaDrawableTransform);
            Point contentOffset = AlphaBoundsPosition(untransformed);
            TestContext.WriteLine(
                $"offset-input notarget transform vs oracle SSIM={ssim:F4} MAE={mae:F6} content offset={contentOffset}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("effect", viaEffect), ("oracle", viaDrawableTransform)),
                    Is.Null);

                // Without this the effect inputs could be origin-anchored and the case would be vacuous.
                Assert.That(
                    contentOffset,
                    Is.EqualTo(s_firstChild.Position),
                    "the group's effect inputs must not be origin-anchored.");

                // Without this both sides could agree on a blank frame.
                Assert.That(
                    ImageMetrics.CoveredPixelRatio(viaEffect),
                    Is.GreaterThan(MinimumCoverageRatio),
                    "the transformed children must produce substantial coverage.");
                Assert.That(
                    ssim,
                    Is.GreaterThan(MinimumOracleSsim),
                    "the notarget transform diverged from its independently rendered oracle");
            });
        });
    }

    private static TransformEffect MakeNoTargetTransform()
    {
        var effect = new TransformEffect();
        effect.Transform.Value = MakeEffectTransformGroup();
        effect.TransformOrigin.Value = RelativePoint.Center;
        effect.ApplyToTarget.Value = false;
        return effect;
    }

    private static TransformGroup MakeEffectTransformGroup()
    {
        var group = new TransformGroup();
        var rotation = new RotationTransform();
        rotation.Rotation.Value = EffectRotation;
        var scale = new ScaleTransform();
        scale.ScaleX.Value = EffectScaleX;
        scale.ScaleY.Value = EffectScaleY;
        group.Children.Add(rotation);
        group.Children.Add(scale);
        return group;
    }

    private static Bitmap Render(FilterEffect? filterEffect, Transform? groupTransform)
    {
        var group = new DrawableGroup();
        group.Children.Add(MakeChild(s_firstChild, Colors.White));
        group.Children.Add(MakeChild(s_secondChild, Colors.Aqua));
        group.TransformOrigin.Value = RelativePoint.Center;
        if (filterEffect != null)
            group.FilterEffect.Value = filterEffect;
        if (groupTransform != null)
            group.Transform.Value = groupTransform;

        return GoldenImageHarness.RenderAtScale(
            group.ToResource(CompositionContext.Default), s_frame, 1f, clearColor: Colors.Transparent);
    }

    private static RectShape MakeChild(Rect placement, Color fill)
    {
        var translate = new TranslateTransform();
        translate.X.Value = placement.X;
        translate.Y.Value = placement.Y;

        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Left;
        shape.AlignmentY.Value = AlignmentY.Top;
        shape.Width.Value = placement.Width;
        shape.Height.Value = placement.Height;
        shape.Fill.Value = new SolidColorBrush(fill);
        shape.Transform.Value = translate;
        return shape;
    }

    private static Point AlphaBoundsPosition(Bitmap bitmap)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if ((float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]) <= 0.5f)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
            }
        }

        Assert.That(minX, Is.LessThan(bitmap.Width), "the group rendered nothing");
        return new Point(minX, minY);
    }
}
