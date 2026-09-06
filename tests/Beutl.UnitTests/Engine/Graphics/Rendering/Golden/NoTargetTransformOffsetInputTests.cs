using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards TransformEffect(ApplyToTarget=false) over effect inputs that are not origin-anchored, with no custom
// effect in the chain. The matrix filter is the one in-tree Skia item that is not translation-invariant, so it
// is the only one that can move a target's Bounds and OriginalBounds apart relative to each other, and it does
// so only where their positions already differ. Three properties of this scene put it there, and each one is
// load-bearing:
//
//  - DrawableGroup pushes the filter-effect node outside OnDraw, so each child's own placement lands inside
//    the effect's coordinate space. A bare Drawable pushes its placement outside the node instead, which
//    leaves every effect input anchored at the origin and the case vacuous.
//  - Two children give the segment several values. A single-value input whose items are all direct-replayable
//    composes one SKImageFilter without the activator's pending-target frame, so one child never gets there.
//  - Ordinary shapes keep the recorded bounds concrete, which routes FilterEffectContext.Transform down its
//    non-deferred branch.
//
// The oracle is the same geometry driven through the group's own Transform, which builds its matrix about
// TransformOrigin over the same recorded content bounds the effect resolves its shared matrix from and
// carries no target re-anchoring at all. The children do not overlap, so compositing them separately cannot
// diverge from drawing them in one pass.
[NonParallelizable]
[TestFixture]
[Category("GpuPassFusionGpu")]
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
                    CoveredPixelRatio(viaEffect),
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
        effect.Transform.CurrentValue = MakeEffectTransformGroup();
        effect.TransformOrigin.CurrentValue = RelativePoint.Center;
        effect.ApplyToTarget.CurrentValue = false;
        return effect;
    }

    private static TransformGroup MakeEffectTransformGroup()
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

    private static Bitmap Render(FilterEffect? filterEffect, Transform? groupTransform)
    {
        var group = new DrawableGroup();
        group.Children.Add(MakeChild(s_firstChild, Colors.White));
        group.Children.Add(MakeChild(s_secondChild, Colors.Aqua));
        group.TransformOrigin.CurrentValue = RelativePoint.Center;
        if (filterEffect != null)
            group.FilterEffect.CurrentValue = filterEffect;
        if (groupTransform != null)
            group.Transform.CurrentValue = groupTransform;

        return GoldenImageHarness.RenderAtScale(
            group.ToResource(CompositionContext.Default), s_frame, 1f, clearColor: Colors.Transparent);
    }

    private static RectShape MakeChild(Rect placement, Color fill)
    {
        var translate = new TranslateTransform();
        translate.X.CurrentValue = placement.X;
        translate.Y.CurrentValue = placement.Y;

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Left;
        shape.AlignmentY.CurrentValue = AlignmentY.Top;
        shape.Width.CurrentValue = placement.Width;
        shape.Height.CurrentValue = placement.Height;
        shape.Fill.CurrentValue = new SolidColorBrush(fill);
        shape.Transform.CurrentValue = translate;
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
