using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A DelayAnimationEffect re-applies its child once per target at that target's own delayed time. These
// cases pin that the re-application reaches the whole child: the animated content of a nested
// DrawableBrush as well as the child's own animated parameters, each applied exactly once.
[NonParallelizable]
[TestFixture]
public class DelayAnimationNestedBrushTimeTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static readonly TimeSpan CompositionTime = TimeSpan.FromSeconds(1);

    // The right tile is delayed by a full second against a brush whose colour animates red -> blue over
    // that second, so it must paint the red the brush had then, not the parent's blue snapshot.
    [Test]
    public void SplitDelayedNestedBrush_FollowsThePerTileDelayedTime()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = MakeSplitDelayedBrush();
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, Frame, 1f, clearColor: Colors.Transparent);

            AssertIsCompositionTimeContent(MedianColour(rendered, 0), "the undelayed tile");
            AssertIsDelayedContent(MedianColour(rendered, 1), "the delayed tile");
        });
    }

    // Split(2, 2) under a 250ms-per-tile delay evaluates the child's animation four times. Each tile
    // must carry its own delayed amount exactly once; applying it twice squares the factor.
    [Test]
    public void SplitDelayedBrightness_AppliesEachTilesDelayedAmountOnce()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource control = MakeSplitGrid(delayedBrightness: false);
            using Bitmap withoutBrightness = GoldenImageHarness.RenderAtScale(
                control, Frame, 1f, clearColor: Colors.Transparent);
            using Drawable.Resource delayed = MakeSplitGrid(delayedBrightness: true);
            using Bitmap withBrightness = GoldenImageHarness.RenderAtScale(
                delayed, Frame, 1f, clearColor: Colors.Transparent);

            double[] factors = new double[4];
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                factors[quadrant] = MedianColour(withBrightness, quadrant).R
                    / MedianColour(withoutBrightness, quadrant).R;
            }

            Array.Sort(factors);
            TestContext.WriteLine($"per-tile factors: {string.Join(", ", factors.Select(f => f.ToString("F4")))}");

            // Amount animates 40 -> 220 over 2s; at 1s the four tiles evaluate it at 1.00, 0.75, 0.50
            // and 0.25s, giving 130, 107.5, 85 and 62.5 percent.
            double[] expected = [0.625, 0.85, 1.075, 1.30];
            Assert.Multiple(() =>
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.That(factors[i], Is.EqualTo(expected[i]).Within(0.02),
                        $"tile factor {i} must be the delayed amount applied once");
                }
            });
        });
    }

    private static void AssertIsCompositionTimeContent((double R, double G, double B) colour, string label)
    {
        Assert.Multiple(() =>
        {
            Assert.That(colour.B, Is.GreaterThan(0.8),
                $"{label} did not paint the brush content at the parent's composition time");
            Assert.That(colour.R, Is.LessThan(0.05),
                $"{label} painted delayed brush content where it has no delay");
        });
    }

    private static void AssertIsDelayedContent((double R, double G, double B) colour, string label)
    {
        Assert.Multiple(() =>
        {
            Assert.That(colour.R, Is.GreaterThan(0.8),
                $"{label} did not paint the brush content at its own delayed time");
            Assert.That(colour.B, Is.LessThan(0.05),
                $"{label} painted the parent's composition-time snapshot instead of the delayed one");
        });
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

    // A 2x2 split whose child brightness animates, so every tile evaluates it at its own delayed time.
    private static Drawable.Resource MakeSplitGrid(bool delayedBrightness)
    {
        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 2;
        split.VerticalDivisions.CurrentValue = 2;
        split.HorizontalSpacing.CurrentValue = 24;
        split.VerticalSpacing.CurrentValue = 24;

        var group = new FilterEffectGroup();
        group.Children.Add(split);
        if (delayedBrightness)
        {
            var brightness = new Brightness();
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { Value = 40f, KeyTime = TimeSpan.Zero });
            animation.KeyFrames.Add(new KeyFrame<float> { Value = 220f, KeyTime = TimeSpan.FromSeconds(2) });
            brightness.Amount.Animation = animation;

            var delay = new DelayAnimationEffect();
            delay.Delay.CurrentValue = 250f;
            delay.Effect.CurrentValue = brightness;
            group.Children.Add(delay);
        }

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 140;
        // Mid grey keeps the brightest tile inside the unit range, so no factor is clipped away.
        shape.Fill.CurrentValue = new SolidColorBrush(Color.FromArgb(255, 96, 96, 96));
        shape.FilterEffect.CurrentValue = group;
        return shape.ToResource(new CompositionContext(CompositionTime));
    }

    /// <summary>
    /// The median opaque colour of one quadrant of the frame. The split's spacing keeps each tile
    /// inside its own quadrant, and the transparent background keeps the median on tile pixels.
    /// </summary>
    private static (double R, double G, double B) MedianColour(Bitmap bitmap, int quadrant)
    {
        int x0 = (quadrant & 1) == 0 ? 0 : bitmap.Width / 2;
        int y0 = quadrant < 2 ? 0 : bitmap.Height / 2;
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        var samples = new List<(double R, double G, double B)>();
        for (int y = y0; y < y0 + (bitmap.Height / 2); y++)
        {
            for (int x = x0; x < x0 + (bitmap.Width / 2); x++)
            {
                int i = ((y * bitmap.Width) + x) * 4;
                double alpha = (double)BitConverter.UInt16BitsToHalf(pixels[i + 3]);
                if (alpha < 0.999) continue;
                samples.Add((
                    (double)BitConverter.UInt16BitsToHalf(pixels[i]) / alpha,
                    (double)BitConverter.UInt16BitsToHalf(pixels[i + 1]) / alpha,
                    (double)BitConverter.UInt16BitsToHalf(pixels[i + 2]) / alpha));
            }
        }

        Assert.That(samples, Is.Not.Empty, $"quadrant {quadrant} carried no opaque tile pixels");
        samples.Sort((a, b) => a.R.CompareTo(b.R));
        return samples[samples.Count / 2];
    }

}
