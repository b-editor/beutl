using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public abstract class ImmutableTileBrush : ITileBrush
{
    protected ImmutableTileBrush(
        AlignmentX alignmentX,
        AlignmentY alignmentY,
        RelativeRect destinationRect,
        float opacity,
        ITransform? transform,
        RelativePoint transformOrigin,
        RelativeRect sourceRect,
        Stretch stretch,
        TileMode tileMode,
        BitmapInterpolationMode bitmapInterpolationMode)
    {
        AlignmentX = alignmentX;
        AlignmentY = alignmentY;
        DestinationRect = destinationRect;
        Opacity = opacity;
        Transform = (transform as IMutableTransform)?.ToImmutable() ?? transform;
        TransformOrigin = transformOrigin;
        SourceRect = sourceRect;
        Stretch = stretch;
        TileMode = tileMode;
        BitmapInterpolationMode = bitmapInterpolationMode;
    }

    protected ImmutableTileBrush(ITileBrush source)
        : this(
              source.AlignmentX,
              source.AlignmentY,
              source.DestinationRect,
              source.Opacity,
              source.Transform,
              source.TransformOrigin,
              source.SourceRect,
              source.Stretch,
              source.TileMode,
              source.BitmapInterpolationMode)
    {
    }

    public AlignmentX AlignmentX { get; }

    public AlignmentY AlignmentY { get; }

    public RelativeRect DestinationRect { get; }

    public float Opacity { get; }

    public ITransform? Transform { get; }

    public RelativePoint TransformOrigin { get; }

    public RelativeRect SourceRect { get; }

    public Stretch Stretch { get; }

    public TileMode TileMode { get; }

    public BitmapInterpolationMode BitmapInterpolationMode { get; }
}
