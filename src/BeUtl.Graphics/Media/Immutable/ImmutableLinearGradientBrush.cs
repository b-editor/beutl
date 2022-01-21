
using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public sealed record ImmutableLinearGradientBrush(
    IReadOnlyList<IGradientStop> GradientStops,
    float Opacity,
    GradientSpreadMethod SpreadMethod,
    RelativePoint StartPoint,
    RelativePoint EndPoint)
    : ImmutableGradientBrush(GradientStops, Opacity, SpreadMethod), ILinearGradientBrush;
