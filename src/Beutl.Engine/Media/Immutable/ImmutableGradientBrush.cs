using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public abstract class ImmutableGradientBrush : IGradientBrush
{
    protected ImmutableGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity,
        ITransform? transform,
        RelativePoint? transformOrigin,
        GradientSpreadMethod spreadMethod)
    {
        GradientStops = gradientStops;
        Opacity = opacity;
        Transform = (transform as IMutableTransform)?.ToImmutable() ?? transform;
        TransformOrigin = transformOrigin ?? RelativePoint.TopLeft;
        SpreadMethod = spreadMethod;
    }

    protected ImmutableGradientBrush(IGradientBrush source)
        : this((IReadOnlyList<ImmutableGradientStop>)((source.GradientStops as GradientStops)?.ToImmutable() ?? source.GradientStops),
              source.Opacity,
              source.Transform,
              source.TransformOrigin,
              source.SpreadMethod)
    {

    }

    public IReadOnlyList<IGradientStop> GradientStops { get; }

    public float Opacity { get; }

    public ITransform? Transform { get; }

    public RelativePoint TransformOrigin { get; }

    public GradientSpreadMethod SpreadMethod { get; }
}
