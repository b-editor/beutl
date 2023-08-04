using Beutl.Graphics.Transformation;
using Beutl.Media.Immutable;
using Beutl.Media.Source;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="IBitmap"/>.
/// </summary>
public class ImageBrush : TileBrush, IImageBrush, IEquatable<IImageBrush?>
{
    public static readonly CoreProperty<IImageSource?> SourceProperty;
    private IImageSource? _source;

    static ImageBrush()
    {
        SourceProperty = ConfigureProperty<IImageSource?, ImageBrush>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .Register();

        AffectsRender<ImageBrush>(SourceProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    public ImageBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="source">The image to draw.</param>
    public ImageBrush(IImageSource source)
    {
        Source = source;
    }

    /// <summary>
    /// Gets or sets the image to draw.
    /// </summary>
    public IImageSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableImageBrush(this);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as IImageBrush);
    }

    public bool Equals(IImageBrush? other)
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
            && EqualityComparer<IImageSource?>.Default.Equals(Source, other.Source);
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
        hash.Add(Source);
        return hash.ToHashCode();
    }
}
