using Beutl.Graphics;

namespace Beutl.Media;

public sealed class QuadraticBezierOperation : PathOperation
{
    public static readonly CoreProperty<Point> ControlPointProperty;
    public static readonly CoreProperty<Point> EndPointProperty;
    private Point _controlPoint;
    private Point _endPoint;

    static QuadraticBezierOperation()
    {
        ControlPointProperty = ConfigureProperty<Point, QuadraticBezierOperation>(nameof(ControlPoint))
            .Accessor(o => o.ControlPoint, (o, v) => o.ControlPoint = v)
            .Register();

        EndPointProperty = ConfigureProperty<Point, QuadraticBezierOperation>(nameof(EndPoint))
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .Register();

        AffectsRender<QuadraticBezierOperation>(ControlPointProperty, EndPointProperty);
    }

    public QuadraticBezierOperation()
    {
    }
    
    public QuadraticBezierOperation(Point controlPoint, Point endPoint)
    {
        ControlPoint = controlPoint;
        EndPoint = endPoint;
    }

    public Point ControlPoint
    {
        get => _controlPoint;
        set => SetAndRaise(ControlPointProperty, ref _controlPoint, value);
    }

    public Point EndPoint
    {
        get => _endPoint;
        set => SetAndRaise(EndPointProperty, ref _endPoint, value);
    }

    public override void ApplyTo(IGeometryContext context)
    {
        context.QuadraticTo(ControlPoint, EndPoint);
    }
}
