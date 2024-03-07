using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Serialization;

using SkiaSharp;

namespace Beutl.Media;

public sealed class PathGeometry : Geometry
{
    public static readonly CoreProperty<Point> StartPointProperty;
    public static readonly CoreProperty<bool> IsClosedProperty;
    public static readonly CoreProperty<PathSegments> OperationsProperty;
    private Point _startPoint;
    private bool _isClosed;
    private readonly PathSegments _operations = [];

    static PathGeometry()
    {
        StartPointProperty = ConfigureProperty<Point, PathGeometry>(nameof(StartPoint))
            .Accessor(o => o.StartPoint, (o, v) => o.StartPoint = v)
            .Register();

        IsClosedProperty = ConfigureProperty<bool, PathGeometry>(nameof(IsClosed))
            .Accessor(o => o.IsClosed, (o, v) => o.IsClosed = v)
            .Register();

        OperationsProperty = ConfigureProperty<PathSegments, PathGeometry>(nameof(Segments))
            .Accessor(o => o.Segments, (o, v) => o.Segments = v)
            .Register();

        AffectsRender<PathGeometry>(StartPointProperty, IsClosedProperty);
    }

    public PathGeometry()
    {
        _operations.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    public Point StartPoint
    {
        get => _startPoint;
        set => SetAndRaise(StartPointProperty, ref _startPoint, value);
    }

    public bool IsClosed
    {
        get => _isClosed;
        set => SetAndRaise(IsClosedProperty, ref _isClosed, value);
    }

    [NotAutoSerialized]
    public PathSegments Segments
    {
        get => _operations;
        set => _operations.Replace(value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Segments), Segments);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<PathSegments>(nameof(Segments)) is { } operations)
        {
            Segments = operations;
        }
    }

    public static PathGeometry Parse(string svg)
    {
        using var path = SKPath.ParseSvgPathData(svg);
        using SKPath.RawIterator it = path.CreateRawIterator();

        var result = new PathGeometry();
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPathVerb pathVerb;
        Point? startPoint = null;
        bool? isClosed = null;

        do
        {
            pathVerb = it.Next(points);
            PathSegment? operation;

            if (pathVerb == SKPathVerb.Move)
            {
                if (startPoint.HasValue)
                    throw new InvalidOperationException("PathGeometryは単一の図形のみ対応します");

                startPoint = points[0].ToGraphicsPoint();
            }
            else if (pathVerb == SKPathVerb.Close)
            {
                if (isClosed.HasValue)
                    throw new InvalidOperationException("PathGeometryは単一の図形のみ対応します");

                isClosed = true;
            }

            operation = pathVerb switch
            {
                SKPathVerb.Line => new LineSegment(points[1].ToGraphicsPoint()),
                SKPathVerb.Quad => new QuadraticBezierSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint()),
                SKPathVerb.Conic => new ConicSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), it.ConicWeight()),
                SKPathVerb.Cubic => new CubicBezierSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), points[3].ToGraphicsPoint()),
                SKPathVerb.Done or _ => null,
            };

            if (operation != null)
            {
                result.Segments.Add(operation);
            }

        } while (pathVerb != SKPathVerb.Done);

        if (startPoint.HasValue)
            result.StartPoint = startPoint.Value;
        
        if (isClosed.HasValue)
            result.IsClosed = isClosed.Value;

        return result;
    }

    [Obsolete("Use 'StartPoint'.")]
    public void MoveTo(Point point)
    {
        Segments.Add(new MoveOperation(point));
    }

    public void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point)
    {
        Segments.Add(new ArcSegment()
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
        Segments.Add(new ConicSegment()
        {
            ControlPoint = controlPoint,
            EndPoint = endPoint,
            Weight = weight
        });
    }

    public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        Segments.Add(new CubicBezierSegment()
        {
            ControlPoint1 = controlPoint1,
            ControlPoint2 = controlPoint2,
            EndPoint = endPoint
        });
    }

    public void LineTo(Point point)
    {
        Segments.Add(new LineSegment(point));
    }

    public void QuadraticTo(Point controlPoint, Point endPoint)
    {
        Segments.Add(new QuadraticBezierSegment()
        {
            ControlPoint = controlPoint,
            EndPoint = endPoint
        });
    }

    [Obsolete("Use 'IsClosed'.")]
    public void Close()
    {
        Segments.Add(new CloseOperation());
    }

    public override void ApplyTo(IGeometryContext context)
    {
        base.ApplyTo(context);
        context.MoveTo(StartPoint);

        foreach (PathSegment item in Segments.GetMarshal().Value)
        {
            item.ApplyTo(context);
        }

        if (IsClosed)
            context.Close();
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (PathSegment item in Segments)
        {
            item.ApplyAnimations(clock);
        }
    }
}
