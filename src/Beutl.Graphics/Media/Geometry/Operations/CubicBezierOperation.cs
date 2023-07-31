using Beutl.Graphics;

namespace Beutl.Media;

public sealed class CubicBezierOperation : PathOperation
{
    public static readonly CoreProperty<Point> ControlPoint1Property;
    public static readonly CoreProperty<Point> ControlPoint2Property;
    public static readonly CoreProperty<Point> EndPointProperty;
    private Point _controlPoint1;
    private Point _controlPoint2;
    private Point _endPoint;

    static CubicBezierOperation()
    {
        ControlPoint1Property = ConfigureProperty<Point, CubicBezierOperation>(nameof(ControlPoint1))
            .Accessor(o => o.ControlPoint1, (o, v) => o.ControlPoint1 = v)
            .Register();

        ControlPoint2Property = ConfigureProperty<Point, CubicBezierOperation>(nameof(ControlPoint2))
            .Accessor(o => o.ControlPoint2, (o, v) => o.ControlPoint2 = v)
            .Register();

        EndPointProperty = ConfigureProperty<Point, CubicBezierOperation>(nameof(EndPoint))
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .Register();

        AffectsRender<CubicBezierOperation>(ControlPoint1Property, ControlPoint2Property, EndPointProperty);
    }

    public CubicBezierOperation()
    {
    }

    public CubicBezierOperation(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        ControlPoint1 = controlPoint1;
        ControlPoint2 = controlPoint2;
        EndPoint = endPoint;
    }

    public Point ControlPoint1
    {
        get => _controlPoint1;
        set => SetAndRaise(ControlPoint1Property, ref _controlPoint1, value);
    }

    public Point ControlPoint2
    {
        get => _controlPoint2;
        set => SetAndRaise(ControlPoint2Property, ref _controlPoint2, value);
    }
    
    public Point EndPoint
    {
        get => _endPoint;
        set => SetAndRaise(EndPointProperty, ref _endPoint, value);
    }

    public override void ApplyTo(IGeometryContext context)
    {
        context.CubicTo(ControlPoint1, ControlPoint2,EndPoint);
    }
}
