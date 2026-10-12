using Beutl.Collections.Pooled;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class ContourTracerTests
{
    private static Bitmap CreateBitmap(int width, int height, Action<Bitmap> draw)
    {
        var bitmap = new Bitmap(width, height);
        draw(bitmap);
        return bitmap;
    }

    private static void FillRect(Bitmap bitmap, int x, int y, int w, int h, byte alpha = 255)
    {
        var pixel = new Bgra8888(255, 255, 255, alpha);
        for (int row = y; row < y + h && row < bitmap.Height; row++)
        {
            Span<Bgra8888> span = bitmap.GetRow<Bgra8888>(row);
            for (int col = x; col < x + w && col < bitmap.Width; col++)
            {
                span[col] = pixel;
            }
        }
    }

    [TestCase(10, 10, TestName = "FindContours_EmptyImage_ReturnsNoContours")]
    [TestCase(1, 1, TestName = "FindContours_1x1Image_Empty")]
    public void FindContours_EmptyImage_ReturnsNoContours(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Is.Empty);
    }

    [Test]
    public void FindContours_FullyFilledImage_ReturnsOneContour()
    {
        using var bitmap = CreateBitmap(10, 10, b => b.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(255, 255, 255, 255)));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
        Assert.That(contours[0].Length, Is.GreaterThanOrEqualTo(4));
    }

    [TestCase(5, 5, 2, 2, TestName = "FindContours_SinglePixel_ReturnsSinglePointContour")]
    [TestCase(5, 5, 0, 0, TestName = "FindContours_CornerPixel_ReturnsContour")]
    [TestCase(1, 1, 0, 0, TestName = "FindContours_1x1Image_SinglePixelFilled")]
    public void FindContours_SinglePixel_ReturnsSinglePointContour(int width, int height, int x, int y)
    {
        using var bitmap = CreateBitmap(width, height, b =>
        {
            b.GetRow<Bgra8888>(y)[x] = new Bgra8888(255, 255, 255, 255);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
        Assert.That(contours[0].Length, Is.EqualTo(1));
        Assert.That(contours[0][0], Is.EqualTo(new PixelPoint(x, y)));
    }

    [Test]
    public void FindContours_SmallRectangle_ContoursContainCornerPoints()
    {
        using var bitmap = CreateBitmap(5, 5, b => FillRect(b, 1, 1, 3, 3));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));

        var allPoints = new HashSet<PixelPoint>();
        foreach (var contour in contours)
        {
            foreach (var pt in contour)
                allPoints.Add(pt);
        }

        Assert.That(allPoints, Does.Contain(new PixelPoint(1, 1)));
        Assert.That(allPoints, Does.Contain(new PixelPoint(3, 1)));
        Assert.That(allPoints, Does.Contain(new PixelPoint(1, 3)));
        Assert.That(allPoints, Does.Contain(new PixelPoint(3, 3)));
    }

    [Test]
    public void FindContours_RectangleWithHole_ReturnsMultipleContours()
    {
        using var bitmap = CreateBitmap(10, 10, b =>
        {
            b.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(255, 255, 255, 255));
            FillRect(b, 3, 3, 4, 4, alpha: 0);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void FindContours_TwoSeparateRegions_ReturnsTwoOuterContours()
    {
        using var bitmap = CreateBitmap(20, 10, b =>
        {
            FillRect(b, 1, 1, 3, 3);
            FillRect(b, 15, 1, 3, 3);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void FindContours_HorizontalLine_ReturnsContour()
    {
        using var bitmap = CreateBitmap(10, 7, b => FillRect(b, 2, 3, 5, 1));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FindContours_VerticalLine_ReturnsContour()
    {
        using var bitmap = CreateBitmap(7, 10, b => FillRect(b, 3, 2, 1, 5));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FindContours_SemiTransparentPixels_TreatedAsForeground()
    {
        using var bitmap = CreateBitmap(5, 5, b => FillRect(b, 1, 1, 3, 3, alpha: 1));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FindContours_ApproxSimple_CompressesCollinearPoints()
    {
        // A 6x6 filled rectangle: outer contour with ApproxSimple should compress
        // the straight edges, resulting in roughly 4 corner points
        using var bitmap = CreateBitmap(8, 8, b => FillRect(b, 1, 1, 6, 6));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));

        // With ApproxSimple, a rectangle contour should have significantly fewer
        // points than its perimeter (roughly 4 corners instead of ~20 perimeter pixels)
        var outerContour = contours[0];
        Assert.That(outerContour.Length, Is.LessThanOrEqualTo(8),
            "ApproxSimple should compress straight edges of a rectangle");
        Assert.That(outerContour.Length, Is.GreaterThanOrEqualTo(4),
            "Rectangle contour should have at least 4 points");
    }

    [Test]
    public void FindContours_ContourPointsAreWithinImageBounds()
    {
        using var bitmap = CreateBitmap(15, 15, b => FillRect(b, 2, 2, 10, 10));

        using var contours = ContourTracer.FindContours(bitmap);

        foreach (var contour in contours.List)
        {
            foreach (var point in contour)
            {
                Assert.That(point.X, Is.InRange(0, bitmap.Width - 1),
                    $"X coordinate {point.X} out of bounds");
                Assert.That(point.Y, Is.InRange(0, bitmap.Height - 1),
                    $"Y coordinate {point.Y} out of bounds");
            }
        }
    }

    [Test]
    public void FindContours_EdgeFilledRow_ReturnsContour()
    {
        using var bitmap = CreateBitmap(5, 5, b => FillRect(b, 0, 0, 5, 1));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));

        foreach (var contour in contours)
        {
            foreach (var point in contour)
            {
                Assert.That(point.X, Is.InRange(0, 4));
                Assert.That(point.Y, Is.EqualTo(0));
            }
        }
    }

    [Test]
    public void FindContours_LShape_ReturnsContour()
    {
        using var bitmap = CreateBitmap(10, 10, b =>
        {
            FillRect(b, 1, 1, 2, 5);
            FillRect(b, 1, 5, 5, 2);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));

        var contour = contours[0];
        Assert.That(contour.Length, Is.GreaterThanOrEqualTo(6),
            "L-shape should have at least 6 corner points");
    }

    [Test]
    public void FindContours_DiagonalPixels_ReturnsContours()
    {
        // Diagonally adjacent pixels form one 8-connected contour
        using var bitmap = CreateBitmap(5, 5, b =>
        {
            b.GetRow<Bgra8888>(0)[0] = new Bgra8888(255, 255, 255, 255);
            b.GetRow<Bgra8888>(1)[1] = new Bgra8888(255, 255, 255, 255);
            b.GetRow<Bgra8888>(2)[2] = new Bgra8888(255, 255, 255, 255);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
    }

    [Test]
    public void FindContoursWithHierarchy_FilledRect_HoleHasParent()
    {
        using var bitmap = CreateBitmap(12, 12, b =>
        {
            FillRect(b, 1, 1, 10, 10);
            FillRect(b, 4, 4, 4, 4, alpha: 0);
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            Assert.That(contours.Count, Is.GreaterThanOrEqualTo(2));

            bool hasTopLevel = false;
            for (int i = 0; i < parentIndices.Count; i++)
            {
                if (parentIndices[i] == -1)
                {
                    hasTopLevel = true;
                    break;
                }
            }
            Assert.That(hasTopLevel, Is.True, "Should have at least one top-level outer contour");

            bool hasHoleWithParent = false;
            for (int i = 0; i < parentIndices.Count; i++)
            {
                if (parentIndices[i] >= 0)
                {
                    hasHoleWithParent = true;
                    Assert.That(parentIndices[i], Is.LessThan(contours.Count),
                        $"Parent index {parentIndices[i]} must be within contours range");
                    break;
                }
            }
            Assert.That(hasHoleWithParent, Is.True, "Should have at least one hole contour with a parent");
        }
    }

    [Test]
    public void FindContoursWithHierarchy_TwoRects_BothTopLevel()
    {
        using var bitmap = CreateBitmap(20, 10, b =>
        {
            FillRect(b, 1, 1, 4, 4);
            FillRect(b, 14, 1, 4, 4);
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            Assert.That(contours.Count, Is.GreaterThanOrEqualTo(2));

            for (int i = 0; i < parentIndices.Count; i++)
            {
                Assert.That(parentIndices[i], Is.EqualTo(-1),
                    $"Contour {i} should have no parent (separate filled rects have no holes)");
            }
        }
    }

    [Test]
    public void FindContoursWithHierarchy_NestedShapes_HolesHaveCorrectParents()
    {
        // Two separate ring shapes: each should have their hole point to their own outer contour
        using var bitmap = CreateBitmap(20, 10, b =>
        {
            FillRect(b, 1, 1, 6, 6);
            FillRect(b, 2, 2, 4, 4, alpha: 0);

            FillRect(b, 10, 1, 6, 6);
            FillRect(b, 11, 2, 4, 4, alpha: 0);
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            Assert.That(contours.Count, Is.GreaterThanOrEqualTo(4));

            int topLevelCount = 0;
            int holeCount = 0;
            for (int i = 0; i < parentIndices.Count; i++)
            {
                if (parentIndices[i] == -1)
                    topLevelCount++;
                else
                    holeCount++;
            }

            Assert.That(topLevelCount, Is.GreaterThanOrEqualTo(2), "Should have 2 top-level outer contours");
            Assert.That(holeCount, Is.GreaterThanOrEqualTo(2), "Should have 2 hole contours");

            for (int i = 0; i < parentIndices.Count; i++)
            {
                int p = parentIndices[i];
                if (p >= 0)
                {
                    Assert.That(parentIndices[p], Is.EqualTo(-1),
                        $"Hole contour {i}'s parent ({p}) should itself be a top-level contour");
                }
            }
        }
    }

    /// <summary>
    /// Simulates the 国 kanji scenario: a hollow outer square with an inner solid shape (王).
    /// PartsSplitEffect should produce the frame (口) and the inner shape (王) separately.
    /// The hole contour of the outer ring must be exactly ONE contour, not fragmented.
    /// </summary>
    [Test]
    public void FindContoursWithHierarchy_HollowSquareWithInnerShape_OneHoleContour()
    {
        using var bitmap = CreateBitmap(20, 20, b =>
        {
            FillRect(b, 1, 1, 18, 18);
            FillRect(b, 4, 4, 12, 12, alpha: 0);

            FillRect(b, 7, 7, 6, 6);
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            Assert.That(contours.Count, Is.EqualTo(3),
                "Should have exactly 3 contours: outer ring, its hole, and the inner shape");

            int topLevelCount = 0;
            int holeCount = 0;
            for (int i = 0; i < parentIndices.Count; i++)
            {
                if (parentIndices[i] == -1)
                    topLevelCount++;
                else
                    holeCount++;
            }

            Assert.That(topLevelCount, Is.EqualTo(2), "Should have 2 top-level outer contours (outer ring + inner shape)");
            Assert.That(holeCount, Is.EqualTo(1), "Should have exactly 1 hole contour (inner boundary of outer ring)");
        }
    }
}
