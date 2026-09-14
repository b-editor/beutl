using Beutl.Graphics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

[TestFixture]
public sealed class GeometryContextTests
{
    [Test]
    public void Edits_BuildThePathTheMutableBaselineBuilds()
    {
        AssertParity((context, _) =>
        {
            context.FillType = PathFillType.EvenOdd;
            context.MoveTo(new Point(10, 10));
            context.LineTo(new Point(60, 12));
            context.QuadraticTo(new Point(80, 30), new Point(60, 50));
            context.ConicTo(new Point(40, 70), new Point(20, 50), 0.7f);
            context.CubicTo(new Point(10, 40), new Point(0, 30), new Point(10, 20));
            context.ArcTo(new Size(15, 10), 30, true, false, new Point(40, 30));
            context.Close();
            // A segment after Close starts its contour from the last move point.
            context.LineTo(new Point(90, 90));
            context.MoveTo(new Point(5, 95));
            // A zero radius degenerates to a line, and so does an arc to the current point.
            context.ArcTo(new Size(0, 10), 0, false, true, new Point(15, 95));
            context.ArcTo(new Size(5, 5), 0, false, true, new Point(15, 95));
        });
    }

    [Test]
    public void ReadsBetweenEdits_ObserveWhatTheMutableBaselineObserves()
    {
        AssertParity((context, probe) =>
        {
            Observe(context, probe);
            context.FillType = PathFillType.EvenOdd;
            context.MoveTo(new Point(4, 8));
            Observe(context, probe);
            context.CubicTo(new Point(20, 0), new Point(30, 4), new Point(34, 12));
            Observe(context, probe);
            // RoundedRectGeometry places each corner arc relative to where the previous segment ended.
            context.ArcTo(new Size(6, 6), 0, false, true, new Point(4, 4) + context.LastPoint);
            Observe(context, probe);
            context.Close();
            Observe(context, probe);
            context.Clear();
            Observe(context, probe);
        });
    }

    [Test]
    public void TransformBetweenEdits_ContinuesFromTheTransformedGeometry()
    {
        AssertParity((context, probe) =>
        {
            context.FillType = PathFillType.EvenOdd;
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(10, 0));
            context.Transform(Matrix.CreateScale(2, 3) * Matrix.CreateTranslation(5, 7));
            Observe(context, probe);
            context.LineTo(new Point(10, 10));
            context.Close();
            context.Transform(Matrix.CreateRotation(0.5f));
            Observe(context, probe);
        });
    }

    [Test]
    public void EditsAfterReadingNativeObject_ContinueFromIt()
    {
        AssertParity((context, probe) =>
        {
            context.MoveTo(new Point(1, 2));
            context.LineTo(new Point(30, 2));
            probe.Observed.Add($"points={probe.NativeObject().PointCount}");
            context.LineTo(new Point(30, 40));
            context.Close();
            probe.Observed.Add($"points={probe.NativeObject().PointCount}");
        });
    }

    [Test]
    public void EditsMadeThroughNativeObject_AreKept()
    {
        AssertParity((context, probe) =>
        {
            context.MoveTo(new Point(1, 2));
            context.LineTo(new Point(30, 2));
#pragma warning disable CS0618 // A caller holding the path can still edit it through SKPath's obsolete API.
            probe.NativeObject().AddRect(SKRect.Create(40, 40, 10, 10));
#pragma warning restore CS0618
            context.LineTo(new Point(30, 40));
        });
    }

    [Test]
    public void AddPath_AppendsWhatSKPathAddPathAppends()
    {
        using var ovalBuilder = new SKPathBuilder();
        ovalBuilder.AddOval(SKRect.Create(20, 20, 30, 10));
        using SKPath oval = ovalBuilder.Detach();

        AssertParity((context, probe) =>
        {
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(5, 5));
            probe.AddPath(oval);
            context.LineTo(new Point(50, 50));
        });
    }

    [Test]
    public void ReadingNativeObjectAgainWithoutAnEdit_ReturnsTheSameInstance()
    {
        using var context = new GeometryContext();
        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(10, 0));

        Assert.That(context.NativeObject, Is.SameAs(context.NativeObject));
    }

    [Test]
    public void APathReadBeforeAnEdit_StaysValidUntilTheContextIsDisposed()
    {
        SKPath first;
        SKPath second;
        using (var context = new GeometryContext())
        {
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(10, 0));
            first = context.NativeObject;
            context.LineTo(new Point(10, 10));
            second = context.NativeObject;

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(first.PointCount, Is.EqualTo(2));
                Assert.That(second.PointCount, Is.EqualTo(3));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(second.Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    private static void AssertParity(Action<IGeometryContext, Probe> edit)
    {
        using var baseline = new MutablePathContext();
        var baselineProbe = new Probe(() => baseline.NativeObject, baseline.AddPath, []);
        edit(baseline, baselineProbe);

        using var shipped = new GeometryContext();
        var shippedProbe = new Probe(() => shipped.NativeObject, shipped.AddPath, []);
        edit(shipped, shippedProbe);

        Assert.Multiple(() =>
        {
            Assert.That(shippedProbe.Observed, Is.EqualTo(baselineProbe.Observed));
            Assert.That(
                PreChangeGeometryPath.Describe(shipped.NativeObject),
                Is.EqualTo(PreChangeGeometryPath.Describe(baseline.NativeObject)));
        });
    }

    private static void Observe(IGeometryContext context, Probe probe)
    {
        Point last = context.LastPoint;
        Rect bounds = context.Bounds;
        probe.Observed.Add(
            $"fill={context.FillType} last=({last.X}, {last.Y}) bounds=({bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height})");
    }

    private sealed record Probe(Func<SKPath> NativeObject, Action<SKPath> AddPath, List<string> Observed);

    /// <summary>
    /// The SKPath-backed <see cref="GeometryContext"/> that the SKPathBuilder one replaced, transcribed as a
    /// parity baseline.
    /// </summary>
    /// <remarks>Edit only to correct transcription; it must not track the production implementation.</remarks>
    private sealed class MutablePathContext : IGeometryContext, IDisposable
    {
        public SKPath NativeObject { get; } = new();

        public PathFillType FillType
        {
            get => (PathFillType)NativeObject.FillType;
            set => NativeObject.FillType = (SKPathFillType)value;
        }

        public Point LastPoint => NativeObject.LastPoint.ToGraphicsPoint();

        public Rect Bounds => NativeObject.TightBounds.ToGraphicsRect();

#pragma warning disable CS0618 // SKPath's editing API is what the baseline is made of.
        public void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point)
        {
            NativeObject.ArcTo(
                r: new SKPoint(radius.Width, radius.Height),
                xAxisRotate: angle,
                largeArc: isLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,
                sweep: sweepClockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                xy: point.ToSKPoint());
        }

        public void Close() => NativeObject.Close();

        public void ConicTo(Point controlPoint, Point endPoint, float weight)
            => NativeObject.ConicTo(controlPoint.ToSKPoint(), endPoint.ToSKPoint(), weight);

        public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
            => NativeObject.CubicTo(controlPoint1.ToSKPoint(), controlPoint2.ToSKPoint(), endPoint.ToSKPoint());

        public void LineTo(Point point) => NativeObject.LineTo(point.ToSKPoint());

        public void MoveTo(Point point) => NativeObject.MoveTo(point.ToSKPoint());

        public void QuadraticTo(Point controlPoint, Point endPoint)
            => NativeObject.QuadTo(controlPoint.ToSKPoint(), endPoint.ToSKPoint());

        public void AddPath(SKPath path) => NativeObject.AddPath(path);
#pragma warning restore CS0618

        public void Clear() => NativeObject.Reset();

        public void Transform(Matrix matrix) => NativeObject.Transform(matrix.ToSKMatrix());

        public void Dispose() => NativeObject.Dispose();
    }
}
