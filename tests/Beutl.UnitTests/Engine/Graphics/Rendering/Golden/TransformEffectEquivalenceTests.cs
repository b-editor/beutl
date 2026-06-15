using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards the w==1 behaviour of the TransformEffect fix (commit 533334740), which swapped the w==1 blit from
// canvas.DrawRenderTarget(rt, default) to EffectTarget.Draw to match the same transform applied as the drawable's
// own Transform. With no golden-PNG baseline in the repo, the strongest guard is this cross-equivalence at scale 1
// against the drawable's own Transform, a known-correct code path. A regression in the w==1 blit/origin diverges here.
[NonParallelizable]
[TestFixture]
public class TransformEffectEquivalenceTests
{
    private static readonly PixelSize Frame = new(240, 240);

    private static TransformGroup MakeGroup()
    {
        var g = new TransformGroup();
        var rot = new RotationTransform();
        rot.Rotation.CurrentValue = 45f;
        var scale = new ScaleTransform();
        scale.ScaleX.CurrentValue = 120f;
        scale.ScaleY.CurrentValue = 100f;
        g.Children.Add(rot);
        g.Children.Add(scale);
        return g;
    }

    private static RectShape BaseShape()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 80;
        shape.Fill.CurrentValue = Brushes.White;
        var pen = new Pen();
        pen.Thickness.CurrentValue = 8;
        pen.Brush.CurrentValue = Brushes.Red;
        shape.Pen.CurrentValue = pen;
        return shape;
    }

    private static Drawable.Resource MakeViaDrawableTransform()
    {
        var shape = BaseShape();
        shape.Transform.CurrentValue = MakeGroup();
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeViaEffect()
    {
        var shape = BaseShape();
        var fx = new TransformEffect();
        fx.Transform.CurrentValue = MakeGroup();
        shape.FilterEffect.CurrentValue = fx;
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
}
