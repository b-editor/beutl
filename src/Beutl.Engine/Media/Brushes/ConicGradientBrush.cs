using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a swept circular gradient.
/// </summary>
public sealed partial class ConicGradientBrush : GradientBrush
{
    public ConicGradientBrush()
    {
        ScanProperties<ConicGradientBrush>();
    }

    /// <summary>
    /// Gets or sets the center point of the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Center), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>
    /// Gets or sets the angle of the start and end of the sweep, measured from above the center point.
    /// </summary>
    [Display(Name = nameof(Strings.Angle), ResourceType = typeof(Strings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable(0f);
}
