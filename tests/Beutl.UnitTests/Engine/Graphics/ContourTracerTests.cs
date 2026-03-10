using Beutl.Collections.Pooled;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class ContourTracerTests
{
    private static Bitmap<Bgra8888> CreateBitmap(int width, int height, Action<Bitmap<Bgra8888>> draw)
    {
        var bitmap = new Bitmap<Bgra8888>(width, height);
        draw(bitmap);
        return bitmap;
    }

    private static void FillRect(Bitmap<Bgra8888> bitmap, int x, int y, int w, int h, byte alpha = 255)
    {
        var pixel = new Bgra8888(255, 255, 255, alpha);
        for (int row = y; row < y + h && row < bitmap.Height; row++)
        {
            Span<Bgra8888> span = bitmap[row];
            for (int col = x; col < x + w && col < bitmap.Width; col++)
            {
                span[col] = pixel;
            }
        }
    }

    [Test]
    public void FindContours_EmptyImage_ReturnsNoContours()
    {
        using var bitmap = new Bitmap<Bgra8888>(10, 10);

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Is.Empty);
    }

    [Test]
    public void FindContours_FullyFilledImage_ReturnsOneContour()
    {
        using var bitmap = CreateBitmap(10, 10, b => b.Fill(new Bgra8888(255, 255, 255, 255)));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
        Assert.That(contours[0].Length, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void FindContours_SinglePixel_ReturnsSinglePointContour()
    {
        using var bitmap = CreateBitmap(5, 5, b =>
        {
            b[2, 2] = new Bgra8888(255, 255, 255, 255);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
        Assert.That(contours[0].Length, Is.EqualTo(1));
        Assert.That(contours[0][0], Is.EqualTo(new PixelPoint(2, 2)));
    }

    [Test]
    public void FindContours_SmallRectangle_ContoursContainCornerPoints()
    {
        // 5x5 bitmap with a 3x3 filled rect at (1,1)
        using var bitmap = CreateBitmap(5, 5, b => FillRect(b, 1, 1, 3, 3));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));

        // Collect all contour points
        var allPoints = new HashSet<PixelPoint>();
        foreach (var contour in contours)
        {
            foreach (var pt in contour)
                allPoints.Add(pt);
        }

        // The contour should include points on the border of the 3x3 rect
        Assert.That(allPoints, Does.Contain(new PixelPoint(1, 1)));
        Assert.That(allPoints, Does.Contain(new PixelPoint(3, 1)));
        Assert.That(allPoints, Does.Contain(new PixelPoint(1, 3)));
        Assert.That(allPoints, Does.Contain(new PixelPoint(3, 3)));
    }

    [Test]
    public void FindContours_RectangleWithHole_ReturnsMultipleContours()
    {
        // 10x10 bitmap, fill outer 10x10, clear inner 4x4 (creating a hole)
        using var bitmap = CreateBitmap(10, 10, b =>
        {
            b.Fill(new Bgra8888(255, 255, 255, 255));
            // Clear inner region (3,3)-(6,6)
            var empty = new Bgra8888(0, 0, 0, 0);
            for (int row = 3; row <= 6; row++)
            {
                Span<Bgra8888> span = b[row];
                for (int col = 3; col <= 6; col++)
                {
                    span[col] = empty;
                }
            }
        });

        using var contours = ContourTracer.FindContours(bitmap);

        // Should have at least 2 contours: outer border and hole border
        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void FindContours_TwoSeparateRegions_ReturnsTwoOuterContours()
    {
        // Two separate rectangles
        using var bitmap = CreateBitmap(20, 10, b =>
        {
            FillRect(b, 1, 1, 3, 3);   // Left rect
            FillRect(b, 15, 1, 3, 3);  // Right rect (far away)
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void FindContours_HorizontalLine_ReturnsContour()
    {
        // Horizontal line of 5 pixels at row 3
        using var bitmap = CreateBitmap(10, 7, b => FillRect(b, 2, 3, 5, 1));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FindContours_VerticalLine_ReturnsContour()
    {
        // Vertical line of 5 pixels at col 3
        using var bitmap = CreateBitmap(7, 10, b => FillRect(b, 3, 2, 1, 5));

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FindContours_SemiTransparentPixels_TreatedAsForeground()
    {
        // Pixels with alpha=1 should be treated as foreground
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
    public void FindContours_CornerPixel_ReturnsContour()
    {
        // Pixel at (0,0) - edge case for boundary handling
        using var bitmap = CreateBitmap(5, 5, b =>
        {
            b[0, 0] = new Bgra8888(255, 255, 255, 255);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
        Assert.That(contours[0].Length, Is.EqualTo(1));
        Assert.That(contours[0][0], Is.EqualTo(new PixelPoint(0, 0)));
    }

    [Test]
    public void FindContours_EdgeFilledRow_ReturnsContour()
    {
        // Fill entire top row - tests boundary handling
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
        // L-shaped region
        using var bitmap = CreateBitmap(10, 10, b =>
        {
            FillRect(b, 1, 1, 2, 5); // Vertical part
            FillRect(b, 1, 5, 5, 2); // Horizontal part
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));

        // L-shape has more corners than a rectangle
        var contour = contours[0];
        Assert.That(contour.Length, Is.GreaterThanOrEqualTo(6),
            "L-shape should have at least 6 corner points");
    }

    [Test]
    public void FindContours_1x1Image_SinglePixelFilled()
    {
        using var bitmap = CreateBitmap(1, 1, b =>
        {
            b[0, 0] = new Bgra8888(255, 255, 255, 255);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Has.Count.EqualTo(1));
        Assert.That(contours[0].Length, Is.EqualTo(1));
        Assert.That(contours[0][0], Is.EqualTo(new PixelPoint(0, 0)));
    }

    [Test]
    public void FindContours_1x1Image_Empty()
    {
        using var bitmap = new Bitmap<Bgra8888>(1, 1);

        using var contours = ContourTracer.FindContours(bitmap);

        Assert.That(contours.List, Is.Empty);
    }

    [Test]
    public void FindContours_DiagonalPixels_ReturnsContours()
    {
        // Diagonal line of single pixels - each should be a separate contour
        // (since they are 4-connected separated but 8-connected adjacent,
        // behavior depends on implementation)
        using var bitmap = CreateBitmap(5, 5, b =>
        {
            b[0, 0] = new Bgra8888(255, 255, 255, 255);
            b[1, 1] = new Bgra8888(255, 255, 255, 255);
            b[2, 2] = new Bgra8888(255, 255, 255, 255);
        });

        using var contours = ContourTracer.FindContours(bitmap);

        // Should find at least one contour
        Assert.That(contours.List, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FindContoursWithHierarchy_FilledRect_HoleHasParent()
    {
        // 12x12 bitmap: outer 10x10 filled, inner 4x4 hole → ring shape
        using var bitmap = CreateBitmap(12, 12, b =>
        {
            FillRect(b, 1, 1, 10, 10);
            // Clear inner hole (4,4)-(7,7)
            var empty = new Bgra8888(0, 0, 0, 0);
            for (int row = 4; row <= 7; row++)
            {
                Span<Bgra8888> span = b[row];
                for (int col = 4; col <= 7; col++)
                    span[col] = empty;
            }
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            // Should have at least 2: outer contour and hole contour
            Assert.That(contours.Count, Is.GreaterThanOrEqualTo(2));

            // There must be at least one top-level outer contour (parent == -1)
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

            // There must be at least one hole contour whose parent is the outer contour
            bool hasHoleWithParent = false;
            for (int i = 0; i < parentIndices.Count; i++)
            {
                if (parentIndices[i] >= 0)
                {
                    hasHoleWithParent = true;
                    // Parent index must be valid
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
        // Two separate filled rectangles, no holes → both outer contours, no parents
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

            // All outer contours should have parent == -1 (no holes in these rects)
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
        // Ring 1: outer at (1,1) 6x6, hole at (2,2) 4x4
        // Ring 2: outer at (10,1) 6x6, hole at (11,2) 4x4
        using var bitmap = CreateBitmap(20, 10, b =>
        {
            FillRect(b, 1, 1, 6, 6);
            var empty = new Bgra8888(0, 0, 0, 0);
            for (int row = 2; row <= 5; row++)
            {
                Span<Bgra8888> span = b[row];
                for (int col = 2; col <= 5; col++) span[col] = empty;
            }

            FillRect(b, 10, 1, 6, 6);
            for (int row = 2; row <= 5; row++)
            {
                Span<Bgra8888> span = b[row];
                for (int col = 11; col <= 14; col++) span[col] = empty;
            }
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            // 2 outer + 2 hole = 4 contours
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

            // Each hole's parent must be a valid top-level contour (parent == -1)
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
        // 20x20 image:
        //  Outer ring: fg from (1,1) to (18,18), hole from (4,4) to (15,15)
        //  Inner shape: fg from (7,7) to (12,12) inside the hole
        using var bitmap = CreateBitmap(20, 20, b =>
        {
            // Outer ring
            FillRect(b, 1, 1, 18, 18);
            var empty = new Bgra8888(0, 0, 0, 0);
            for (int row = 4; row <= 15; row++)
            {
                Span<Bgra8888> span = b[row];
                for (int col = 4; col <= 15; col++)
                    span[col] = empty;
            }

            // Inner shape (inside the hole)
            FillRect(b, 7, 7, 6, 6);
        });

        ContourTracer.FindContoursWithHierarchy(bitmap, out Contours contours, out PooledList<int> parentIndices);
        using (contours)
        using (parentIndices)
        {
            // Expect exactly: outer ring contour + 1 hole contour + inner shape contour = 3
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
