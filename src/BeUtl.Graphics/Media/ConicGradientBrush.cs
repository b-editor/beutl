
using BeUtl.Graphics;

namespace BeUtl.Media;

/// <summary>
/// Paints an area with a swept circular gradient.
/// </summary>
public sealed class ConicGradientBrush : GradientBrush, IConicGradientBrush
{
    /// <summary>
    /// Gets or sets the center point of the gradient.
    /// </summary>
    public RelativePoint Center { get; set; } = RelativePoint.Center;

    /// <summary>
    /// Gets or sets the angle of the start and end of the sweep, measured from above the center point.
    /// </summary>
    public float Angle { get; set; }
}
