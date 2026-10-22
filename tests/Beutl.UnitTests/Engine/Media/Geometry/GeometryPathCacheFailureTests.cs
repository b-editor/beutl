using Beutl.Graphics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

[TestFixture]
public sealed class GeometryPathCacheFailureTests
{
    [Test]
    public void ShippedOrdering_KeepsThrowingInsteadOfServingAnEmptyPath()
    {
        using var resource = new ThrowingGeometryResource();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
            Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
            Assert.Throws<InvalidOperationException>(() => _ = resource.FillContains(new Point(1, 1)));
            Assert.Throws<InvalidOperationException>(() => _ = resource.GetRenderBounds(null));
            Assert.That(resource.Calls, Is.EqualTo(4), "each entry point must retry the build, not serve a stale one");
        }
    }

    [Test]
    public void ARebuildThatSucceedsAfterAFailure_ProducesTheCompletePath()
    {
        using var resource = new ThrowingGeometryResource();

        Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
        resource.Throw = false;

        Assert.That(resource.Bounds, Is.EqualTo(new Rect(0, 0, 20, 10)));
    }

    [Test]
    public void AFailedRebuild_DoesNotReleaseThePathAPreviousBuildProduced()
    {
        using var resource = new ThrowingGeometryResource { Throw = false };
        SKPath first = resource.GetCachedPath();

        resource.Throw = true;
        resource.InvalidateCachedPaths();
        Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);

        // SKPath.Handle is zero once the owning GeometryContext has disposed it. This must stop the test
        // rather than collect into a multiple scope: reading TightBounds off a released path crashes the host.
        Assert.That(first.Handle, Is.Not.EqualTo(IntPtr.Zero),
            "the failed rebuild released the path the previous successful build produced");
        Assert.That(first.TightBounds.ToGraphicsRect(), Is.EqualTo(new Rect(0, 0, 20, 10)));

        resource.Throw = false;
        resource.InvalidateCachedPaths();
        Assert.That(resource.Bounds, Is.EqualTo(new Rect(0, 0, 20, 10)));
    }

    [Test]
    public void AThrowingSegment_MakesTheEnclosingGeometryKeepThrowing()
    {
        using var resource = new PathGeometry.Resource
        {
            Figures =
            {
                new PathFigure.Resource
                {
                    StartPoint = new Point(0, 0),
                    Segments = { new ThrowingSegmentResource() },
                },
            },
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
            Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
        }
    }

    private sealed class ThrowingSegmentResource : PathSegment.Resource
    {
        public override void ApplyTo(IGeometryContext context) => throw new InvalidOperationException("author failure");
    }

    private sealed class ThrowingGeometryResource : Geometry.Resource
    {
        public bool Throw { get; set; } = true;

        public int Calls { get; private set; }

        public override void ApplyTo(IGeometryContext context)
        {
            Calls++;
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(20, 10));
            if (Throw)
                throw new InvalidOperationException("author failure");
        }
    }
}
