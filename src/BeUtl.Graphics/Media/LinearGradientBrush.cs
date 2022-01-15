
using BeUtl.Graphics;

namespace BeUtl.Media;

/// <summary>
/// A brush that draws with a linear gradient.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush, ILinearGradientBrush
{
    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    public RelativePoint StartPoint { get; set; } = RelativePoint.TopLeft;

    /// <summary>
    /// Gets or sets the end point for the gradient.
    /// </summary>
    public RelativePoint EndPoint { get; set; } = RelativePoint.BottomRight;
}
