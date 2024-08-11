
using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableLinearGradientBrush : ImmutableGradientBrush, ILinearGradientBrush, IEquatable<ILinearGradientBrush?>
{
    public ImmutableLinearGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity = 100,
        ImmutableTransform? transform = null,
        RelativePoint? transformOrigin = null,
        GradientSpreadMethod spreadMethod = GradientSpreadMethod.Pad,
        RelativePoint? startPoint = null,
        RelativePoint? endPoint = null)
        : base(gradientStops, opacity, transform, transformOrigin, spreadMethod)
    {
        StartPoint = startPoint ?? RelativePoint.TopLeft;
        EndPoint = endPoint ?? RelativePoint.BottomRight;
    }

    public ImmutableLinearGradientBrush(LinearGradientBrush source)
        : base(source)
    {
        StartPoint = source.StartPoint;
        EndPoint = source.EndPoint;
    }

    public RelativePoint StartPoint { get; }

    public RelativePoint EndPoint { get; }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ILinearGradientBrush);
    }

    public bool Equals(ILinearGradientBrush? other)
    {
        return other is not null
            && GradientStops.SequenceEqual(other.GradientStops)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin)
            && SpreadMethod == other.SpreadMethod
            && StartPoint.Equals(other.StartPoint)
            && EndPoint.Equals(other.EndPoint);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GradientStops, Opacity, Transform, TransformOrigin, SpreadMethod, StartPoint, EndPoint);
    }
}
