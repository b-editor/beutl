using Beutl.Graphics;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a radial gradient.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush, IRadialGradientBrush
{
    public static readonly CoreProperty<RelativePoint> CenterProperty;
    public static readonly CoreProperty<RelativePoint> GradientOriginProperty;
    public static readonly CoreProperty<float> RadiusProperty;
    private RelativePoint _center = RelativePoint.Center;
    private RelativePoint _gradientOrigin = RelativePoint.Center;
    private float _radius = 0.5f;

    static RadialGradientBrush()
    {
        CenterProperty = ConfigureProperty<RelativePoint, RadialGradientBrush>(nameof(Center))
            .Accessor(o => o.Center, (o, v) => o.Center = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(RelativePoint.Center)
            .SerializeName("center")
            .Register();

        GradientOriginProperty = ConfigureProperty<RelativePoint, RadialGradientBrush>(nameof(GradientOrigin))
            .Accessor(o => o.GradientOrigin, (o, v) => o.GradientOrigin = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(RelativePoint.Center)
            .SerializeName("gradient-origin")
            .Register();

        RadiusProperty = ConfigureProperty<float, RadialGradientBrush>(nameof(Radius))
            .Accessor(o => o.Radius, (o, v) => o.Radius = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(0.5f)
            .SerializeName("radius")
            .Register();

        AffectsRender<RadialGradientBrush>(CenterProperty, GradientOriginProperty, RadiusProperty);
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    public RelativePoint Center
    {
        get => _center;
        set => SetAndRaise(CenterProperty, ref _center, value);
    }

    /// <summary>
    /// Gets or sets the location of the two-dimensional focal point that defines the beginning
    /// of the gradient.
    /// </summary>
    public RelativePoint GradientOrigin
    {
        get => _gradientOrigin;
        set => SetAndRaise(GradientOriginProperty, ref _gradientOrigin, value);
    }

    /// <summary>
    /// Gets or sets the horizontal and vertical radius of the outermost circle of the radial
    /// gradient.
    /// </summary>
    public float Radius
    {
        get => _radius;
        set => SetAndRaise(RadiusProperty, ref _radius, value);
    }

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableRadialGradientBrush(this);
    }
}
