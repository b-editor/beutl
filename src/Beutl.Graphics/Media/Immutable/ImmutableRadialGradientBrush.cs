using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableRadialGradientBrush : ImmutableGradientBrush, IRadialGradientBrush, IEquatable<IRadialGradientBrush?>
{
    public ImmutableRadialGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity = 1,
        ImmutableTransform? transform = null,
        RelativePoint? transformOrigin = null,
        GradientSpreadMethod spreadMethod = GradientSpreadMethod.Pad,
        RelativePoint? center = null,
        RelativePoint? gradientOrigin = null,
        float radius = 0.5f)
        : base(gradientStops, opacity, transform, transformOrigin, spreadMethod)
    {
        Center = center ?? RelativePoint.Center;
        GradientOrigin = gradientOrigin ?? RelativePoint.Center;
        Radius = radius;
    }

    public ImmutableRadialGradientBrush(RadialGradientBrush source)
        : base(source)
    {
        Center = source.Center;
        GradientOrigin = source.GradientOrigin;
        Radius = source.Radius;
    }

    public RelativePoint Center { get; }

    public RelativePoint GradientOrigin { get; }

    public float Radius { get; }

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
