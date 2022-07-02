using BeUtl.Graphics;
using BeUtl.Media.Immutable;

namespace BeUtl.Media;

/// <summary>
/// Paints an area with a swept circular gradient.
/// </summary>
public sealed class ConicGradientBrush : GradientBrush, IConicGradientBrush
{
    public static readonly CoreProperty<RelativePoint> CenterProperty;
    public static readonly CoreProperty<float> AngleProperty;
    private RelativePoint _center = RelativePoint.Center;
    private float _angle;

    static ConicGradientBrush()
    {
        CenterProperty = ConfigureProperty<RelativePoint, ConicGradientBrush>(nameof(Center))
            .Accessor(o => o.Center, (o, v) => o.Center = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(RelativePoint.Center)
            .SerializeName("center")
            .Register();

        AngleProperty = ConfigureProperty<float, ConicGradientBrush>(nameof(Angle))
            .Accessor(o => o.Angle, (o, v) => o.Angle = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(0)
            .SerializeName("angle")
            .Register();

        AffectsRender<ConicGradientBrush>(CenterProperty, AngleProperty);
    }

    /// <summary>
    /// Gets or sets the center point of the gradient.
    /// </summary>
    public RelativePoint Center
    {
        get => _center;
        set => SetAndRaise(CenterProperty, ref _center, value);
    }

    /// <summary>
    /// Gets or sets the angle of the start and end of the sweep, measured from above the center point.
    /// </summary>
    public float Angle
    {
        get => _angle;
        set => SetAndRaise(AngleProperty, ref _angle, value);
    }

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableConicGradientBrush(this);
    }
}
