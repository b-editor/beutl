
using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public sealed record ImmutableRadialGradientBrush(
    IReadOnlyList<IGradientStop> GradientStops,
    float Opacity,
    GradientSpreadMethod SpreadMethod,
    RelativePoint Center,
    RelativePoint GradientOrigin,
    float Radius)
    : ImmutableGradientBrush(GradientStops, Opacity, SpreadMethod), IRadialGradientBrush;
