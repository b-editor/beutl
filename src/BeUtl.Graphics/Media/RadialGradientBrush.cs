using BeUtl.Graphics;

namespace BeUtl.Media;

/// <summary>
/// Paints an area with a radial gradient.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush, IRadialGradientBrush
{
    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    public RelativePoint Center { get; set; } = RelativePoint.Center;

    /// <summary>
    /// Gets or sets the location of the two-dimensional focal point that defines the beginning
    /// of the gradient.
    /// </summary>
    public RelativePoint GradientOrigin { get; set; } = RelativePoint.Center;

    /// <summary>
    /// Gets or sets the horizontal and vertical radius of the outermost circle of the radial
    /// gradient.
    /// </summary>
    // TODO: This appears to always be relative so should use a RelativeSize struct or something.
    public float Radius { get; set; } = 0.5f;
}
