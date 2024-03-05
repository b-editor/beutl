using Beutl.Graphics;

namespace Beutl.Media;

public sealed class LineOperation : PathOperation
{
    public static readonly CoreProperty<Point> PointProperty;
    private Point _point;

    static LineOperation()
    {
        PointProperty = ConfigureProperty<Point, LineOperation>(nameof(Point))
            .Accessor(o => o.Point, (o, v) => o.Point = v)
            .Register();

        AffectsRender<LineOperation>(PointProperty);
    }

    public LineOperation()
    {
    }
    
    public LineOperation(Point point)
    {
        Point = point;
    }

    public LineOperation(float x, float y)
        : this(new Point(x, y))
    {
    }

    public Point Point
    {
        get => _point;
        set => SetAndRaise(PointProperty, ref _point, value);
    }

    public override void ApplyTo(IGeometryContext context)
    {
        context.LineTo(Point);
    }
}
