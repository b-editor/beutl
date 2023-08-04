using Beutl.Graphics;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="IDrawable"/>.
/// </summary>
public interface IDrawableBrush : ITileBrush
{
    /// <summary>
    /// Gets the drawable to draw.
    /// </summary>
    Drawable? Drawable { get; }
}
