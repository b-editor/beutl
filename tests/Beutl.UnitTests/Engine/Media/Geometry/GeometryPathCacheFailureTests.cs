using Beutl.Graphics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

/// <summary>
/// A failing <c>ApplyTo</c> must not be able to install a half-built path behind the version guard.
/// </summary>
/// <remarks>
/// <see cref="PreChangeOrdering"/> reproduces the field-write order this cache used at 989856e8d, so the
/// difference is measured in one process rather than read off the diff.
/// </remarks>
[TestFixture]
public sealed class GeometryPathCacheFailureTests
{
    [Test]
    public void PreChangeOrdering_ServedWhateverWasBuiltBeforeTheThrow()
    {
        using var partial = new PreChangeOrdering();
        using var immediate = new PreChangeOrdering();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidOperationException>(() => partial.GetCachedPath(ThrowingApplyTo));
            Assert.That(partial.GetCachedPath(ThrowingApplyTo).TightBounds.ToGraphicsRect(),
                Is.EqualTo(new Rect(0, 0, 20, 10)),
                "the guard passes on the next call and hands back the two segments recorded before the throw");
            Assert.That(partial.GetCachedPath(ThrowingApplyTo).TightBounds.ToGraphicsRect(),
                Is.EqualTo(new Rect(0, 0, 20, 10)));

            Assert.Throws<InvalidOperationException>(
                () => immediate.GetCachedPath(static _ => throw new InvalidOperationException("author failure")));
            Assert.That(
                immediate.GetCachedPath(static _ => throw new InvalidOperationException("author failure"))
                    .TightBounds.ToGraphicsRect(),
                Is.EqualTo(default(Rect)),
                "an author that throws before recording anything degrades to an empty path instead");
        }
    }

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
    public void AFallbackSegment_MakesTheEnclosingGeometryKeepThrowing()
    {
        using var resource = new PathGeometry.Resource
        {
            Figures =
            {
                new PathFigure.Resource
                {
                    StartPoint = new Point(0, 0),
                    Segments = { new FallbackPathSegment.Resource() },
                },
            },
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
            Assert.Throws<InvalidOperationException>(() => _ = resource.Bounds);
        }
    }

    private static void ThrowingApplyTo(IGeometryContext context)
    {
        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(20, 10));
        throw new InvalidOperationException("author failure");
    }

    private sealed class ThrowingGeometryResource : Geometry.Resource
    {
        public ThrowingGeometryResource()
        {
        }

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

    /// <summary>
    /// The cache orchestration <c>Geometry.Resource.GetCachedPath</c> used at 989856e8d, transcribed so its
    /// behaviour on a throwing build can be observed alongside the shipped one.
    /// </summary>
    private sealed class PreChangeOrdering : IDisposable
    {
        private int? _capturedVersion;
        private GeometryContext? _cachedPath;

        public int Version { get; set; }

        public SKPath GetCachedPath(Action<IGeometryContext> applyTo)
        {
            if (_capturedVersion != Version || _cachedPath == null)
            {
                _capturedVersion = Version;
                _cachedPath?.Dispose();

                _cachedPath = new GeometryContext();
                applyTo(_cachedPath);
            }

            return _cachedPath.NativeObject;
        }

        public void Dispose() => _cachedPath?.Dispose();
    }
}
