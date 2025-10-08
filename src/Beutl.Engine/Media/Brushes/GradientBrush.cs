using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Base class for brushes that draw with a gradient.
/// </summary>
public abstract partial class GradientBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    public GradientBrush()
    {
        GradientStops = new GradientStops(this);
        ScanProperties<GradientBrush>();
    }

    [Display(Name = nameof(Strings.SpreadMethod), ResourceType = typeof(Strings))]
    public IProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.Create(GradientSpreadMethod.Pad);

    [Display(Name = nameof(Strings.GradientStops), ResourceType = typeof(Strings))]
    public GradientStops GradientStops { get; }
}
