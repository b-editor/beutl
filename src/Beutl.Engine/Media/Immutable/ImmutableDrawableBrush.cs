
using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableDrawableBrush : ImmutableTileBrush, IDrawableBrush, IEquatable<IDrawableBrush?>
{
    public ImmutableDrawableBrush(
        Drawable drawable,
        AlignmentX alignmentX = AlignmentX.Center,
        AlignmentY alignmentY = AlignmentY.Center,
        RelativeRect? destinationRect = null,
        float opacity = 100,
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

    public Drawable? Drawable { get; }

    int? IDrawableBrush.Version => Drawable?.Version;

    public override bool Equals(object? obj)
    {
        return Equals(obj as IDrawableBrush);
    }

    public bool Equals(IDrawableBrush? other)
    {
        return other is not null
            && AlignmentX == other.AlignmentX
            && AlignmentY == other.AlignmentY
            && DestinationRect.Equals(other.DestinationRect)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin)
            && SourceRect.Equals(other.SourceRect)
            && Stretch == other.Stretch
            && TileMode == other.TileMode
            && BitmapInterpolationMode == other.BitmapInterpolationMode
            && ReferenceEquals(Drawable, other.Drawable)
            && Drawable?.Version == other.Version;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AlignmentX);
        hash.Add(AlignmentY);
        hash.Add(DestinationRect);
        hash.Add(Opacity);
        hash.Add(Transform);
        hash.Add(TransformOrigin);
        hash.Add(SourceRect);
        hash.Add(Stretch);
        hash.Add(TileMode);
        hash.Add(BitmapInterpolationMode);
        hash.Add(Drawable);
        return hash.ToHashCode();
    }
}
