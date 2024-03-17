using Beutl.Graphics;

namespace Beutl.Media;

public sealed class ConicSegment : PathSegment
{
    public static readonly CoreProperty<Point> ControlPointProperty;
    public static readonly CoreProperty<Point> EndPointProperty;
    public static readonly CoreProperty<float> WeightProperty;
    private Point _controlPoint;
    private Point _endPoint;
    private float _weight;

    static ConicSegment()
    {
        ControlPointProperty = ConfigureProperty<Point, ConicSegment>(nameof(ControlPoint))
            .Accessor(o => o.ControlPoint, (o, v) => o.ControlPoint = v)
            .Register();

        EndPointProperty = ConfigureProperty<Point, ConicSegment>(nameof(EndPoint))
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .Register();

        WeightProperty = ConfigureProperty<float, ConicSegment>(nameof(Weight))
            .Accessor(o => o.Weight, (o, v) => o.Weight = v)
            .Register();

        AffectsRender<ConicSegment>(ControlPointProperty, EndPointProperty, WeightProperty);
    }

    public ConicSegment()
    {
    }

    public ConicSegment(Point controlPoint, Point endPoint, float weight)
    {
        ControlPoint = controlPoint;
        EndPoint = endPoint;
        Weight = weight;
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
    
    public float Weight
    {
        get => _weight;
        set => SetAndRaise(WeightProperty, ref _weight, value);
    }

    public override void ApplyTo(IGeometryContext context)
    {
        context.ConicTo(ControlPoint, EndPoint, Weight);
    }

    public override CoreProperty<Point> GetEndPointProperty() => EndPointProperty;
}
