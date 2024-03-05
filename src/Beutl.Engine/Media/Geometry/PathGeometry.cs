using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Serialization;

using SkiaSharp;

namespace Beutl.Media;

public sealed class PathGeometry : Geometry
{
    public static readonly CoreProperty<PathOperations> OperationsProperty;
    private readonly PathOperations _operations = [];

    static PathGeometry()
    {
        OperationsProperty = ConfigureProperty<PathOperations, PathGeometry>(nameof(Operations))
            .Accessor(o => o.Operations, (o, v) => o.Operations = v)
            .Register();
    }

    public PathGeometry()
    {
        _operations.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public PathOperations Operations
    {
        get => _operations;
        set => _operations.Replace(value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Operations), Operations);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<PathOperations>(nameof(Operations)) is { } operations)
        {
            Operations = operations;
        }
    }

    public static PathGeometry Parse(string svg)
    {
        using var path = SKPath.ParseSvgPathData(svg);
        using SKPath.RawIterator it = path.CreateRawIterator();

        var result = new PathGeometry();
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPathVerb pathVerb;

        do
        {
            pathVerb = it.Next(points);
            PathOperation? operation;

            operation = pathVerb switch
            {
                SKPathVerb.Move => new MoveOperation(points[0].ToGraphicsPoint()),
                SKPathVerb.Line => new LineOperation(points[1].ToGraphicsPoint()),
                SKPathVerb.Quad => new QuadraticBezierOperation(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint()),
                SKPathVerb.Conic => new ConicOperation(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), it.ConicWeight()),
                SKPathVerb.Cubic => new CubicBezierOperation(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), points[3].ToGraphicsPoint()),
                SKPathVerb.Close => new CloseOperation(),
                SKPathVerb.Done or _ => null,
            };

            if (operation != null)
            {
                result.Operations.Add(operation);
            }

        } while (pathVerb != SKPathVerb.Done);

        return result;
    }

    public void MoveTo(Point point)
    {
        Operations.Add(new MoveOperation(point));
    }

    public void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point)
    {
        Operations.Add(new ArcOperation()
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
        Operations.Add(new ConicOperation()
        {
            ControlPoint = controlPoint,
            EndPoint = endPoint,
            Weight = weight
        });
    }

    public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        Operations.Add(new CubicBezierOperation()
        {
            ControlPoint1 = controlPoint1,
            ControlPoint2 = controlPoint2,
            EndPoint = endPoint
        });
    }

    public void LineTo(Point point)
    {
        Operations.Add(new LineOperation(point));
    }

    public void QuadraticTo(Point controlPoint, Point endPoint)
    {
        Operations.Add(new QuadraticBezierOperation()
        {
            ControlPoint = controlPoint,
            EndPoint = endPoint
        });
    }

    public void Close()
    {
        Operations.Add(new CloseOperation());
    }

    public override void ApplyTo(IGeometryContext context)
    {
        base.ApplyTo(context);

        foreach (PathOperation item in Operations.GetMarshal().Value)
        {
            item.ApplyTo(context);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (PathOperation item in Operations)
        {
            item.ApplyAnimations(clock);
        }
    }
}
