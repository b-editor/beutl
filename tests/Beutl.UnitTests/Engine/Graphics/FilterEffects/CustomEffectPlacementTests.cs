using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

// An effect that goes through CustomEffect and moves nothing leaves a drawable that starts at a fraction of a pixel
// as it was drawn without the effect. Text usually starts at one, and the effect used to move it onto whole pixels
// and cut its last partly covered column and row, so it roughened edges and made slow motion jump (#4197).
[TestFixture]
public sealed class CustomEffectPlacementTests
{
    private const int Size = 32;

    // A curve that touches the edge of the buffer an effect draws its input into comes out a few hundredths off the
    // same curve drawn on the frame: at 2x, where the shape starts on a whole pixel, five pixels differ by up to 0.043,
    // and an identity ColorShift, which runs no custom effect, differs the same way. Moving the shape onto whole pixels
    // or cutting its partly covered column, as the effect used to, changes its edge by far more.
    private const float Tolerance = 0.06f;

    // Centred in the frame, a 15.4 x 11.7 shape starts at (8.3, 10.15). The offsets move it to 8.5, half a pixel,
    // and to 9, a whole one.
    [Test]
    public void An_effect_that_changes_nothing_leaves_a_shape_at_a_fraction_of_a_pixel_as_it_was(
        [Values(typeof(LayerEffect), typeof(Clipping))] Type effectType,
        [Values(1, 2)] int renderScale,
        [Values(0f, 0.2f, 0.7f)] float offset)
    {
        using Bitmap plain = Render(null, renderScale, offset);
        using Bitmap withEffect = Render((FilterEffect)Activator.CreateInstance(effectType)!, renderScale, offset);

        AssertSamePixels(plain, withEffect);
    }

    // A blur ahead of the effect is drawn on the device grid too, where the effect then finds it.
    [TestCase(typeof(LayerEffect))]
    [TestCase(typeof(Clipping))]
    public void An_effect_that_changes_nothing_after_a_blur_leaves_the_blur_as_it_was(Type effectType)
    {
        using Bitmap blurred = Render(Blurred(null), 1, 0f);
        using Bitmap withEffect = Render(Blurred((FilterEffect)Activator.CreateInstance(effectType)!), 1, 0f);

        AssertSamePixels(blurred, withEffect);
    }

    // The effect works on the content laid on whole pixels and leaves the content's own bounds to what follows, so a
    // transform after it scales about the same centre as it does without the effect.
    [TestCase(typeof(LayerEffect))]
    [TestCase(typeof(Clipping))]
    public void A_transform_after_an_effect_that_changes_nothing_scales_about_the_same_centre(Type effectType)
    {
        using Bitmap scaled = Render(ThenScaled(null), 1, 0f);
        using Bitmap withEffect = Render(ThenScaled((FilterEffect)Activator.CreateInstance(effectType)!), 1, 0f);

        AssertSamePixels(scaled, withEffect);
    }

    private static FilterEffectGroup ThenScaled(FilterEffect? head)
    {
        var group = new FilterEffectGroup();
        if (head != null)
            group.Children.Add(head);
        group.Children.Add(new TransformEffect { Transform = { Value = new ScaleTransform(150, 150) } });
        return group;
    }

    private static FilterEffectGroup Blurred(FilterEffect? tail)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Blur { Sigma = { Value = new Size(1.5f, 1.5f) } });
        if (tail != null)
            group.Children.Add(tail);
        return group;
    }

    private static void AssertSamePixels(Bitmap expected, Bitmap actual)
    {
        int changed = 0;
        float largest = 0f;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Rgba e = ImageMetrics.ReadPixel(expected, x, y);
                Rgba a = ImageMetrics.ReadPixel(actual, x, y);
                float difference = Math.Max(
                    Math.Max(Math.Abs(e.Red - a.Red), Math.Abs(e.Green - a.Green)),
                    Math.Max(Math.Abs(e.Blue - a.Blue), Math.Abs(e.Alpha - a.Alpha)));
                if (difference > 0f)
                    changed++;
                largest = Math.Max(largest, difference);
            }
        }

        Assert.That(largest, Is.LessThanOrEqualTo(Tolerance), $"{changed} pixels changed, by up to {largest:0.000}.");
    }

    private static Bitmap Render(FilterEffect? effect, int renderScale, float offset)
    {
        var shape = new EllipseShape();
        shape.Width.Value = 15.4f;
        shape.Height.Value = 11.7f;
        shape.Fill.Value = Brushes.White;
        shape.FilterEffect.Value = effect;
        shape.Transform.Value = new TranslateTransform(offset, 0);
        return RenderThread.Dispatcher.Invoke(() =>
        {
            int pixels = Size * renderScale;
            using var renderer = new Renderer(
                Size,
                Size,
                RenderIntent.Preview,
                renderScale: renderScale,
                maxWorkingScale: float.PositiveInfinity,
                surface: new CpuRenderTarget(pixels, pixels));
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(Size, Size), shape));
            return renderer.Snapshot();
        });
    }
}
