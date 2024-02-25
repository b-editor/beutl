using System.ComponentModel.DataAnnotations;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a radial gradient.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush, IRadialGradientBrush,IEquatable<IRadialGradientBrush?>
{
    public static readonly CoreProperty<RelativePoint> CenterProperty;
    public static readonly CoreProperty<RelativePoint> GradientOriginProperty;
    public static readonly CoreProperty<float> RadiusProperty;
    private RelativePoint _center = RelativePoint.Center;
    private RelativePoint _gradientOrigin = RelativePoint.Center;
    private float _radius = 50;

    static RadialGradientBrush()
    {
        CenterProperty = ConfigureProperty<RelativePoint, RadialGradientBrush>(nameof(Center))
            .Accessor(o => o.Center, (o, v) => o.Center = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        GradientOriginProperty = ConfigureProperty<RelativePoint, RadialGradientBrush>(nameof(GradientOrigin))
            .Accessor(o => o.GradientOrigin, (o, v) => o.GradientOrigin = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        RadiusProperty = ConfigureProperty<float, RadialGradientBrush>(nameof(Radius))
            .Accessor(o => o.Radius, (o, v) => o.Radius = v)
            .DefaultValue(50)
            .Register();

        AffectsRender<RadialGradientBrush>(CenterProperty, GradientOriginProperty, RadiusProperty);
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Center), ResourceType = typeof(Strings))]
    public RelativePoint Center
    {
        get => _center;
        set => SetAndRaise(CenterProperty, ref _center, value);
    }

    /// <summary>
    /// Gets or sets the location of the two-dimensional focal point that defines the beginning
    /// of the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.GradientOrigin), ResourceType = typeof(Strings))]
    public RelativePoint GradientOrigin
    {
        get => _gradientOrigin;
        set => SetAndRaise(GradientOriginProperty, ref _gradientOrigin, value);
    }

    /// <summary>
    /// Gets or sets the horizontal and vertical radius of the outermost circle of the radial
    /// gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Radius), ResourceType = typeof(Strings))]
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

    public override bool Equals(object? obj)
    {
        return Equals(obj as IRadialGradientBrush);
    }

    public bool Equals(IRadialGradientBrush? other)
    {
        return other is not null
            && GradientStops.SequenceEqual(other.GradientStops)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin)
            && SpreadMethod == other.SpreadMethod
            && Center.Equals(other.Center)
            && GradientOrigin.Equals(other.GradientOrigin)
            && Radius == other.Radius;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GradientStops, Opacity, Transform, TransformOrigin, SpreadMethod, Center, GradientOrigin, Radius);
    }
}
