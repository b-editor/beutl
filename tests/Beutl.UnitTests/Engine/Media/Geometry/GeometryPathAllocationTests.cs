using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

/// <summary>
/// <c>GetCachedPath</c> is on the render path, so a cache hit has to stay allocation-free and a rebuild must
/// not cost more than the pre-change construction transcribed in <see cref="PreChangeGeometryPath"/>.
/// </summary>
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

    [Test]
    public void ARebuild_DoesNotCostMoreThanThePreChangeConstruction()
    {
        using Geometry.Resource shipped = CreateGeometry().ToResource(CompositionContext.Default);
        using Geometry.Resource reference = CreateGeometry().ToResource(CompositionContext.Default);

        const int rebuildIterations = 2000;
        long before = Measure(
            () =>
            {
                using GeometryContext context = PreChangeGeometryPath.Build(reference);
                _ = context.NativeObject;
            },
            rebuildIterations);
        long after = Measure(
            () =>
            {
                shipped.InvalidateCachedPaths();
                _ = shipped.GetCachedPath();
            },
            rebuildIterations);

        Assert.That(after, Is.LessThanOrEqualTo(before),
            $"rebuild allocated {after} bytes against the pre-change {before}");
    }

    private static PathGeometry CreateGeometry()
    {
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure
        {
            StartPoint = { CurrentValue = new Point(5, 5) },
            IsClosed = { CurrentValue = true },
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
