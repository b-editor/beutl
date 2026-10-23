using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

[TestFixture]
public sealed class GeometryPathAllocationTests
{
    private const int Iterations = 20000;
    private const int Rounds = 5;

    [Test]
    public void ACacheHit_DoesNotAllocate()
    {
        using Geometry.Resource resource = CreateGeometry().ToResource(CompositionContext.Default);
        _ = resource.GetCachedPath();

        Assert.That(Measure(() => resource.GetCachedPath(), Iterations), Is.Zero);
    }

    [Test]
    public void ADetachedCacheHit_DoesNotAllocate()
    {
        using var resource = new EllipseGeometry.Resource { Width = 100, Height = 50 };
        _ = resource.GetCachedPath();

        Assert.That(Measure(() => resource.GetCachedPath(), Iterations), Is.Zero);
    }

    private static PathGeometry CreateGeometry()
    {
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure
        {
            StartPoint = { Value = new Point(5, 5) },
            IsClosed = { Value = true },
            Segments =
            {
                new LineSegment(new Point(50, 5)),
                new QuadraticBezierSegment(new Point(70, 15), new Point(50, 35)),
                new CubicBezierSegment(new Point(40, 55), new Point(20, 55), new Point(10, 40)),
            },
        });
        return geometry;
    }

    private static long Measure(Action action, int iterations)
    {
        for (int index = 0; index < 200; index++)
            action();

        long best = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < iterations; index++)
                action();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
        }

        return best;
    }
}
