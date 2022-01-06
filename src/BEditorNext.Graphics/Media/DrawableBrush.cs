using BEditorNext.Graphics;

namespace BEditorNext.Media;

/// <summary>
/// Paints an area with an <see cref="IDrawable"/>.
/// </summary>
public class DrawableBrush : TileBrush, IDrawableBrush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawableBrush"/> class.
    /// </summary>
    public DrawableBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawableBrush"/> class.
    /// </summary>
    /// <param name="drawable">The drawable to draw.</param>
    public DrawableBrush(IDrawable drawable)
    {
        Drawable = drawable;
    }

    /// <summary>
    /// Gets or sets the visual to draw.
    /// </summary>
    public IDrawable? Drawable { get; set; }
}
