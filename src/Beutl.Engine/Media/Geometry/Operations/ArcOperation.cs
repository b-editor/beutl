using Beutl.Graphics;

namespace Beutl.Media;

public sealed class ArcOperation : PathOperation
{
    public static readonly CoreProperty<Size> RadiusProperty;
    public static readonly CoreProperty<float> RotationAngleProperty;
    public static readonly CoreProperty<bool> IsLargeArcProperty;
    public static readonly CoreProperty<bool> SweepClockwiseProperty;
    public static readonly CoreProperty<Point> PointProperty;
    private Size _radius;
    private float _rotationAngle;
    private bool _isLargeArc;
    private bool _sweepClockwise = true;
    private Point _point;

    static ArcOperation()
    {
        RadiusProperty = ConfigureProperty<Size, ArcOperation>(nameof(Radius))
            .Accessor(o => o.Radius, (o, v) => o.Radius = v)
            .Register();

        RotationAngleProperty = ConfigureProperty<float, ArcOperation>(nameof(RotationAngle))
            .Accessor(o => o.RotationAngle, (o, v) => o.RotationAngle = v)
            .Register();

        IsLargeArcProperty = ConfigureProperty<bool, ArcOperation>(nameof(IsLargeArc))
            .Accessor(o => o.IsLargeArc, (o, v) => o.IsLargeArc = v)
            .Register();

        SweepClockwiseProperty = ConfigureProperty<bool, ArcOperation>(nameof(SweepClockwise))
            .Accessor(o => o.SweepClockwise, (o, v) => o.SweepClockwise = v)
            .DefaultValue(true)
            .Register();

        PointProperty = ConfigureProperty<Point, ArcOperation>(nameof(Point))
            .Accessor(o => o.Point, (o, v) => o.Point = v)
            .Register();

        AffectsRender<ArcOperation>(RadiusProperty, RotationAngleProperty, IsLargeArcProperty, SweepClockwiseProperty, PointProperty);
    }

    public Size Radius
    {
        get => _radius;
        set => SetAndRaise(RadiusProperty, ref _radius, value);
    }

    public float RotationAngle
    {
        get => _rotationAngle;
        set => SetAndRaise(RotationAngleProperty, ref _rotationAngle, value);
    }

    public bool IsLargeArc
    {
        get => _isLargeArc;
        set => SetAndRaise(IsLargeArcProperty, ref _isLargeArc, value);
    }

    public bool SweepClockwise
    {
        get => _sweepClockwise;
        set => SetAndRaise(SweepClockwiseProperty, ref _sweepClockwise, value);
    }

    public Point Point
    {
        get => _point;
        set => SetAndRaise(PointProperty, ref _point, value);
    }

    public override void ApplyTo(IGeometryContext context)
    {
        context.ArcTo(Radius, RotationAngle, IsLargeArc, SweepClockwise, Point);
    }
}
