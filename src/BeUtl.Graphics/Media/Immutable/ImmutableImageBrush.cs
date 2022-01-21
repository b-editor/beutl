
using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public sealed record ImmutableImageBrush(
    IBitmap? Source,
    AlignmentX AlignmentX,
    AlignmentY AlignmentY,
    RelativeRect DestinationRect,
    float Opacity,
    RelativeRect SourceRect,
    Stretch Stretch,
    TileMode TileMode,
    BitmapInterpolationMode BitmapInterpolationMode)
    : ImmutableTileBrush(
        AlignmentX,
        AlignmentY,
        DestinationRect,
        Opacity,
        SourceRect,
        Stretch,
        TileMode,
        BitmapInterpolationMode), IImageBrush;
