using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Media;

public sealed partial class PathGeometry : Geometry
{
    public PathGeometry()
    {
        ScanProperties<PathGeometry>();
    }

    public IListProperty<PathFigure> Figures { get; } = Property.CreateList<PathFigure>();

    public static PathGeometry Parse(string svg)
    {
        using var path = SKPath.ParseSvgPathData(svg);
        using SKPath.RawIterator it = path.CreateRawIterator();

        var result = new PathGeometry();
        var currentFigure = new PathFigure();
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPathVerb pathVerb;

        do
        {
            pathVerb = it.Next(points);
            PathSegment? segment;

            if (pathVerb == SKPathVerb.Move)
            {
                if (!currentFigure.StartPoint.CurrentValue.IsInvalid || currentFigure.Segments.Count > 0)
                {
                    result.Figures.Add(currentFigure);
                    currentFigure = new PathFigure();
                }

                currentFigure.StartPoint.CurrentValue = points[0].ToGraphicsPoint();
            }
            else if (pathVerb == SKPathVerb.Close)
            {
                currentFigure.IsClosed.CurrentValue = true;
                result.Figures.Add(currentFigure);
                currentFigure = new PathFigure();
            }

            segment = pathVerb switch
            {
                SKPathVerb.Line => new LineSegment(points[1].ToGraphicsPoint()),
                SKPathVerb.Quad => new QuadraticBezierSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint()),
                SKPathVerb.Conic => new ConicSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), it.ConicWeight()),
                SKPathVerb.Cubic => new CubicBezierSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), points[3].ToGraphicsPoint()),
                SKPathVerb.Done or _ => null,
            };

            if (segment != null)
            {
                currentFigure.Segments.Add(segment);
            }

        } while (pathVerb != SKPathVerb.Done);

        if (currentFigure.Segments.Count > 0)
            result.Figures.Add(currentFigure);

        return result;
    }

    [Obsolete("Use 'StartPoint'.")]
    public void MoveTo(Point point)
    {
        var figure = new PathFigure
        {
            StartPoint = { CurrentValue = point }
        };
        Figures.Add(figure);
    }

    private PathFigure GetLastOrAdd()
    {
        if (Figures.Count > 0)
        {
            return Figures[^1];
        }
        else
        {
            var figure = new PathFigure();
            Figures.Add(figure);
            return figure;
        }
    }

    public void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point)
    {
        GetLastOrAdd().Segments.Add(new ArcSegment()
        {
            Radius = { CurrentValue = radius },
            RotationAngle = { CurrentValue = angle },
            IsLargeArc = { CurrentValue = isLargeArc },
            SweepClockwise = { CurrentValue = sweepClockwise },
            Point = { CurrentValue = point }
        });
    }

    public void ConicTo(Point controlPoint, Point endPoint, float weight)
    {
        GetLastOrAdd().Segments.Add(new ConicSegment()
        {
            ControlPoint = { CurrentValue = controlPoint },
            EndPoint = { CurrentValue = endPoint },
            Weight = { CurrentValue = weight }
        });
    }

    public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        GetLastOrAdd().Segments.Add(new CubicBezierSegment()
        {
            ControlPoint1 = { CurrentValue = controlPoint1 },
            ControlPoint2 = { CurrentValue = controlPoint2 },
            EndPoint = { CurrentValue = endPoint }
        });
    }

    public void LineTo(Point point)
    {
        GetLastOrAdd().Segments.Add(new LineSegment(point));
    }

    public void QuadraticTo(Point controlPoint, Point endPoint)
    {
        GetLastOrAdd().Segments.Add(new QuadraticBezierSegment()
        {
            ControlPoint = { CurrentValue = controlPoint },
            EndPoint = { CurrentValue = endPoint }
        });
    }

    [Obsolete("Use 'IsClosed'.")]
    public void Close()
    {
        if (Figures.Count > 0)
        {
            Figures[^1].IsClosed.CurrentValue = true;
        }
    }

    public override void ApplyTo(IGeometryContext context, Geometry.Resource resource)
    {
        base.ApplyTo(context, resource);
        var r = (Resource)resource;

        foreach (PathFigure.Resource item in r.Figures)
        {
            item.GetOriginal().ApplyTo(context, item);
        }
    }

    public PathFigure.Resource? HitTestFigure(Point point, Pen.Resource? pen, Geometry.Resource resource)
    {
        var r = (Resource)resource;
        Rect bounds = resource.Bounds;

        foreach (PathFigure.Resource item in r.Figures)
        {
            using (var context = new GeometryContext())
            {
                context.FillType = r.FillType;
                item.GetOriginal().ApplyTo(context, item);
                if (r.Transform != null)
                {
                    context.Transform(r.Transform.Matrix);
                }

                if (context.NativeObject.Contains(point.X, point.Y))
                {
                    return item;
                }

                if (pen != null)
                {
                    using SKPath strokePath = PenHelper.CreateStrokePath(context.NativeObject, pen, bounds);
                    if (strokePath.Contains(point.X, point.Y))
                    {
                        return item;
                    }
                }

            }
        }

        return null;
    }
}
