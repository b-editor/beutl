using Beutl.Media.Source;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="IBitmap"/>.
/// </summary>
public interface IImageBrush : ITileBrush
{
    /// <summary>
    /// Gets the image to draw.
    /// </summary>
    IImageSource? Source { get; }
}
