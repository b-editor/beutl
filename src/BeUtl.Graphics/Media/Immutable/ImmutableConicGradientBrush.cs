
using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public sealed record ImmutableConicGradientBrush(
    IReadOnlyList<IGradientStop> GradientStops,
    float Opacity,
    GradientSpreadMethod SpreadMethod,
    RelativePoint Center,
    float Angle)
    : ImmutableGradientBrush(GradientStops, Opacity, SpreadMethod), IConicGradientBrush;
