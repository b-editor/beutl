using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Describes the location and color of a transition point in a gradient.
/// </summary>
public sealed partial class GradientStop : EngineObject
{
    public GradientStop()
    {
        ScanProperties<GradientStop>();
    }

    public GradientStop(Color color, float offset) : this()
    {
        Color.CurrentValue = color;
        Offset.CurrentValue = offset;
    }

    [Display(Name = nameof(GraphicsStrings.Offset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Offset { get; } = Property.Create<float>();

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.Create(Colors.White);
}
