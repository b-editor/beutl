using System.Numerics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

// StrokeEffect and FlatShadow draw these outlines, so a pixel step here is a pixel step in the stroke.
[TestFixture]
public class AlphaOutlineTracerTests
{
    private static Bitmap Draw(int width, int height, Action<SKCanvas, SKPaint> draw, byte alpha = 255)
    {
        var skBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(skBitmap))
        using (var paint = new SKPaint { Color = new SKColor(255, 255, 255, alpha), IsAntialias = true })
        {
            canvas.Clear(SKColors.Empty);
            draw(canvas, paint);
        }

        return new Bitmap(skBitmap);
    }

    private static Bitmap Pixels(int width, int height, Action<Span<byte>, int> fill)
    {
        var skBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
        skBitmap.Erase(SKColors.Empty);
        var bitmap = new Bitmap(skBitmap);
        fill(bitmap.GetPixelSpan(), bitmap.RowBytes);
        return bitmap;
    }

    private static List<Vector2[]> Trace(Bitmap bitmap)
    {
        return AlphaOutlineTracer.Trace(bitmap)
            .Select(polygon => polygon.Select(point => new Vector2(point.X, point.Y)).ToArray())
            .ToList();
    }

    // Twice the area is positive for a contour that runs clockwise on screen.
    private static float SignedArea(Vector2[] polygon)
    {
        float sum = 0;
        Vector2 previous = polygon[^1];
        foreach (Vector2 point in polygon)
        {
            sum += previous.X * point.Y - point.X * previous.Y;
            previous = point;
        }

        return sum / 2;
    }

    private static void AssertVertices(Vector2[] actual, params Vector2[] expected)
    {
        string described = string.Join(", ", actual);
        Assert.That(actual, Has.Length.EqualTo(expected.Length), described);
        foreach (Vector2 vertex in expected)
            Assert.That(actual.Any(p => Vector2.Distance(p, vertex) < 1e-3f), $"no vertex at {vertex} in {described}");
    }

    [Test]
    public void A_whole_pixel_rectangle_traces_its_exact_sides_and_corners()
    {
        // The old tracer put the right and bottom sides on the last pixel's index, a pixel inside the shape.
        using Bitmap bitmap = Pixels(20, 16, (pixels, stride) =>
        {
            for (int y = 4; y < 12; y++)
                pixels.Slice(y * stride + 3, 10).Fill(255);
        });

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        AssertVertices(contours[0], new(3, 4), new(13, 4), new(13, 12), new(3, 12));
    }

    [Test]
    public void A_shape_filling_the_bitmap_traces_the_bitmap_edges()
    {
        using Bitmap bitmap = Pixels(12, 9, (pixels, _) => pixels.Fill(255));

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        AssertVertices(contours[0], new(0, 0), new(12, 0), new(12, 9), new(0, 9));
    }

    // An anti-aliased circle off the pixel grid: the old tracer's whole-pixel steps strayed up to a pixel from it.
    [TestCase((byte)255)]
    [TestCase((byte)77)]
    public void An_antialiased_circle_traces_within_a_fifth_of_a_pixel(byte alpha)
    {
        var center = new Vector2(32.3f, 31.7f);
        const float Radius = 20.4f;
        using Bitmap bitmap = Draw(64, 64, (canvas, paint) => canvas.DrawCircle(center.X, center.Y, Radius, paint), alpha);

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        float worst = contours[0].Max(p => MathF.Abs(Vector2.Distance(p, center) - Radius));
        Assert.That(worst, Is.LessThan(0.2f), $"a vertex strayed {worst} px from the circle");
        // Dense enough that the polygon follows the curve rather than cutting across it.
        Assert.That(contours[0], Has.Length.GreaterThan(24));
    }

    [Test]
    public void A_rotated_rectangle_keeps_its_corners()
    {
        Vector2[] corners = [new(14.3f, 9.6f), new(52.1f, 17.6f), new(46.5f, 44.0f), new(8.7f, 36.0f)];
        using Bitmap bitmap = Draw(64, 56, (canvas, paint) =>
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(corners[0].X, corners[0].Y);
            for (int i = 1; i < corners.Length; i++)
                builder.LineTo(corners[i].X, corners[i].Y);
            builder.Close();
            using SKPath path = builder.Detach();
            canvas.DrawPath(path, paint);
        });

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        // Half coverage cuts each corner by about half a pixel; a mitered pen would bevel that cut.
        foreach (Vector2 corner in corners)
        {
            float nearest = contours[0].Min(p => Vector2.Distance(p, corner));
            Assert.That(nearest, Is.LessThan(0.2f),
                $"corner {corner} was rounded off by {nearest} px in {string.Join(", ", contours[0])}");
        }
    }

    [Test]
    public void A_star_keeps_its_points()
    {
        // Half coverage cuts a 36 degree point back by more than two pixels, and a mitered pen draws the point far
        // beyond that, so the outline must put it back.
        var centre = new Vector2(50.3f, 52.6f);
        Vector2[] star = Enumerable.Range(0, 10)
            .Select(k => centre + (k % 2 == 0 ? 40f : 16f) * new Vector2(
                MathF.Cos((-90 + 36 * k) * MathF.PI / 180), MathF.Sin((-90 + 36 * k) * MathF.PI / 180)))
            .ToArray();
        using Bitmap bitmap = Draw(100, 100, (canvas, paint) =>
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(star[0].X, star[0].Y);
            for (int i = 1; i < star.Length; i++)
                builder.LineTo(star[i].X, star[i].Y);
            builder.Close();
            using SKPath path = builder.Detach();
            canvas.DrawPath(path, paint);
        });

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        for (int k = 0; k < star.Length; k += 2)
        {
            float nearest = contours[0].Min(p => Vector2.Distance(p, star[k]));
            Assert.That(nearest, Is.LessThan(0.3f),
                $"point {star[k]} was cut back by {nearest} px in {string.Join(", ", contours[0])}");
        }
    }

    [Test]
    public void A_rounded_corner_stays_rounded()
    {
        const float Radius = 5;
        var box = SKRect.Create(10.3f, 8.6f, 40, 30);
        using Bitmap bitmap = Draw(60, 48, (canvas, paint) => canvas.DrawRoundRect(box, Radius, Radius, paint));

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        // A sharpened corner would have a vertex where the sides meet, 0.41 of the radius from the arc.
        foreach (var corner in new Vector2[] { new(box.Left, box.Top), new(box.Right, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom) })
        {
            float nearest = contours[0].Min(p => Vector2.Distance(p, corner));
            Assert.That(nearest, Is.GreaterThan(1.5f), $"the corner at {corner} was sharpened");
        }
    }

    [Test]
    public void Holes_run_against_the_outer_contour()
    {
        using Bitmap bitmap = Pixels(24, 24, (pixels, stride) =>
        {
            for (int y = 2; y < 22; y++)
                pixels.Slice(y * stride + 2, 20).Fill(255);
            for (int y = 8; y < 16; y++)
                pixels.Slice(y * stride + 8, 8).Clear();
        });

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(2));
        float[] areas = contours.Select(SignedArea).OrderByDescending(a => a).ToArray();
        // Non-zero filling the path then leaves the hole empty, which a pen's inside or outside alignment relies on.
        Assert.That(areas[0], Is.EqualTo(400).Within(0.01));
        Assert.That(areas[1], Is.EqualTo(-64).Within(0.01));
    }

    [Test]
    public void A_translucent_part_inside_a_shape_is_not_cut_out()
    {
        using Bitmap bitmap = Pixels(24, 24, (pixels, stride) =>
        {
            for (int y = 2; y < 22; y++)
                pixels.Slice(y * stride + 2, 20).Fill(255);
            for (int y = 8; y < 16; y++)
                pixels.Slice(y * stride + 8, 8).Fill(40);
        });

        List<Vector2[]> contours = Trace(bitmap);

        Assert.That(contours, Has.Count.EqualTo(1));
        Assert.That(SignedArea(contours[0]), Is.EqualTo(400).Within(0.01));
    }

    [Test]
    public void Pixels_touching_at_a_corner_stay_one_shape()
    {
        using Bitmap bitmap = Pixels(12, 12, (pixels, stride) =>
        {
            for (int y = 2; y < 6; y++)
                pixels.Slice(y * stride + 2, 4).Fill(255);
            for (int y = 6; y < 10; y++)
                pixels.Slice(y * stride + 6, 4).Fill(255);
        });

        Assert.That(Trace(bitmap), Has.Count.EqualTo(1));
    }

    [Test]
    public void Faint_or_single_pixels_are_not_outlined()
    {
        using Bitmap bitmap = Pixels(16, 16, (pixels, stride) =>
        {
            for (int y = 0; y < 16; y += 2)
            {
                for (int x = 0; x < 16; x += 2)
                    pixels[y * stride + x] = AlphaOutlineTracer.BackgroundAlpha - 1;
            }

            pixels[9 * stride + 9] = 255;
        });

        Assert.That(Trace(bitmap), Is.Empty);
    }
}
