using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public abstract class ImmutableGradientBrush : IGradientBrush
{
    protected ImmutableGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity,
        ImmutableTransform? transform,
        RelativePoint? transformOrigin,
        GradientSpreadMethod spreadMethod)
    {
        GradientStops = gradientStops;
        Opacity = opacity;
        Transform = transform;
        TransformOrigin = transformOrigin ?? RelativePoint.TopLeft;
        SpreadMethod = spreadMethod;
    }

    protected ImmutableGradientBrush(GradientBrush source)
        : this(source.GradientStops.ToImmutable(), source.Opacity, source.Transform?.ToImmutable(),
              source.TransformOrigin, source.SpreadMethod)
    {

    }

    public IReadOnlyList<IGradientStop> GradientStops { get; }

    public float Opacity { get; }

    public ITransform? Transform { get; }

    public RelativePoint TransformOrigin { get; }

    public GradientSpreadMethod SpreadMethod { get; }
}
