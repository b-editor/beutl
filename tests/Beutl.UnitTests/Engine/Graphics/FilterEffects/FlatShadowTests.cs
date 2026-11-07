using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

// FlatShadow fills the region its content's outline sweeps in one pass. These pin that region against the content
// moved a pixel at a time, which is how the shadow used to be drawn.
[TestFixture]
[NonParallelizable]
public sealed class FlatShadowTests
{
    private const int Size = 160;

    [SetUp]
    public void SetUp() => VulkanTestEnvironment.EnsureAvailable();

    [TestCase(0f)]
    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(270f)]
    public void A_shadow_covers_what_the_content_passes_over_from_a_pixel_out_to_its_length(float angle)
    {
        byte[,] content = RenderAlpha(Square(), null);

        byte[,] shadow = RenderAlpha(Square(), ShadowOnly(angle, 40));

        float radian = angle * MathF.PI / 180;
        AssertSame(shadow, Swept(content, (int)MathF.Round(MathF.Cos(radian)), (int)MathF.Round(MathF.Sin(radian)), 40));
    }

    // The ring's hole is 32 px tall, and its top side moves 12 px down into it.
    [Test]
    public void A_hole_stays_open_where_the_content_does_not_pass_over_it()
    {
        byte[,] content = RenderAlpha(Ring(), null);

        byte[,] shadow = RenderAlpha(Ring(), ShadowOnly(90, 12));

        Assert.Multiple(() =>
        {
            Assert.That(content[Size / 2, Size / 2], Is.Zero, "the ring has a hole");
            Assert.That(shadow[70, Size / 2], Is.EqualTo(255), "the top of the hole is covered");
            Assert.That(shadow[85, Size / 2], Is.Zero, "the rest of the hole is open");
        });
        AssertSame(shadow, Swept(content, 0, 1, 12));
    }

    // Moving right or up, each arm of an L has its own run of edges facing the way, apart from the other's.
    [TestCase(0f)]
    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(270f)]
    public void A_concave_shape_casts_the_shadow_of_every_part(float angle)
    {
        byte[,] content = RenderAlpha(LShape(), null);

        byte[,] shadow = RenderAlpha(LShape(), ShadowOnly(angle, 20));

        float radian = angle * MathF.PI / 180;
        AssertSame(shadow, Swept(content, (int)MathF.Round(MathF.Cos(radian)), (int)MathF.Round(MathF.Sin(radian)), 20));
    }

    // Any nearer than a pixel, the shadow would show through the content's anti-aliased edge on the side facing away.
    [Test]
    public void A_shadow_shorter_than_a_pixel_stays_off_the_edge_facing_away()
    {
        byte[,] content = RenderAlpha(Square(), null);

        byte[,] shadow = RenderAlpha(Square(), ShadowOnly(0, 0.5f));

        int left = 0;
        while (content[Size / 2, left] == 0)
            left++;

        Assert.Multiple(() =>
        {
            Assert.That(shadow[Size / 2, left], Is.Zero, "the content's first column");
            Assert.That(shadow[Size / 2, left + 1], Is.EqualTo(255), "the column a pixel out");
        });
    }

    // A centred square moved from a pixel out to the length sweeps a band as wide as the square across the movement,
    // and stays symmetric about the middle of the way.
    [TestCase(50f)]
    [TestCase(-50f)]
    public void A_slanted_shadow_covers_the_area_the_content_sweeps(float length)
    {
        const double Radian = 30 * Math.PI / 180;
        byte[,] content = RenderAlpha(Square(), null);

        byte[,] shadow = RenderAlpha(Square(), ShadowOnly(30, length));

        double area = 32 * 32 + (Math.Abs(length) - 1) * 32 * (Math.Cos(Radian) + Math.Sin(Radian));
        double travelled = (1 + Math.Abs(length)) / 2 * Math.Sign(length);
        (double x, double y) = Centroid(shadow);
        (double contentX, double contentY) = Centroid(content);
        Assert.Multiple(() =>
        {
            Assert.That(Sum(shadow) / 255.0, Is.EqualTo(area).Within(1).Percent);
            Assert.That(x - contentX, Is.EqualTo(travelled * Math.Cos(Radian)).Within(0.5));
            Assert.That(y - contentY, Is.EqualTo(travelled * Math.Sin(Radian)).Within(0.5));
        });
    }

    [Test]
    public void A_zero_length_draws_no_shadow()
    {
        byte[,] shadow = RenderAlpha(Square(), ShadowOnly(0, 0));

        Assert.That(Sum(shadow), Is.Zero);
    }

    private static FlatShadow ShadowOnly(float angle, float length)
    {
        var effect = new FlatShadow();
        effect.Angle.Value = angle;
        effect.Length.Value = length;
        effect.Brush.Value = Brushes.White;
        effect.ShadowOnly.Value = true;
        return effect;
    }

    // A 32 px square in the middle of the frame, on whole pixels.
    private static Shape Square()
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 32;
        shape.Height.Value = 32;
        shape.Fill.Value = Brushes.White;
        return shape;
    }

    // A 32 x 40 px L in the middle of the frame, on whole pixels: a 12 px wide upright arm and a 12 px tall foot.
    private static Shape LShape()
    {
        var figure = new PathFigure();
        figure.StartPoint.Value = new Point(0, 0);
        figure.IsClosed.Value = true;
        figure.Segments.Add(new LineSegment(12, 0));
        figure.Segments.Add(new LineSegment(12, 28));
        figure.Segments.Add(new LineSegment(32, 28));
        figure.Segments.Add(new LineSegment(32, 40));
        figure.Segments.Add(new LineSegment(0, 40));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var shape = new GeometryShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Data.Value = geometry;
        shape.Fill.Value = Brushes.White;
        return shape;
    }

    // A 48 px square ring 8 px wide in the middle of the frame, on whole pixels, around a 32 px hole.
    private static Shape Ring()
    {
        var pen = new Pen();
        pen.Thickness.Value = 8;
        pen.Brush.Value = Brushes.White;
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 40;
        shape.Height.Value = 40;
        shape.Fill.Value = null;
        shape.Pen.Value = pen;
        return shape;
    }

    // The alpha of a render on nothing, by row and column.
    private static byte[,] RenderAlpha(Shape shape, FilterEffect? effect)
    {
        var alpha = new byte[Size, Size];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            shape.FilterEffect.Value = effect;
            using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, new PixelSize(Size, Size), 1f, clearColor: Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            for (int y = 0; y < Size; y++)
            {
                ReadOnlySpan<byte> row = bytes.GetRow(y);
                for (int x = 0; x < Size; x++)
                    alpha[y, x] = row[x * 4 + 3];
            }
        });
        return alpha;
    }

    // The content moved by (dx, dy) a pixel at a time, from a pixel out to length, all at once.
    private static byte[,] Swept(byte[,] content, int dx, int dy, int length)
    {
        var result = new byte[Size, Size];
        for (int step = 1; step <= length; step++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int sourceX = x - step * dx;
                    int sourceY = y - step * dy;
                    if (sourceX is >= 0 and < Size && sourceY is >= 0 and < Size)
                        result[y, x] = Math.Max(result[y, x], content[sourceY, sourceX]);
                }
            }
        }

        return result;
    }

    private static void AssertSame(byte[,] actual, byte[,] expected)
    {
        int differing = 0;
        string? first = null;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (Math.Abs(actual[y, x] - expected[y, x]) <= 2)
                    continue;

                differing++;
                first ??= $"({x}, {y}) has alpha {actual[y, x]} where {expected[y, x]} was expected";
            }
        }

        Assert.That(differing, Is.Zero, $"{differing} pixels differ; {first}");
        Assert.That(Sum(expected), Is.GreaterThan(0), "the expected shadow is not empty");
    }

    private static long Sum(byte[,] alpha)
    {
        long sum = 0;
        foreach (byte value in alpha)
            sum += value;

        return sum;
    }

    private static (double X, double Y) Centroid(byte[,] alpha)
    {
        double x = 0, y = 0, sum = 0;
        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                x += alpha[row, column] * (column + 0.5);
                y += alpha[row, column] * (row + 0.5);
                sum += alpha[row, column];
            }
        }

        return (x / sum, y / sum);
    }
}
