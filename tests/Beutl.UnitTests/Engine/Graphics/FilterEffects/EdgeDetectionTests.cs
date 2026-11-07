using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class EdgeDetectionTests
{
    private const int Width = 64;
    private const int Height = 32;
    private static readonly Color s_dark = Color.FromRgb(64, 64, 64);
    private static readonly Color s_bright = Color.FromRgb(192, 192, 192);

    // A red and a blue of the same luminance, about 0.3 apart in Oklab.
    private static readonly Color s_red = Color.FromRgb(200, 80, 80);
    private static readonly Color s_blue = Color.FromRgb(40, 116, 227);

    // BGRA bytes in sRGB without premultiplying, on nothing, of the row through the middle.
    private static byte[] Render(Drawable shape, FilterEffect? effect, float scale = 1f, Rect? region = null)
    {
        byte[] row = [];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            shape.FilterEffect.Value = effect;
            using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, new PixelSize(Width, Height), scale, region, Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            row = bytes.GetRow(bytes.Height / 2).ToArray();
        });
        return row;
    }

    // Fills the frame, with a vertical edge in the middle.
    private static RectShape Edge(Color left, Color right)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(left, 0));
        gradient.GradientStops.Add(new GradientStop(left, 0.5f));
        gradient.GradientStops.Add(new GradientStop(right, 0.5f));
        gradient.GradientStops.Add(new GradientStop(right, 1));
        var shape = new RectShape { Width = { Value = Width }, Height = { Value = Height } };
        shape.Fill.Value = gradient;
        return shape;
    }

    // One color, and smaller than the frame on all sides.
    private static RectShape Box()
    {
        var shape = new RectShape { Width = { Value = 32 }, Height = { Value = 16 } };
        shape.Fill.Value = new SolidColorBrush(s_bright);
        return shape;
    }

    // Two colors side by side, meeting in the middle of the frame, and smaller than the frame on all sides.
    private static RectShape Box(Color left, Color right)
    {
        RectShape shape = Edge(left, right);
        shape.Width.Value = 32;
        shape.Height.Value = 16;
        return shape;
    }

    private static int Peak(byte[] row) => Enumerable.Range(0, row.Length / 4).Max(x => AlphaAt(row, x));

    private static int AlphaAt(byte[] row, int x) => row[x * 4 + 3];

    private static int[] Covered(byte[] row)
        => [.. Enumerable.Range(0, row.Length / 4).Where(x => AlphaAt(row, x) > 127)];

    [SetUp]
    public void SetUp() => VulkanTestEnvironment.EnsureAvailable();

    [Test]
    public void A_change_in_lightness_draws_a_line_and_the_rest_stays_empty()
    {
        byte[] row = Render(Edge(s_dark, s_bright), new EdgeDetection());

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(row, Width / 2 - 1), Is.EqualTo(255));
            Assert.That(AlphaAt(row, Width / 2), Is.EqualTo(255));
            Assert.That(row[Width / 2 * 4 + 1], Is.EqualTo(255), "the line is white by default");
            Assert.That(AlphaAt(row, Width / 2 - 8), Is.Zero);
            Assert.That(AlphaAt(row, Width / 2 + 8), Is.Zero);
            Assert.That(AlphaAt(row, 0), Is.Zero, "the frame of the picture is not an edge");
            Assert.That(AlphaAt(row, Width - 1), Is.Zero, "the frame of the picture is not an edge");
        });
    }

    [Test]
    public void A_picture_of_one_color_draws_nothing_not_even_around_it()
    {
        byte[] row = Render(Box(), new EdgeDetection());

        Assert.That(Covered(row), Is.Empty);
    }

    [Test]
    public void Transparency_draws_a_line_along_the_outline_to_both_sides_of_it()
    {
        byte[] before = Render(Box(), null);
        int first = Enumerable.Range(0, Width).First(x => AlphaAt(before, x) == 255);

        byte[] row = Render(Box(), new EdgeDetection { Target = { Value = EdgeDetectionTarget.Alpha } });

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(row, first - 1), Is.EqualTo(255), "outside the outline");
            Assert.That(AlphaAt(row, first), Is.EqualTo(255), "inside the outline");
            Assert.That(AlphaAt(row, first + 8), Is.Zero);
        });
    }

    [Test]
    public void The_threshold_leaves_small_changes_without_a_line()
    {
        Color dark = Color.FromRgb(120, 120, 120);
        Color bright = Color.FromRgb(136, 136, 136);

        byte[] kept = Render(Edge(dark, bright), new EdgeDetection());
        byte[] drawn = Render(Edge(dark, bright), new EdgeDetection { Threshold = { Value = 0 } });

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(kept, Width / 2), Is.Zero);
            Assert.That(AlphaAt(drawn, Width / 2), Is.GreaterThan(100));
        });
    }

    // The two colors look as light as each other, which Luminance cannot tell apart. Inside each the color is flat, and
    // around them is nothing: neither draws a line.
    [Test]
    public void Color_draws_a_line_between_two_hues_of_one_lightness_and_nowhere_else()
    {
        byte[] lightness = Render(Box(s_red, s_blue), new EdgeDetection());

        byte[] color = Render(Box(s_red, s_blue), new EdgeDetection { Target = { Value = EdgeDetectionTarget.Color } });

        Assert.Multiple(() =>
        {
            Assert.That(Enumerable.Range(0, Width).Select(x => AlphaAt(lightness, x)), Is.All.Zero);
            Assert.That(AlphaAt(color, Width / 2 - 1), Is.EqualTo(255));
            Assert.That(AlphaAt(color, Width / 2), Is.EqualTo(255));
            Assert.That(Enumerable.Range(0, Width).Where(x => x is not (Width / 2 - 1 or Width / 2)).Select(x => AlphaAt(color, x)),
                Is.All.Zero);
        });
    }

    [Test]
    public void The_threshold_decides_which_changes_in_color_draw_a_line()
    {
        EdgeDetection Detect(float threshold) => new() { Target = { Value = EdgeDetectionTarget.Color }, Threshold = { Value = threshold } };

        byte[] drawn = Render(Edge(s_red, s_blue), Detect(15));
        byte[] kept = Render(Edge(s_red, s_blue), Detect(35));

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(drawn, Width / 2), Is.EqualTo(255));
            Assert.That(AlphaAt(kept, Width / 2), Is.Zero);
        });
    }

    // A lower strength lengthens the ramp past the threshold, so that a line darkens with the size of its change.
    [Test]
    public void A_lower_strength_darkens_a_line_with_the_size_of_its_change()
    {
        Color gray = Color.FromRgb(100, 100, 100);
        Color lighter = Color.FromRgb(140, 140, 140);
        int Line(Color left, Color right, float strength)
            => AlphaAt(Render(Edge(left, right), new EdgeDetection { Strength = { Value = strength } }), Width / 2);

        int large = Line(s_dark, s_bright, 20);
        int small = Line(gray, lighter, 20);

        Assert.Multiple(() =>
        {
            Assert.That(Line(s_dark, s_bright, 100), Is.EqualTo(255), "at 100% a change past the threshold draws a solid line");
            Assert.That(large, Is.InRange(150, 250));
            Assert.That(small, Is.InRange(1, 60));
            Assert.That(small, Is.LessThan(large));
        });
    }

    // The strength fades whatever is measured: the line where two colors meet, and the one along an outline.
    [TestCase(EdgeDetectionTarget.Color, 20f)]
    [TestCase(EdgeDetectionTarget.Alpha, 5f)]
    public void The_strength_fades_the_lines_of_color_and_transparency_too(EdgeDetectionTarget target, float strength)
    {
        RectShape Shape() => target == EdgeDetectionTarget.Color ? Box(s_red, s_blue) : Box();

        byte[] full = Render(Shape(), new EdgeDetection { Target = { Value = target } });
        byte[] faint = Render(Shape(), new EdgeDetection { Target = { Value = target }, Strength = { Value = strength } });

        Assert.Multiple(() =>
        {
            Assert.That(Peak(full), Is.EqualTo(255));
            Assert.That(Peak(faint), Is.InRange(30, 225));
        });
    }

    // The strength changes how dark the line is, not how far it reaches.
    [Test]
    public void The_strength_leaves_the_thickness_alone()
    {
        byte[] full = Render(Edge(s_dark, s_bright), new EdgeDetection { Thickness = { Value = 4 } });
        byte[] faint = Render(Edge(s_dark, s_bright), new EdgeDetection { Thickness = { Value = 4 }, Strength = { Value = 20 } });
        double ratio = (double)Peak(faint) / Peak(full);

        Assert.Multiple(() =>
        {
            Assert.That(Peak(faint), Is.InRange(150, 250));
            Assert.That(Covered(full).Length, Is.EqualTo(8));
            foreach (int x in Enumerable.Range(0, Width))
                Assert.That(AlphaAt(faint, x), Is.EqualTo(AlphaAt(full, x) * ratio).Within(2), $"x = {x}");
        });
    }

    [TestCase(1f, 1f, 2)]
    [TestCase(4f, 1f, 8)]
    [TestCase(12f, 1f, 24)]
    [TestCase(4f, 2f, 16)]
    public void The_thickness_widens_the_line_without_gaps_and_follows_the_output_scale(float thickness, float scale, int width)
    {
        byte[] row = Render(Edge(s_dark, s_bright), new EdgeDetection { Thickness = { Value = thickness } }, scale);

        int[] covered = Covered(row);
        Assert.Multiple(() =>
        {
            Assert.That(covered, Has.Length.EqualTo(width).Within(2));
            Assert.That(covered[^1] - covered[0] + 1, Is.EqualTo(covered.Length), "a gap in the line");
        });
    }

    // A line this thick around a dot is a disc, as far from the dot in every direction.
    [Test]
    public void A_thick_line_stays_round()
    {
        const int Size = 320;
        const float Thickness = 120;
        var dot = new EllipseShape { Width = { Value = 16 }, Height = { Value = 16 } };
        dot.Fill.Value = new SolidColorBrush(s_bright);
        dot.FilterEffect.Value = new EdgeDetection { Target = { Value = EdgeDetectionTarget.Alpha }, Thickness = { Value = Thickness } };
        double[] reach = [];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var resource = (Drawable.Resource)dot.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(resource, new PixelSize(Size, Size), 1f, null, Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            reach = [.. Enumerable.Range(0, 64).Select(step =>
            {
                double angle = step * Math.PI / 32;
                double radius = Size / 2.0 - 1;
                while (radius > 0
                    && bytes.GetRow((int)(Size / 2.0 + radius * Math.Sin(angle)))[(int)(Size / 2.0 + radius * Math.Cos(angle)) * 4 + 3] <= 127)
                {
                    radius -= 0.25;
                }

                return radius;
            })];
        });

        // The dot's own radius, and the line's reach to each side of its outline.
        Assert.That(reach, Is.All.EqualTo(8 + Thickness).Within(2));
    }

    // A pixel of a small preview is several composition units wide, and the change stays as strong across it.
    [Test]
    public void The_lines_stay_in_a_small_preview()
    {
        var effect = new EdgeDetection { Target = { Value = EdgeDetectionTarget.Alpha }, Threshold = { Value = 50 } };

        byte[] row = Render(Box(), effect, scale: 0.2f);

        Assert.That(Covered(row), Is.Not.Empty);
    }

    [Test]
    public void No_thickness_draws_nothing()
    {
        byte[] row = Render(Edge(s_dark, s_bright), new EdgeDetection { Thickness = { Value = 0 } });

        Assert.That(Covered(row), Is.Empty);
    }

    [Test]
    public void The_line_takes_its_color_and_can_go_over_the_picture()
    {
        byte[] before = Render(Edge(s_dark, s_bright), null);

        byte[] row = Render(Edge(s_dark, s_bright), new EdgeDetection { Color = { Value = Colors.Red }, Original = { Value = EdgeDetectionOriginal.Behind } });

        int line = Width / 2 * 4;
        int picture = (Width / 2 + 8) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(row[line..(line + 4)], Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
            Assert.That(row[picture..(picture + 4)], Is.EqualTo(before[picture..(picture + 4)]));
        });
    }

    // The picture covers the part of the line that falls on it, which leaves an outline around it.
    [Test]
    public void The_picture_can_go_in_front_of_the_line()
    {
        byte[] before = Render(Box(), null);
        int first = Enumerable.Range(0, Width).First(x => AlphaAt(before, x) == 255);

        byte[] row = Render(Box(), new EdgeDetection
        {
            Target = { Value = EdgeDetectionTarget.Alpha },
            Color = { Value = Colors.Red },
            Original = { Value = EdgeDetectionOriginal.InFront },
        });

        int outside = (first - 1) * 4;
        int inside = first * 4;
        Assert.Multiple(() =>
        {
            Assert.That(row[outside..(outside + 4)], Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "outside the outline");
            Assert.That(row[inside..(inside + 4)], Is.EqualTo(before[inside..(inside + 4)]), "inside the outline");
        });
    }

    // The part cannot have the line without reading across the edge, and the line itself comes from outside the part.
    [Test]
    public void Drawing_part_of_the_frame_reads_as_far_around_it_as_the_line_reaches()
    {
        var effect = new EdgeDetection { Thickness = { Value = 28 } };
        byte[] whole = Render(Edge(s_dark, s_bright), effect);

        byte[] part = Render(Edge(s_dark, s_bright), effect, region: new Rect(Width / 2 + 24, 0, 4, Height));

        Assert.Multiple(() =>
        {
            for (int x = Width / 2 + 24; x < Width / 2 + 28; x++)
                Assert.That(AlphaAt(part, x), Is.EqualTo(AlphaAt(whole, x)).Within(1), $"x={x}");
            Assert.That(AlphaAt(whole, Width / 2 + 24), Is.GreaterThan(127));
        });
    }

    // An expression can make any of them not a number.
    [TestCase(float.NaN, 10f, 100f)]
    [TestCase(1f, float.PositiveInfinity, 100f)]
    [TestCase(1f, 10f, float.NaN)]
    public void A_value_that_is_not_finite_records_nothing(float thickness, float threshold, float strength)
    {
        var bounds = new Rect(10, 20, 100, 60);
        using var resource = new EdgeDetection.Resource { Thickness = thickness, Threshold = threshold, Strength = strength };
        using var context = new FilterEffectContext(bounds);

        context.ApplyTransactional(resource);

        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(bounds));
        });
    }

    [Test]
    public void The_bounds_grow_as_far_as_the_line_can_reach()
    {
        var bounds = new Rect(10, 20, 100, 60);
        using FilterEffect.Resource resource = new EdgeDetection { Thickness = { Value = 3 } }.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);

        context.ApplyTransactional(resource);

        Assert.That(context.Bounds, Is.EqualTo(bounds.Inflate(4)));
    }
}
