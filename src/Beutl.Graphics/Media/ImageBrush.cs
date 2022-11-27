using Beutl.Media.Immutable;
using Beutl.Media.Source;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="IBitmap"/>.
/// </summary>
public class ImageBrush : TileBrush, IImageBrush
{
    public static readonly CoreProperty<IImageSource?> SourceProperty;
    private IImageSource? _source;

    static ImageBrush()
    {
        SourceProperty = ConfigureProperty<IImageSource?, ImageBrush>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .PropertyFlags(PropertyFlags.All)
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
}
