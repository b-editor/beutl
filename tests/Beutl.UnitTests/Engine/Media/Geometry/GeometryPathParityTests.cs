using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

/// <summary>
/// Compares the shipped path against <see cref="PreChangeGeometryPath"/>, a transcription of the same
/// construction as it stood at 989856e8d, in one process and element by element.
/// </summary>
[TestFixture]
public sealed class GeometryPathParityTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData((Func<Geometry>)(() => new EllipseGeometry
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 50 },
        })).SetName("Ellipse");

        yield return new TestCaseData((Func<Geometry>)(() => new EllipseGeometry
        {
            Width = { CurrentValue = float.PositiveInfinity },
            Height = { CurrentValue = 50 },
        })).SetName("EllipseWithInfiniteWidth");

        yield return new TestCaseData((Func<Geometry>)(() => new RectGeometry
        {
            Width = { CurrentValue = 30 },
            Height = { CurrentValue = 40 },
        })).SetName("Rect");

        yield return new TestCaseData((Func<Geometry>)(() =>
        {
            var geometry = new RectGeometry
            {
                Width = { CurrentValue = 30 },
                Height = { CurrentValue = 40 },
            };
            geometry.FillType.CurrentValue = PathFillType.EvenOdd;
            geometry.Transform.CurrentValue = new RotationTransform { Rotation = { CurrentValue = 30 } };
            return geometry;
        })).SetName("RectWithTransformAndFillType");

        yield return new TestCaseData((Func<Geometry>)(() => new RoundedRectGeometry
        {
            Width = { CurrentValue = 120 },
            Height = { CurrentValue = 80 },
            CornerRadius = { CurrentValue = new CornerRadius(12, 4, 30, 0) },
            Smoothing = { CurrentValue = 60 },
        })).SetName("RoundedRectSmoothed");

        yield return new TestCaseData((Func<Geometry>)(() => new RoundedRectGeometry
        {
            Width = { CurrentValue = 120 },
            Height = { CurrentValue = 80 },
            CornerRadius = { CurrentValue = new CornerRadius(0) },
            Smoothing = { CurrentValue = 0 },
        })).SetName("RoundedRectSquare");

        yield return new TestCaseData((Func<Geometry>)(() =>
            PathGeometry.Parse("M 10 10 L 60 10 Q 80 30 60 50 C 40 60 20 60 10 50 Z")))
            .SetName("PathGeometryParsed");

        yield return new TestCaseData((Func<Geometry>)AllSegmentKinds).SetName("PathGeometryAllSegmentKinds");

        yield return new TestCaseData((Func<Geometry>)(() =>
        {
            PathGeometry geometry = AllSegmentKinds();
            geometry.Figures[0].StartPoint.CurrentValue = new Point(float.NaN, float.NaN);
            return geometry;
        })).SetName("PathGeometryWithoutStartPoint");

        yield return new TestCaseData((Func<Geometry>)(() =>
        {
            PathGeometry geometry = AllSegmentKinds();
            geometry.Figures[0].StartPoint.CurrentValue = new Point(float.NaN, float.NaN);
            geometry.Figures[0].IsClosed.CurrentValue = true;
            return geometry;
        })).SetName("PathGeometryClosedWithoutStartPoint");

        yield return new TestCaseData((Func<Geometry>)(() =>
        {
            PathGeometry geometry = AllSegmentKinds();
            geometry.Figures.Add(new PathFigure
            {
                StartPoint = { CurrentValue = new Point(200, 200) },
                Segments = { new LineSegment(new Point(260, 240)) },
            });
            return geometry;
        })).SetName("PathGeometryMultipleFigures");
    }

    private static PathGeometry AllSegmentKinds()
    {
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure
        {
            StartPoint = { CurrentValue = new Point(5, 5) },
            Segments =
            {
                new LineSegment(new Point(50, 5)),
                new QuadraticBezierSegment(new Point(70, 15), new Point(50, 35)),
                new CubicBezierSegment(new Point(40, 55), new Point(20, 55), new Point(10, 40)),
                new ConicSegment(new Point(0, 25), new Point(5, 5), 0.7f),
                new ArcSegment
                {
                    Radius = { CurrentValue = new Size(20, 12) },
                    RotationAngle = { CurrentValue = 15 },
                    IsLargeArc = { CurrentValue = true },
                    SweepClockwise = { CurrentValue = false },
                    Point = { CurrentValue = new Point(40, 20) },
                },
            },
        });
        return geometry;
    }

    [TestCaseSource(nameof(Cases))]
    public void ShippedPath_MatchesThePreChangePathElementByElement(Func<Geometry> create)
    {
        Geometry geometry = create();
        using Geometry.Resource resource = geometry.ToResource(CompositionContext.Default);
        using GeometryContext expected = PreChangeGeometryPath.Build(resource);

        IReadOnlyList<string> before = PreChangeGeometryPath.Describe(expected.NativeObject);
        IReadOnlyList<string> after = PreChangeGeometryPath.Describe(resource.GetCachedPath());

        Assert.That(after, Is.EqualTo(before).AsCollection);
    }

    [TestCaseSource(nameof(Cases))]
    public void DetachedResource_ProducesTheSamePathAsItsAttachedCounterpart(Func<Geometry> create)
    {
        Geometry geometry = create();
        using Geometry.Resource attached = geometry.ToResource(CompositionContext.Default);
        Geometry.Resource detached = Detach(attached);
        try
        {
            IReadOnlyList<string> expected = PreChangeGeometryPath.Describe(attached.GetCachedPath());
            IReadOnlyList<string> actual = PreChangeGeometryPath.Describe(detached.GetCachedPath());

            Assert.That(actual, Is.EqualTo(expected).AsCollection);
        }
        finally
        {
            detached.Dispose();
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void StrokeAndHitTestResults_MatchThePreChangePath(Func<Geometry> create)
    {
        Geometry geometry = create();
        using Geometry.Resource resource = geometry.ToResource(CompositionContext.Default);
        using Pen.Resource pen = new Pen
        {
            Brush = { CurrentValue = Brushes.Black },
            Thickness = { CurrentValue = 6 },
        }.ToResource(CompositionContext.Default);
        using GeometryContext expected = PreChangeGeometryPath.Build(resource);
        Rect expectedBounds = expected.NativeObject.TightBounds.ToGraphicsRect();
        using SKPath expectedStroke = PenHelper.CreateStrokePath(expected.NativeObject, pen, expectedBounds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resource.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(
                PreChangeGeometryPath.Describe(resource.GetCachedStrokePath(pen)),
                Is.EqualTo(PreChangeGeometryPath.Describe(expectedStroke)).AsCollection);
            Assert.That(
                resource.GetRenderBounds(pen),
                Is.EqualTo(expectedStroke.TightBounds.ToGraphicsRect()));
            Assert.That(
                resource.FillContains(expectedBounds.Center),
                Is.EqualTo(expected.NativeObject.Contains(expectedBounds.Center.X, expectedBounds.Center.Y)));
        }
    }

    /// <summary>
    /// Rebuilds <paramref name="attached"/>'s value graph into resources that never went through
    /// <c>ToResource</c>, which is the shape a plugin author constructs by hand.
    /// </summary>
    private static Geometry.Resource Detach(Geometry.Resource attached)
    {
        Geometry.Resource copy = attached switch
        {
            EllipseGeometry.Resource r => new EllipseGeometry.Resource { Width = r.Width, Height = r.Height },
            RectGeometry.Resource r => new RectGeometry.Resource { Width = r.Width, Height = r.Height },
            RoundedRectGeometry.Resource r => new RoundedRectGeometry.Resource
            {
                Width = r.Width,
                Height = r.Height,
                CornerRadius = r.CornerRadius,
                Smoothing = r.Smoothing,
            },
            PathGeometry.Resource r => DetachPath(r),
            _ => throw new NotSupportedException(attached.GetType().ToString()),
        };

        copy.FillType = attached.FillType;
        copy.Transform = attached.Transform;
        return copy;
    }

    private static PathGeometry.Resource DetachPath(PathGeometry.Resource source)
    {
        var copy = new PathGeometry.Resource();
        foreach (PathFigure.Resource figure in source.Figures)
        {
            var figureCopy = new PathFigure.Resource
            {
                StartPoint = figure.StartPoint,
                IsClosed = figure.IsClosed,
            };
            foreach (PathSegment.Resource segment in figure.Segments)
            {
                figureCopy.Segments.Add(segment switch
                {
                    LineSegment.Resource s => new LineSegment.Resource { Point = s.Point },
                    QuadraticBezierSegment.Resource s => new QuadraticBezierSegment.Resource
                    {
                        ControlPoint = s.ControlPoint,
                        EndPoint = s.EndPoint,
                    },
                    CubicBezierSegment.Resource s => new CubicBezierSegment.Resource
                    {
                        ControlPoint1 = s.ControlPoint1,
                        ControlPoint2 = s.ControlPoint2,
                        EndPoint = s.EndPoint,
                    },
                    ConicSegment.Resource s => new ConicSegment.Resource
                    {
                        ControlPoint = s.ControlPoint,
                        EndPoint = s.EndPoint,
                        Weight = s.Weight,
                    },
                    ArcSegment.Resource s => new ArcSegment.Resource
                    {
                        Radius = s.Radius,
                        RotationAngle = s.RotationAngle,
                        IsLargeArc = s.IsLargeArc,
                        SweepClockwise = s.SweepClockwise,
                        Point = s.Point,
                    },
                    _ => throw new NotSupportedException(segment.GetType().ToString()),
                });
            }

            copy.Figures.Add(figureCopy);
        }

        return copy;
    }
}
