namespace BEditorNext.Media;

/// <summary>
/// Describes how a <see cref="TileBrush"/> is tiled.
/// </summary>
public enum TileMode
{
    /// <summary>
    /// A single repeat of the content will be displayed.
    /// </summary>
    None,

    /// <summary>
    /// The content will be repeated horizontally, with alternate tiles mirrored.
    /// </summary>
    FlipX,

    /// <summary>
    /// The content will be repeated vertically, with alternate tiles mirrored.
    /// </summary>
    FlipY,

    /// <summary>
    /// The content will be repeated horizontally and vertically, with alternate tiles mirrored.
    /// </summary>
    FlipXY,

    /// <summary>
    /// The content will be repeated.
    /// </summary>
    Tile
}
