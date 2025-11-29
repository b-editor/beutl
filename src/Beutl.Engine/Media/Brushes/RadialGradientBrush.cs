using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a radial gradient.
/// </summary>
public sealed partial class RadialGradientBrush : GradientBrush
{
    public RadialGradientBrush()
    {
        ScanProperties<RadialGradientBrush>();
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Center), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>
    /// Gets or sets the location of the two-dimensional focal point that defines the beginning
    /// of the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.GradientOrigin), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> GradientOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>
    /// Gets or sets the horizontal and vertical radius of the outermost circle of the radial
    /// gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Radius), ResourceType = typeof(Strings))]
    public IProperty<float> Radius { get; } = Property.CreateAnimatable(50f);
}
