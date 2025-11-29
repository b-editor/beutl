using Beutl.Engine;

namespace Beutl.Media;

/// <summary>
/// Describes the location and color of a transition point in a gradient.
/// </summary>
public sealed partial class GradientStop : EngineObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GradientStop"/> class.
    /// </summary>
    public GradientStop()
    {
        ScanProperties<GradientStop>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientStop"/> class.
    /// </summary>
    /// <param name="color">The color</param>
    /// <param name="offset">The offset</param>
    public GradientStop(Color color, float offset) : this()
    {
        Color.CurrentValue = color;
        Offset.CurrentValue = offset;
    }

    public IProperty<float> Offset { get; } = Property.Create<float>();

    public IProperty<Color> Color { get; } = Property.Create<Color>();
}
