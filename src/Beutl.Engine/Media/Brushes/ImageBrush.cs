using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="IBitmap"/>.
/// </summary>
public partial class ImageBrush : TileBrush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    public ImageBrush()
    {
        ScanProperties<ImageBrush>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="source">The image to draw.</param>
    public ImageBrush(ImageSource source) : this()
    {
        Source.CurrentValue = source;
    }

    /// <summary>
    /// Gets or sets the image to draw.
    /// </summary>
    public IProperty<ImageSource?> Source { get; } = Property.Create<ImageSource?>();
}
