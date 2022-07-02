using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public class ImmutableConicGradientBrush : ImmutableGradientBrush, IConicGradientBrush
{
    public ImmutableConicGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity = 1,
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

    public ImmutableConicGradientBrush(ConicGradientBrush source)
        : base(source)
    {
        Center = source.Center;
        Angle = source.Angle;
    }

    public RelativePoint Center { get; }

    public float Angle { get; }
}
