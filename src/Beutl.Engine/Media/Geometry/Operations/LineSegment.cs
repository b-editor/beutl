using Beutl.Graphics;

namespace Beutl.Media;

public sealed class LineSegment : PathSegment
{
    public static readonly CoreProperty<Point> PointProperty;
    private Point _point;

    static LineSegment()
    {
        PointProperty = ConfigureProperty<Point, LineSegment>(nameof(Point))
            .Accessor(o => o.Point, (o, v) => o.Point = v)
            .Register();

        AffectsRender<LineSegment>(PointProperty);
    }

    public LineSegment()
    {
    }
    
    public LineSegment(Point point)
    {
        Point = point;
    }

    public LineSegment(float x, float y)
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

    public override CoreProperty<Point> GetEndPointProperty() => PointProperty;
}
