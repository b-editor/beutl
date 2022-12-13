
using Beutl.Graphics;
using Beutl.Media.Source;

namespace Beutl.Media.Immutable;

public class ImmutableImageBrush : ImmutableTileBrush, IImageBrush
{
    public ImmutableImageBrush(
        IImageSource source,
        AlignmentX alignmentX = AlignmentX.Center,
        AlignmentY alignmentY = AlignmentY.Center,
        RelativeRect? destinationRect = null,
        float opacity = 1,
        ImmutableTransform? transform = null,
        RelativePoint transformOrigin = new RelativePoint(),
        RelativeRect? sourceRect = null,
        Stretch stretch = Stretch.Uniform,
        TileMode tileMode = TileMode.None,
        BitmapInterpolationMode bitmapInterpolationMode = BitmapInterpolationMode.Default)
        : base(
              alignmentX,
              alignmentY,
              destinationRect ?? RelativeRect.Fill,
              opacity,
              transform,
              transformOrigin,
              sourceRect ?? RelativeRect.Fill,
              stretch,
              tileMode,
              bitmapInterpolationMode)
    {
        Source = source;
    }

    public ImmutableImageBrush(IImageBrush source)
        : base(source)
    {
        Source = source.Source;
    }

    public IImageSource? Source { get; }
}
