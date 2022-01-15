namespace BeUtl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
public abstract class Brush : IBrush
{
    /// <summary>
    /// Gets or sets the opacity of the brush.
    /// </summary>
    public float Opacity { get; set; } = 1;
}
