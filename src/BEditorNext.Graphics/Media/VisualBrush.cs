using BEditorNext.Graphics;

namespace BEditorNext.Media;

/// <summary>
/// Paints an area with an <see cref="IVisual"/>.
/// </summary>
public class VisualBrush : TileBrush, IDrawableBrush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VisualBrush"/> class.
    /// </summary>
    public VisualBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualBrush"/> class.
    /// </summary>
    /// <param name="drawable">The drawable to draw.</param>
    public VisualBrush(IDrawable drawable)
    {
        Drawable = drawable;
    }

    /// <summary>
    /// Gets or sets the visual to draw.
    /// </summary>
    public IDrawable? Drawable { get; set; }
}
