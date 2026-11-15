using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

// An effect that goes through CustomEffect and moves nothing leaves a drawable that starts at a fraction of a pixel
// as it was drawn without the effect. Text usually starts at one, and the effect used to move it onto whole pixels
// and cut its last partly covered column and row, so it roughened edges and made slow motion jump (#4197, #4321).
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
    // and to 9, a whole one. With their defaults the effects change nothing on a white shape: BlendEffect blends white
    // in, InnerShadow is white, FlatShadow has no length, and StrokeEffect's pen has no brush, though its output grows
    // by the pen's reach.
    [Test]
    public void An_effect_that_changes_nothing_leaves_a_shape_at_a_fraction_of_a_pixel_as_it_was(
        [Values(
            typeof(LayerEffect), typeof(Clipping), typeof(BlendEffect), typeof(InnerShadow), typeof(FlatShadow),
            typeof(StrokeEffect))] Type effectType,
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
    [TestCase(typeof(BlendEffect))]
    [TestCase(typeof(InnerShadow))]
    [TestCase(typeof(FlatShadow))]
    [TestCase(typeof(StrokeEffect))]
    public void An_effect_that_changes_nothing_after_a_blur_leaves_the_blur_as_it_was(Type effectType)
    {
        using Bitmap blurred = Render(Blurred(null), 1, 0f);
        using Bitmap withEffect = Render(Blurred((FilterEffect)Activator.CreateInstance(effectType)!), 1, 0f);

        AssertSamePixels(blurred, withEffect);
    }

    // The effect works on the content laid on whole pixels and leaves the content's own bounds to what follows, so a
    // transform after it scales about the same centre as it does without the effect. StrokeEffect is left out: its
    // bounds grow by the pen's reach even where the pen draws nothing, so what follows it finds other bounds.
    [Test]
    public void A_transform_after_an_effect_that_changes_nothing_scales_about_the_same_centre(
        [Values(
            typeof(LayerEffect), typeof(Clipping), typeof(BlendEffect), typeof(InnerShadow), typeof(FlatShadow))]
        Type effectType,
        [Values(1, 2)] int renderScale,
        [Values(0f, 0.2f, 0.7f)] float offset)
    {
        using Bitmap scaled = Render(ThenScaled(null), renderScale, offset);
        using Bitmap withEffect = Render(
            ThenScaled((FilterEffect)Activator.CreateInstance(effectType)!), renderScale, offset);

        AssertSamePixels(scaled, withEffect);
    }

    // BlendEffect lays its brush over the content rather than over the whole pixels the content touches, so a gradient
    // blended into a white shape lies where the same gradient lies when it fills the shape.
    [Test]
    public void A_gradient_blended_into_a_shape_lies_where_it_lies_filling_the_shape(
        [Values(1, 2)] int renderScale,
        [Values(0f, 0.2f, 0.7f)] float offset)
    {
        using Bitmap filled = Render(null, renderScale, offset, Gradient());
        using Bitmap blended = Render(new BlendEffect { Brush = { Value = Gradient() } }, renderScale, offset);

        AssertSamePixels(filled, blended);
    }

    // FlatShadow grows its output by whole pixels on the side the shadow falls, however far the shadow reaches, so a
    // shadow of no colour leaves the shape as it was.
    [Test]
    public void A_shadow_of_no_colour_leaves_a_shape_as_it_was(
        [Values(180f, 225f)] float angle,
        [Values(1, 2)] int renderScale,
        [Values(0f, 0.2f, 0.7f)] float offset)
    {
        using Bitmap plain = Render(null, renderScale, offset);
        using Bitmap shadowed = Render(
            new FlatShadow
            {
                Angle = { Value = angle },
                Length = { Value = 5.5f },
                Brush = { Value = new SolidColorBrush(Colors.Transparent) },
            },
            renderScale,
            offset);

        AssertSamePixels(plain, shadowed);
    }

    // An effect that grows its output lays it out in whole pixels around the content laid on them, and goes on with the
    // bounds it gives the content: a shadow 5.5 px to the left, and the default pen's reach of 1.5 px all round.
    [TestCase(typeof(FlatShadow))]
    [TestCase(typeof(StrokeEffect))]
    public void An_effect_that_grows_its_output_goes_on_with_the_bounds_it_gives_the_content(Type effectType)
    {
        var content = new Rect(8.3f, 10.15f, 15.4f, 11.7f);
        PixelRect device = PixelRect.FromRect(content, 1);
        using RenderTarget backing = CpuRenderTargets.Create(device.Width, device.Height);
        using var targets = new EffectTargets { new EffectTarget(backing, content, EffectiveScale.At(1), device) };
        FilterEffect effect = effectType == typeof(FlatShadow)
            ? new FlatShadow { Angle = { Value = 180 }, Length = { Value = 5.5f } }
            : new StrokeEffect();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(content);
        context.ApplyTransactional(resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        executor.Apply(context);

        Assert.That(targets.Single().Bounds, Is.EqualTo(context.Bounds));
    }

    // Merged as LayerEffect merges them, two targets laid on whole pixels a tenth of a pixel off the grid make a union
    // whose right edge, its left edge plus its width, rounds to just inside the right edge of the target on the right.
    // The union still holds that target, and goes back to the content of both.
    [Test]
    public void A_merge_whose_edge_rounds_inside_a_target_goes_back_to_the_content_of_every_target()
    {
        var left = new Rect(-20.1f, 0, 6, 5);
        var right = new Rect(-0.1f, 0, 6, 5);
        Rect merged = left.Union(right);
        Assert.That(merged.Right, Is.LessThan(right.Right), "The union's right edge rounds inside.");
        var leftContent = new Rect(-19.8f, 0.2f, 5.1f, 4.6f);
        var rightContent = new Rect(0.15f, 0.3f, 5.55f, 4.4f);
        var placement = new FilterEffectExecutor.AlignedPlacement([new(leftContent, left), new(rightContent, right)]);

        Assert.That(placement.Restore(merged), Is.EqualTo(leftContent.Union(rightContent)));
    }

    // Grown by two pixels on the left, as a Clipping with a left of -2 grows it, a target's right edge, its new left edge
    // plus its new width, rounds to just inside the right edge it had. The edge it left in place goes back to the
    // content's, and the one it moved keeps the move.
    [Test]
    public void An_edge_that_rounds_inside_the_one_it_lies_on_goes_back_to_the_content()
    {
        var aligned = new Rect(-3.05f, 0, 4, 5);
        var grown = new Rect(aligned.X - 2, aligned.Y, aligned.Width + 2, aligned.Height);
        Assert.That(grown.Right, Is.LessThan(aligned.Right), "The right edge rounds inside.");
        var content = new Rect(-2.8f, 0.25f, 3.45f, 4.5f);
        var placement = new FilterEffectExecutor.AlignedPlacement([new(content, aligned)]);

        Rect restored = placement.Restore(grown);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Left, Is.EqualTo(content.Left - 2).Within(1e-5f));
            Assert.That(restored.Right, Is.EqualTo(content.Right).Within(1e-5f));
            Assert.That(restored.Top, Is.EqualTo(content.Top).Within(1e-5f));
            Assert.That(restored.Bottom, Is.EqualTo(content.Bottom).Within(1e-5f));
        });
    }

    // A group's drawables reach the effect as separate targets. Each goes on with its own bounds, so a transform after
    // the effect scales each about the same centre as without it.
    [Test]
    public void A_transform_after_a_clipping_on_a_group_scales_each_drawable_about_the_same_centre()
    {
        using Bitmap scaled = RenderGroup(ThenScaled(null));
        using Bitmap withEffect = RenderGroup(ThenScaled(new Clipping()));

        AssertSamePixels(scaled, withEffect);
    }

    // Two ellipses apart from each other, whose bounds start and end at fractions of a pixel.
    private static Bitmap RenderGroup(FilterEffect effect)
    {
        var group = new DrawableGroup();
        foreach (float x in new[] { -7.3f, 7.7f })
        {
            var shape = new EllipseShape();
            shape.Width.Value = 9.4f;
            shape.Height.Value = 7.7f;
            shape.Fill.Value = Brushes.White;
            shape.Transform.Value = new TranslateTransform(x, 0.15f);
            group.Children.Add(shape);
        }

        group.FilterEffect.Value = effect;
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = RendererTestSupport.CreateCpuRenderer(48, 48);
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(48, 48), group));
            return renderer.Snapshot();
        });
    }

    // Red to blue across the middle tenth of the shape, steep enough that moving it a fraction of a pixel against the
    // shape shows.
    private static LinearGradientBrush Gradient()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0.45f));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 0.55f));
        return gradient;
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

    private static Bitmap Render(FilterEffect? effect, int renderScale, float offset, Brush? fill = null)
    {
        var shape = new EllipseShape();
        shape.Width.Value = 15.4f;
        shape.Height.Value = 11.7f;
        shape.Fill.Value = fill ?? Brushes.White;
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
