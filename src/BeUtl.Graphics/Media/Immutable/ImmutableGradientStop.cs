namespace BeUtl.Media.Immutable;

public sealed record ImmutableGradientStop(float Offset, Color Color) : IGradientStop;
