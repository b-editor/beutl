using Beutl.Graphics;

namespace Beutl.Media;

public sealed class ConicOperation : PathOperation
{
    public static readonly CoreProperty<Point> ControlPointProperty;
    public static readonly CoreProperty<Point> EndPointProperty;
    public static readonly CoreProperty<float> WeightProperty;
    private Point _controlPoint;
    private Point _endPoint;
    private float _weight;

    static ConicOperation()
    {
        ControlPointProperty = ConfigureProperty<Point, ConicOperation>(nameof(ControlPoint))
            .Accessor(o => o.ControlPoint, (o, v) => o.ControlPoint = v)
            .Register();

        EndPointProperty = ConfigureProperty<Point, ConicOperation>(nameof(EndPoint))
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .Register();

        WeightProperty = ConfigureProperty<float, ConicOperation>(nameof(Weight))
            .Accessor(o => o.Weight, (o, v) => o.Weight = v)
            .Register();

        AffectsRender<ConicOperation>(ControlPointProperty, EndPointProperty, WeightProperty);
    }

    public ConicOperation()
    {
    }

    public ConicOperation(Point controlPoint, Point endPoint, float weight)
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
}
