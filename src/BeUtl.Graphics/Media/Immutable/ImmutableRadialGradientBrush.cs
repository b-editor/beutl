using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public class ImmutableRadialGradientBrush : ImmutableGradientBrush, IRadialGradientBrush
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
}
