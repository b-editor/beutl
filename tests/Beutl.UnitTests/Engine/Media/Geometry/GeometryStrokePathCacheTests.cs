using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

/// <summary>
/// <c>Geometry.Resource</c>'s stroke-path cache keys on the pen. Both halves of that key are exercised here:
/// which pen the cached stroke belongs to, and whether the fill path it was derived from is still current.
/// </summary>
[TestFixture]
public sealed class GeometryStrokePathCacheTests
{
    [Test]
    public void TwoDetachedPens_DoNotShareOneCachedStroke()
    {
        using Geometry.Resource geometry = CreateAttachedEllipse();
        using var thin = CreateDetachedPen(thickness: 4);
        using var thick = CreateDetachedPen(thickness: 20);

        Rect first = geometry.GetRenderBounds(thin);
        Rect second = geometry.GetRenderBounds(thick);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(new Rect(-2, -2, 104, 54)));
            Assert.That(second, Is.EqualTo(new Rect(-10, -10, 120, 70)),
                "keying the cache on GetOriginal() reads null for both detached pens and serves the thin stroke");
        }
    }

    [Test]
    public void TwoAttachedPens_DoNotShareOneCachedStroke()
    {
        using Geometry.Resource geometry = CreateAttachedEllipse();
        using Pen.Resource thin = CreateAttachedPen(thickness: 4);
        using Pen.Resource thick = CreateAttachedPen(thickness: 20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(geometry.GetRenderBounds(thin), Is.EqualTo(new Rect(-2, -2, 104, 54)));
            Assert.That(geometry.GetRenderBounds(thick), Is.EqualTo(new Rect(-10, -10, 120, 70)));
        }
    }

    [Test]
    public void OneDetachedPenReused_StillHitsTheCache()
    {
        using Geometry.Resource geometry = CreateAttachedEllipse();
        using var pen = CreateDetachedPen(thickness: 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(geometry.GetRenderBounds(pen), Is.EqualTo(new Rect(-2, -2, 104, 54)));
            Assert.That(geometry.GetRenderBounds(pen), Is.EqualTo(new Rect(-2, -2, 104, 54)));
        }
    }

    [Test]
    public void ADetachedPenWhoseVersionMoved_RebuildsTheStroke()
    {
        using Geometry.Resource geometry = CreateAttachedEllipse();
        using var pen = CreateDetachedPen(thickness: 4);
        Rect before = geometry.GetRenderBounds(pen);

        pen.Thickness = 20;
        pen.Version++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(new Rect(-2, -2, 104, 54)));
            Assert.That(geometry.GetRenderBounds(pen), Is.EqualTo(new Rect(-10, -10, 120, 70)));
        }
    }

    [Test]
    public void RebuildingTheFillPath_DropsTheStrokeBuiltFromTheOldOne()
    {
        using var geometry = new RectGeometry.Resource { Width = 100, Height = 50 };
        using Pen.Resource pen = CreateAttachedPen(thickness: 4);
        Rect before = geometry.GetRenderBounds(pen);

        geometry.Width = 200;
        geometry.InvalidateCachedPaths();
        // Rebuilding the fill path first is what leaves a stale stroke reachable: GetCachedStrokePath then
        // sees a matching version, a live fill path, a live stroke path, and the same pen.
        _ = geometry.Bounds;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(new Rect(-2, -2, 104, 54)));
            Assert.That(geometry.GetRenderBounds(pen), Is.EqualTo(new Rect(-2, -2, 204, 54)));
        }
    }

    private static Geometry.Resource CreateAttachedEllipse()
    {
        return new EllipseGeometry
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 50 },
        }.ToResource(CompositionContext.Default);
    }

    private static Pen.Resource CreateAttachedPen(float thickness)
    {
        return new Pen
        {
            Brush = { CurrentValue = Brushes.Black },
            Thickness = { CurrentValue = thickness },
        }.ToResource(CompositionContext.Default);
    }

    // A hand-built pen inherits default(T), not the declared default, so TrimEnd and MiterLimit are set
    // explicitly here; leaving TrimEnd at 0 trims the stroke away entirely.
    private static Pen.Resource CreateDetachedPen(float thickness)
    {
        return new Pen.Resource
        {
            Brush = Colors.Black.ToBrushResource(),
            Thickness = thickness,
            TrimEnd = 100,
            MiterLimit = 10,
        };
    }
}
