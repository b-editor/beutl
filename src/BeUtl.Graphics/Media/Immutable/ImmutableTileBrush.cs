
using BeUtl.Graphics;

namespace BeUtl.Media.Immutable;

public abstract record ImmutableTileBrush(
    AlignmentX AlignmentX,
    AlignmentY AlignmentY,
    RelativeRect DestinationRect,
    float Opacity,
    RelativeRect SourceRect,
    Stretch Stretch,
    TileMode TileMode,
    BitmapInterpolationMode BitmapInterpolationMode) : ITileBrush;
