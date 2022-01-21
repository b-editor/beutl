namespace BeUtl.Media;

/// <summary>
/// Paints an area with an <see cref="IBitmap"/>.
/// </summary>
public class ImageBrush : TileBrush, IImageBrush
{
    public static readonly CoreProperty<IBitmap?> SourceProperty;
    private IBitmap? _source;

    static ImageBrush()
    {
        SourceProperty = ConfigureProperty<IBitmap?, ImageBrush>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .Register();

        AffectRender<ImageBrush>(SourceProperty);
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
    public ImageBrush(IBitmap source)
    {
        Source = source;
    }

    /// <summary>
    /// Gets or sets the image to draw.
    /// </summary>
    public IBitmap? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }
}
