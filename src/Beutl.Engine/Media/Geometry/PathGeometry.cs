using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

using SkiaSharp;

namespace Beutl.Media;

public sealed class PathGeometry : Geometry
{
    public static readonly CoreProperty<PathFigures> FiguresProperty;
    private readonly PathFigures _figures = [];

    static PathGeometry()
    {
        FiguresProperty = ConfigureProperty<PathFigures, PathGeometry>(nameof(Figures))
            .Accessor(o => o.Figures, (o, v) => o.Figures = v)
            .Register();

        AffectsRender<PathGeometry>(FiguresProperty);
    }

    public PathGeometry()
    {
        _figures.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public PathFigures Figures
    {
        get => _figures;
        set => _figures.Replace(value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Figures), Figures);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<PathFigures>(nameof(Figures)) is { } figures)
        {
            Figures = figures;
        }
    }

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
                if (!currentFigure.StartPoint.IsInvalid || currentFigure.Segments.Count > 0)
                {
                    result.Figures.Add(currentFigure);
                    currentFigure = new PathFigure();
                }

                currentFigure.StartPoint = points[0].ToGraphicsPoint();
            }
            else if (pathVerb == SKPathVerb.Close)
            {
                currentFigure.IsClosed = true;
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
            StartPoint = point
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
            Radius = radius,
            RotationAngle = angle,
            IsLargeArc = isLargeArc,
            SweepClockwise = sweepClockwise,
            Point = point
        });
    }

    public void ConicTo(Point controlPoint, Point endPoint, float weight)
    {
        GetLastOrAdd().Segments.Add(new ConicSegment()
        {
            ControlPoint = controlPoint,
            EndPoint = endPoint,
            Weight = weight
        });
    }

    public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        GetLastOrAdd().Segments.Add(new CubicBezierSegment()
        {
            ControlPoint1 = controlPoint1,
            ControlPoint2 = controlPoint2,
            EndPoint = endPoint
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
            ControlPoint = controlPoint,
            EndPoint = endPoint
        });
    }

    [Obsolete("Use 'IsClosed'.")]
    public void Close()
    {
        if (Figures.Count > 0)
        {
            Figures[^1].IsClosed = true;
        }
    }

    public override void ApplyTo(IGeometryContext context)
    {
        base.ApplyTo(context);

        foreach (PathFigure item in Figures)
        {
            item.ApplyTo(context);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (var item in Figures)
        {
            item.ApplyAnimations(clock);
        }
    }

    public PathFigure? HitTestFigure(Point point, IPen? pen)
    {
        Rect bounds = Bounds;

        foreach (PathFigure item in Figures)
        {
            using (var context = new GeometryContext())
            {
                context.FillType = FillType;
                item.ApplyTo(context);
                if (Transform?.IsEnabled == true)
                {
                    context.Transform(Transform.Value);
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
