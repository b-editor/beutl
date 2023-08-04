using System.ComponentModel.DataAnnotations;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a swept circular gradient.
/// </summary>
public sealed class ConicGradientBrush : GradientBrush, IConicGradientBrush, IEquatable<IConicGradientBrush?>
{
    public static readonly CoreProperty<RelativePoint> CenterProperty;
    public static readonly CoreProperty<float> AngleProperty;
    private RelativePoint _center = RelativePoint.Center;
    private float _angle;

    static ConicGradientBrush()
    {
        CenterProperty = ConfigureProperty<RelativePoint, ConicGradientBrush>(nameof(Center))
            .Accessor(o => o.Center, (o, v) => o.Center = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        AngleProperty = ConfigureProperty<float, ConicGradientBrush>(nameof(Angle))
            .Accessor(o => o.Angle, (o, v) => o.Angle = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<ConicGradientBrush>(CenterProperty, AngleProperty);
    }

    /// <summary>
    /// Gets or sets the center point of the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Center), ResourceType = typeof(Strings))]
    public RelativePoint Center
    {
        get => _center;
        set => SetAndRaise(CenterProperty, ref _center, value);
    }

    /// <summary>
    /// Gets or sets the angle of the start and end of the sweep, measured from above the center point.
    /// </summary>
    [Display(Name = nameof(Strings.Angle), ResourceType = typeof(Strings))]
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

    public override bool Equals(object? obj)
    {
        return Equals(obj as IConicGradientBrush);
    }

    public bool Equals(IConicGradientBrush? other)
    {
        return other is not null
            && GradientStops.SequenceEqual(other.GradientStops)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin)
            && SpreadMethod == other.SpreadMethod
            && Center.Equals(other.Center)
            && Angle == other.Angle;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GradientStops, Opacity, Transform, TransformOrigin, SpreadMethod, Center, Angle);
    }
}
