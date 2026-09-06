using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelRectTests
{
    private static readonly Rect[] s_fractionalRects =
    [
        new(0.25f, 0.25f, 4, 4),
        new(-0.25f, -0.25f, 4, 4),
        new(-0.02f, -0.02f, 4, 4),
        new(-1.5f, -1.5f, 3, 3),
        new(-3.5f, -3.5f, 0.25f, 0.25f),
    ];

    [Test]
    public void Parse()
    {
        const string str = "20,80,1900,1000";
        var rect = PixelRect.Parse(str);

        Assert.That(rect, Is.EqualTo(new PixelRect(20, 80, 1900, 1000)));
    }

    [Test]
    public void Contains()
    {
        var rect = new PixelRect(20, 80, 1900, 1000);

        Assert.That(rect.Contains(new PixelPoint(1899, 999)));

        Assert.That(rect.Contains(new PixelRect(30, 90, 1870, 990)));
    }

    [Test]
    public void CenterRect()
    {
        var rect = new PixelRect(0, 0, 1920, 1080);
        var center = new PixelRect(0, 0, 1280, 720);

        center = rect.CenterRect(center);

        Assert.That(rect.Contains(center));
    }

    [Test]
    public void Intersect()
    {
        var rect = new PixelRect(0, 0, 100, 100)
            .Intersect(new PixelRect(50, 50, 100, 100));

        Assert.That(rect, Is.EqualTo(new PixelRect(50, 50, 50, 50)));
    }

    [Test]
    public void Intersects()
    {
        var rect = new PixelRect(0, 0, 100, 100);

        Assert.That(rect.Intersects(new PixelRect(50, 50, 100, 100)));

        Assert.That(!rect.Intersects(new PixelRect(100, 100, 100, 100)));
    }

    [Test]
    public void Translate()
    {
        var rect = new PixelRect(0, 0, 100, 100);
        rect = rect.Translate(new PixelPoint(25, 25));

        Assert.That(rect, Is.EqualTo(new PixelRect(25, 25, 100, 100)));
    }

    [Test]
    public void Union()
    {
        var rect = new PixelRect(0, 0, 100, 100)
            .Union(new PixelRect(50, 50, 100, 100));

        Assert.That(rect, Is.EqualTo(new PixelRect(0, 0, 150, 150)));
    }

    [Test]
    public void Empty_IsAllZero()
    {
        Assert.That(PixelRect.Empty, Is.EqualTo(new PixelRect(0, 0, 0, 0)));
        Assert.That(PixelRect.Empty.IsEmpty, Is.True);
    }

    [Test]
    public void Constructor_FromSize_PositionAtOrigin()
    {
        var r = new PixelRect(new PixelSize(100, 200));
        Assert.That(r.Position, Is.EqualTo(PixelPoint.Origin));
        Assert.That(r.Size, Is.EqualTo(new PixelSize(100, 200)));
    }

    [Test]
    public void Constructor_FromPositionAndSize_AssignsAll()
    {
        var r = new PixelRect(new PixelPoint(5, 6), new PixelSize(10, 20));
        Assert.That(r.Right, Is.EqualTo(15));
        Assert.That(r.Bottom, Is.EqualTo(26));
    }

    [Test]
    public void Constructor_FromTwoCorners_CalculatesSize()
    {
        var r = new PixelRect(new PixelPoint(1, 2), new PixelPoint(11, 22));
        Assert.That(r.Width, Is.EqualTo(10));
        Assert.That(r.Height, Is.EqualTo(20));
    }

    [Test]
    public void Corners_ExposeExpectedPoints()
    {
        var r = new PixelRect(0, 0, 10, 20);
        Assert.That(r.TopLeft, Is.EqualTo(new PixelPoint(0, 0)));
        Assert.That(r.TopRight, Is.EqualTo(new PixelPoint(10, 0)));
        Assert.That(r.BottomLeft, Is.EqualTo(new PixelPoint(0, 20)));
        Assert.That(r.BottomRight, Is.EqualTo(new PixelPoint(10, 20)));
        Assert.That(r.Center, Is.EqualTo(new PixelPoint(5, 10)));
    }

    [Test]
    public void Equality_AndHashCode()
    {
        var a = new PixelRect(0, 0, 10, 20);
        var b = new PixelRect(0, 0, 10, 20);
        var c = new PixelRect(5, 0, 10, 20);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Intersect_Disjoint_ReturnsEmpty()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(100, 100, 10, 10);
        Assert.That(a.Intersect(b), Is.EqualTo(PixelRect.Empty));
    }

    [Test]
    public void Union_HandlesEmptyOperands()
    {
        var a = new PixelRect(0, 0, 10, 10);
        Assert.That(PixelRect.Empty.Union(a), Is.EqualTo(a));
        Assert.That(a.Union(PixelRect.Empty), Is.EqualTo(a));
    }

    [Test]
    public void With_Methods_ReplaceFields()
    {
        var r = new PixelRect(1, 2, 3, 4);
        Assert.That(r.WithX(9), Is.EqualTo(new PixelRect(9, 2, 3, 4)));
        Assert.That(r.WithY(9), Is.EqualTo(new PixelRect(1, 9, 3, 4)));
        Assert.That(r.WithWidth(9), Is.EqualTo(new PixelRect(1, 2, 9, 4)));
        Assert.That(r.WithHeight(9), Is.EqualTo(new PixelRect(1, 2, 3, 9)));
    }

    [Test]
    public void ToRect_AndFromRect_RoundTripScale1()
    {
        var pr = new PixelRect(0, 0, 100, 50);
        Rect r = pr.ToRect(1f);
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 100, 50)));
        Assert.That(PixelRect.FromRect(r), Is.EqualTo(pr));
    }

    [Test]
    public void ToRect_VectorScale_DividesEachAxis()
    {
        var pr = new PixelRect(0, 0, 100, 50);
        Rect r = pr.ToRect(new Vector(2f, 5f));
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 50, 10)));
    }

    [Test]
    public void FromRect_WithScale_CeilsBottomRight()
    {
        var r = new Rect(0, 0, 1.5f, 2.5f);
        Assert.That(PixelRect.FromRect(r, 1f),
            Is.EqualTo(new PixelRect(0, 0, 2, 3)));
        Assert.That(PixelRect.FromRect(r, new Vector(2, 4)),
            Is.EqualTo(new PixelRect(0, 0, 3, 10)));
    }

    [Test]
    public void FromRect_CoversEveryLogicalCorner([ValueSource(nameof(s_fractionalRects))] Rect rect)
    {
        Assert.Multiple(() =>
        {
            AssertCovers(PixelRect.FromRect(rect), rect, new Vector(1, 1));
            AssertCovers(PixelRect.FromRect(rect, 2f), rect, new Vector(2, 2));
            AssertCovers(PixelRect.FromRect(rect, new Vector(2, 4)), rect, new Vector(2, 4));
        });
    }

    [Test]
    public void FromRect_FloorsTheTopLeftAtNegativeOrigins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PixelRect.FromRect(new Rect(-0.25f, -0.25f, 4, 4)),
                Is.EqualTo(new PixelRect(-1, -1, 5, 5)));
            Assert.That(PixelRect.FromRect(new Rect(-1.5f, -1.5f, 3, 3)),
                Is.EqualTo(new PixelRect(-2, -2, 4, 4)));
            Assert.That(PixelRect.FromRect(new Rect(-0.25f, -0.25f, 4, 4), 2f),
                Is.EqualTo(new PixelRect(-1, -1, 9, 9)));
        });
    }

    [TestCase(100_000f)]
    [TestCase(1_000_000f)]
    [TestCase(10_000_000f)]
    public void FromRect_CoverOfLargeCoordinateSuperset_ContainsSubsetCover(float coordinate)
    {
        var subset = new Rect(coordinate, coordinate, 0.001f, 0.001f);
        var superset = new Rect(coordinate - 5f, coordinate - 5f, 5.001f, 5.001f);

        PixelRect subsetCover = PixelRect.FromRect(subset, 1f);
        PixelRect supersetCover = PixelRect.FromRect(superset, 1f);

        Assert.That(
            supersetCover.Contains(subsetCover),
            Is.True,
            $"The cover of {superset} ({supersetCover}) did not contain the cover of {subset} ({subsetCover}).");
    }

    [Test]
    public void FromRect_CoverMapping_IsMonotoneForRandomContainedRects()
    {
        var random = new Random(0x50495845);
        int verified = 0;

        for (int attempt = 0; attempt < 20_000 && verified < 5_000; attempt++)
        {
            float outerX = RandomCoordinate(random);
            float outerY = RandomCoordinate(random);
            float outerWidth = RandomExtent(random);
            float outerHeight = RandomExtent(random);
            var outer = new Rect(outerX, outerY, outerWidth, outerHeight);

            float innerX = (float)((double)outerX + outerWidth * random.NextDouble());
            float innerY = (float)((double)outerY + outerHeight * random.NextDouble());
            double remainingWidth = (double)outerX + outerWidth - innerX;
            double remainingHeight = (double)outerY + outerHeight - innerY;
            if (remainingWidth <= 0 || remainingHeight <= 0)
                continue;

            float innerWidth = (float)(remainingWidth * random.NextDouble());
            float innerHeight = (float)(remainingHeight * random.NextDouble());
            if (innerWidth <= 0 || innerHeight <= 0)
                continue;

            var inner = new Rect(innerX, innerY, innerWidth, innerHeight);
            if (!ContainsInDouble(outer, inner))
                continue;

            float scale = 0.1f + (float)(random.NextDouble() * 3.9);
            PixelRect outerCover = PixelRect.FromRect(outer, scale);
            PixelRect innerCover = PixelRect.FromRect(inner, scale);

            Assert.That(
                outerCover.Contains(innerCover),
                Is.True,
                $"Scale {scale}: cover of {outer} ({outerCover}) did not contain cover of {inner} ({innerCover}).");
            verified++;
        }

        Assert.That(verified, Is.EqualTo(5_000), "The seeded sweep did not generate enough valid contained rectangles.");
    }

    private static float RandomCoordinate(Random random)
    {
        double magnitude = Math.Pow(10, 2 + random.NextDouble() * 5);
        return (float)((random.Next(2) == 0 ? -1 : 1) * magnitude);
    }

    private static float RandomExtent(Random random)
    {
        return (float)Math.Pow(10, -3 + random.NextDouble() * 5);
    }

    private static bool ContainsInDouble(Rect outer, Rect inner)
    {
        return (double)inner.X >= outer.X
               && (double)inner.Y >= outer.Y
               && (double)inner.X + inner.Width <= (double)outer.X + outer.Width
               && (double)inner.Y + inner.Height <= (double)outer.Y + outer.Height;
    }

    private static void AssertCovers(PixelRect actual, Rect logical, Vector scale)
    {
        var device = new Rect(
            logical.X * scale.X,
            logical.Y * scale.Y,
            logical.Width * scale.X,
            logical.Height * scale.Y);

        Assert.That(actual.X, Is.LessThanOrEqualTo(device.X), $"{actual} misses the left of {device}");
        Assert.That(actual.Y, Is.LessThanOrEqualTo(device.Y), $"{actual} misses the top of {device}");
        Assert.That(actual.Right, Is.GreaterThanOrEqualTo(device.Right), $"{actual} misses the right of {device}");
        Assert.That(actual.Bottom, Is.GreaterThanOrEqualTo(device.Bottom), $"{actual} misses the bottom of {device}");
        Assert.That(actual.Width, Is.GreaterThan(0), $"{actual} has no width for {device}");
        Assert.That(actual.Height, Is.GreaterThan(0), $"{actual} has no height for {device}");
    }

    [Test]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.That(PixelRect.TryParse("garbage", out PixelRect r), Is.False);
        Assert.That(r, Is.EqualTo(default(PixelRect)));
    }

    [Test]
    public void ToString_UsesInvariantCulture()
    {
        Assert.That(new PixelRect(1, 2, 3, 4).ToString(), Is.EqualTo("1, 2, 3, 4"));
    }
}
