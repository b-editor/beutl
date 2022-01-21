namespace BeUtl.Media.Immutable;

public abstract record ImmutableGradientBrush(
    IReadOnlyList<IGradientStop> GradientStops,
    float Opacity,
    GradientSpreadMethod SpreadMethod) : IGradientBrush;
