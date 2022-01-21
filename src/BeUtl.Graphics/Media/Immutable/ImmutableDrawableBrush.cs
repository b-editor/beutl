
using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public sealed record ImmutableDrawableBrush(
    IDrawable? Drawable,
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
        BitmapInterpolationMode), IDrawableBrush;
