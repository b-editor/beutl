namespace BeUtl.Media;

/// <summary>
/// Describes the location and color of a transition point in a gradient.
/// </summary>
public sealed class GradientStop : IGradientStop
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GradientStop"/> class.
    /// </summary>
    public GradientStop() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientStop"/> class.
    /// </summary>
    /// <param name="color">The color</param>
    /// <param name="offset">The offset</param>
    public GradientStop(Color color, double offset)
    {
        Color = color;
        Offset = offset;
    }

    /// <inheritdoc/>
    public double Offset { get; set; }

    /// <inheritdoc/>
    public Color Color { get; set; }
}
