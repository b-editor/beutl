using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableConicGradientBrush : ImmutableGradientBrush, IConicGradientBrush, IEquatable<IConicGradientBrush?>
{
    public ImmutableConicGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity = 100,
        ImmutableTransform? transform = null,
        RelativePoint? transformOrigin = null,
        GradientSpreadMethod spreadMethod = GradientSpreadMethod.Pad,
        RelativePoint? center = null,
        float angle = 0)
        : base(gradientStops, opacity, transform, transformOrigin, spreadMethod)
    {
        Center = center ?? RelativePoint.Center;
        Angle = angle;
    }

    public ImmutableConicGradientBrush(IConicGradientBrush source)
        : base(source)
    {
        Center = source.Center;
        Angle = source.Angle;
    }

    public RelativePoint Center { get; }

    public float Angle { get; }

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
