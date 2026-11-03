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

        byte[] row = Render(Edge(s_dark, s_bright), new EdgeDetection { Color = { Value = Colors.Red }, KeepSource = { Value = true } });

        int line = Width / 2 * 4;
        int picture = (Width / 2 + 8) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(row[line..(line + 4)], Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
            Assert.That(row[picture..(picture + 4)], Is.EqualTo(before[picture..(picture + 4)]));
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
