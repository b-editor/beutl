using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a radial gradient.
/// </summary>
[Display(Name = nameof(GraphicsStrings.RadialGradientBrush), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RadialGradientBrush : GradientBrush
{
    public RadialGradientBrush()
    {
        ScanProperties<RadialGradientBrush>();
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.RadialGradientBrush_Center), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>
    /// Gets or sets the location of the two-dimensional focal point that defines the beginning
    /// of the gradient.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.RadialGradientBrush_GradientOrigin), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> GradientOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>
    /// Gets or sets the horizontal and vertical radius of the outermost circle of the radial
    /// gradient.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.RadialGradientBrush_Radius), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Radius { get; } = Property.CreateAnimatable(50f);
}
