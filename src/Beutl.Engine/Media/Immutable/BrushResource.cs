using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Media.Source;

namespace Beutl.Media.Immutable;

public record BrushResource(
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default);

public record SolidColorBrushResource(
    Color Color,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default)
    : BrushResource(Opacity, Transform, Origin);

public record GradientStopResource(float Offset, Color Color);

public record GradientBrushResource(
    ImmutableArray<GradientStopResource> GradientStops,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default,
    GradientSpreadMethod SpreadMethod = GradientSpreadMethod.Pad)
    : BrushResource(Opacity, Transform, Origin);

public record LinearGradientBrushResource(
    RelativePoint StartPoint,
    RelativePoint EndPoint,
    ImmutableArray<GradientStopResource> GradientStops,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default,
    GradientSpreadMethod SpreadMethod = GradientSpreadMethod.Pad)
    : GradientBrushResource(GradientStops, Opacity, Transform, Origin, SpreadMethod);

public record RadialGradientBrushResource(
    RelativePoint Center,
    RelativePoint GradientOrigin,
    float Radius,
    ImmutableArray<GradientStopResource> GradientStops,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default,
    GradientSpreadMethod SpreadMethod = GradientSpreadMethod.Pad)
    : GradientBrushResource(GradientStops, Opacity, Transform, Origin, SpreadMethod);

public record ConicGradientBrushResource(
    RelativePoint  Center,
    float Angle,
    ImmutableArray<GradientStopResource> GradientStops,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default,
    GradientSpreadMethod SpreadMethod = GradientSpreadMethod.Pad)
    : GradientBrushResource(GradientStops, Opacity, Transform, Origin, SpreadMethod);

public record TileBrushResource(
    AlignmentX AlignmentX = AlignmentX.Center,
    AlignmentY AlignmentY = AlignmentY.Center,
    RelativeRect DestinationRect = default,
    RelativeRect SourceRect = default,
    Stretch Stretch = Stretch.Uniform,
    TileMode TileMode = TileMode.None,
    BitmapInterpolationMode BitmapInterpolationMode = BitmapInterpolationMode.Default,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default)
    : BrushResource(Opacity, Transform, Origin);

public record ImageBrushResource(
    IImageSource ImageSource,
    AlignmentX AlignmentX = AlignmentX.Center,
    AlignmentY AlignmentY = AlignmentY.Center,
    RelativeRect DestinationRect = default,
    RelativeRect SourceRect = default,
    Stretch Stretch = Stretch.Uniform,
    TileMode TileMode = TileMode.None,
    BitmapInterpolationMode BitmapInterpolationMode = BitmapInterpolationMode.Default,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default)
    : TileBrushResource(AlignmentX, AlignmentY, DestinationRect, SourceRect, Stretch, TileMode, BitmapInterpolationMode, Opacity, Transform, Origin);

public record PerlinNoiseBrushResource(
    float BaseFrequencyX = 0.1f,
    float BaseFrequencyY = 0.1f,
    int Octaves = 1,
    float Seed = 0,
    PerlinNoiseType PerlinNoiseType = PerlinNoiseType.Turbulence,
    float Opacity = 100,
    Matrix? Transform = null,
    RelativePoint Origin = default)
    : BrushResource(Opacity, Transform, Origin);

public record PenResource(
    BrushResource? Brush,
    ImmutableArray<float>? DashArray,
    float DashOffset,
    float Thickness,
    float MiterLimit = 10,
    StrokeCap StrokeCap = StrokeCap.Flat,
    StrokeJoin StrokeJoin = StrokeJoin.Miter,
    StrokeAlignment StrokeAlignment = StrokeAlignment.Center);
