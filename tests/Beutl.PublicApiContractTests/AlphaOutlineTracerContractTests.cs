using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

// An extension traces the content of a bitmap it holds into outlines to draw, as StrokeEffect does.
[TestFixture]
public sealed class AlphaOutlineTracerContractTests
{
    [Test]
    public void Trace_ReturnsOutlinesInPixelSpaceWithHolesRunningTheOtherWay()
    {
        // A 20 x 20 square of whole pixels with an 8 x 8 hole.
        using var bitmap = new Bitmap(24, 24, BitmapColorType.Alpha8, BitmapAlphaType.Premul);
        for (int y = 2; y < 22; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 2; x < 22; x++)
                row[x] = x is >= 8 and < 16 && y is >= 8 and < 16 ? (byte)0 : (byte)255;
        }

        IReadOnlyList<Point[]> outlines = AlphaOutlineTracer.Trace(bitmap);

        Point[] square = outlines.Single(o => SignedArea(o) > 0);
        Point[] hole = outlines.Single(o => SignedArea(o) < 0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outlines, Has.Count.EqualTo(2));
            // Clockwise on screen, along the pixels' own edges, with square corners.
            Assert.That(SignedArea(square), Is.EqualTo(400).Within(0.01));
            Assert.That(square.Min(p => p.X), Is.EqualTo(2).Within(0.001));
            Assert.That(square.Max(p => p.Y), Is.EqualTo(22).Within(0.001));
            Assert.That(SignedArea(hole), Is.EqualTo(-64).Within(0.01));
        }
    }

    [Test]
    public void Trace_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AlphaOutlineTracer.Trace(null!));
    }

    // Positive for a polygon that runs clockwise on screen, where y grows downwards.
    private static float SignedArea(Point[] polygon)
    {
        float sum = 0;
        Point previous = polygon[^1];
        foreach (Point point in polygon)
        {
            sum += previous.X * point.Y - point.X * previous.Y;
            previous = point;
        }

        return sum / 2;
    }
}
