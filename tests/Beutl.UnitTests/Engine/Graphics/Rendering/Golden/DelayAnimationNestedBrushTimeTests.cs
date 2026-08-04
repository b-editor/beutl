using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// LowerNestedEffectBrushes records the child's DrawableBrush content once, at the parent's composition time,
// because the recorder only exists then. Every delayed target therefore paints that one snapshot: the nested
// drawable content does not follow the per-target delayed time the child's other properties do.
[NonParallelizable]
[TestFixture]
public class DelayAnimationNestedBrushTimeTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static readonly TimeSpan CompositionTime = TimeSpan.FromSeconds(1);

    [Test]
    public void DelayedNestedDrawableBrush_PaintsTheCompositionTimeContentOnEveryTarget()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(MakeSplitDelayedBrush(), Frame, 1f);

            SKColor left = rendered.SKBitmap.GetPixel(50, 100);
            SKColor gap = rendered.SKBitmap.GetPixel(100, 100);
            SKColor right = rendered.SKBitmap.GetPixel(150, 100);
            TestContext.WriteLine($"left={left}, gap={gap}, right={right}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    gap.Blue,
                    Is.LessThan(60),
                    "the split spacing must stay empty, or the two samples are not two separate targets");
                AssertIsCompositionTimeContent(left, "the undelayed target");
                AssertIsCompositionTimeContent(right, "the one-second-delayed target");
            });
        });
    }

    private static void AssertIsCompositionTimeContent(SKColor color, string label)
    {
        Assert.That(
            color.Blue,
            Is.GreaterThan(200),
            $"{label} did not paint the brush content recorded at the parent's composition time");
        Assert.That(
            color.Red,
            Is.LessThan(60),
            $"{label} painted delayed brush content, which the recorder cannot produce");
    }

    // Two tiles from one split, then a one-second-per-tile delay: the second tile re-applies the child at t=0.
    private static Drawable.Resource MakeSplitDelayedBrush()
    {
        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 2;
        split.VerticalDivisions.CurrentValue = 1;
        split.HorizontalSpacing.CurrentValue = 20;

        var blend = new BlendEffect();
        blend.Brush.CurrentValue = MakeTimeColouredDrawableBrush();
        blend.BlendMode.CurrentValue = BlendMode.SrcIn;

        var delay = new DelayAnimationEffect();
        delay.Delay.CurrentValue = 1000f;
        delay.Effect.CurrentValue = blend;

        var group = new FilterEffectGroup();
        group.Children.Add(split);
        group.Children.Add(delay);

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 160;
        shape.Height.CurrentValue = 120;
        shape.Fill.CurrentValue = Brushes.White;
        shape.FilterEffect.CurrentValue = group;
        return shape.ToResource(new CompositionContext(CompositionTime));
    }

    // Red at t=0, blue at the composition time: a delayed re-application would tint its tile red.
    private static Brush MakeTimeColouredDrawableBrush()
    {
        var fill = new SolidColorBrush();
        var animation = new KeyFrameAnimation<Color>();
        animation.KeyFrames.Add(new KeyFrame<Color> { Value = Colors.Red, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<Color> { Value = Colors.Blue, KeyTime = CompositionTime });
        fill.Color.Animation = animation;

        var content = new RectShape();
        content.AlignmentX.CurrentValue = AlignmentX.Center;
        content.AlignmentY.CurrentValue = AlignmentY.Center;
        content.Width.CurrentValue = 200;
        content.Height.CurrentValue = 200;
        content.Fill.CurrentValue = fill;

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = content;
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.Tile;
        return brush;
    }
}
