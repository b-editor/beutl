using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public class DelayAnimationVisibilityTests
{
    [TestCase(0, 0, 0)]
    [TestCase(0.5, 0, 240)]
    [TestCase(1.5, 240, 960)]
    public void ZeroScaleRemovesTheDelayedTargetWithoutSkippingTheNextTarget(
        double seconds, int leftArea, int rightArea)
    {
        var scale = new ScaleTransform();
        var keyframes = new KeyFrameAnimation<float>();
        keyframes.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 0 } });
        keyframes.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 100 } });
        scale.Scale.SetKeyFrameAnimation(keyframes);
        var delay = new DelayAnimationEffect
        {
            Delay = { Value = 1000 },
            Order = { Value = DelayAnimationOrder.Reverse },
            Effect = { Value = new TransformEffect { Transform = { Value = scale } } },
        };
        using var targets = new EffectTargets
        {
            FilterEffectTestHarness.CreateSolidTarget(new Rect(0, 0, 40, 24)),
            FilterEffectTestHarness.CreateSolidTarget(new Rect(40, 0, 40, 24)),
        };
        using var resource = delay.ToResource(new CompositionContext(MediaTime.FromSeconds(seconds)));
        FilterEffectTestHarness.ApplyToTargets(resource, targets);

        Assert.Multiple(() =>
        {
            Assert.That(targets.Count, Is.EqualTo((leftArea > 0 ? 1 : 0) + (rightArea > 0 ? 1 : 0)));
            Assert.That(VisibleArea(targets, left: true), Is.EqualTo(leftArea));
            Assert.That(VisibleArea(targets, left: false), Is.EqualTo(rightArea));
        });
    }

    private static int VisibleArea(EffectTargets targets, bool left)
    {
        int count = 0;
        foreach (EffectTarget target in targets)
        {
            if ((target.Bounds.Center.X < 40) != left)
                continue;

            using Bitmap bitmap = target.RenderTarget!.Snapshot();
            using Bitmap pixels = bitmap.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
            foreach (Bgra8888 pixel in pixels.GetPixelSpan<Bgra8888>())
            {
                if (pixel.A != 0)
                    count++;
            }
        }

        return count;
    }
}
