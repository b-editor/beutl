using Beutl.Graphics;

namespace Beutl.Media;

public sealed class MoveOperation : PathOperation
{
    public static readonly CoreProperty<Point> PointProperty;
    private Point _point;

    static MoveOperation()
    {
        PointProperty = ConfigureProperty<Point, MoveOperation>(nameof(Point))
            .Accessor(o => o.Point, (o, v) => o.Point = v)
            .Register();

        AffectsRender<MoveOperation>(PointProperty);
    }

    public MoveOperation()
    {
    }
    
    public MoveOperation(Point point)
    {
        Point = point;
    }

    public MoveOperation(float x, float y)
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
        context.MoveTo(Point);
    }
}
