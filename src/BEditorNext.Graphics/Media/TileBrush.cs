
using BEditorNext.Graphics;

namespace BEditorNext.Media;

/// <summary>
/// Base class for brushes which display repeating images.
/// </summary>
public abstract class TileBrush : Brush, ITileBrush
{
    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    public AlignmentX AlignmentX { get; set; } = AlignmentX.Center;

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    public AlignmentY AlignmentY { get; set; } = AlignmentY.Center;

    /// <summary>
    /// Gets or sets the rectangle on the destination in which to paint a tile.
    /// </summary>
    public RelativeRect DestinationRect { get; set; } = RelativeRect.Fill;

    /// <summary>
    /// Gets or sets the rectangle of the source image that will be displayed.
    /// </summary>
    public RelativeRect SourceRect { get; set; } = RelativeRect.Fill;

    /// <summary>
    /// Gets or sets a value controlling how the source rectangle will be stretched to fill
    /// the destination rect.
    /// </summary>
    public Stretch Stretch { get; set; } = Stretch.Uniform;

    /// <summary>
    /// Gets or sets the brush's tile mode.
    /// </summary>
    public TileMode TileMode { get; set; }

    /// <summary>
    /// Gets or sets the bitmap interpolation mode.
    /// </summary>
    /// <value>
    /// The bitmap interpolation mode.
    /// </value>
    public BitmapInterpolationMode BitmapInterpolationMode { get; set; } = BitmapInterpolationMode.Default;
}
