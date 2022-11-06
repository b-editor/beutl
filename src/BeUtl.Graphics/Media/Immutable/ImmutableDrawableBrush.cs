
using Beutl.Graphics;

namespace Beutl.Media.Immutable;

public class ImmutableDrawableBrush : ImmutableTileBrush, IDrawableBrush
{
    public ImmutableDrawableBrush(
        Drawable drawable,
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
        Drawable = drawable;
    }

    public ImmutableDrawableBrush(IDrawableBrush source)
        : base(source)
    {
        Drawable = source.Drawable;
    }

    public IDrawable? Drawable { get; }
}
