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
        ScanProperties<GradientBrush>();
    }

    [Display(Name = nameof(GraphicsStrings.GradientBrush_SpreadMethod), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.Create(GradientSpreadMethod.Pad);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_GradientStops), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<GradientStop> GradientStops { get; } = Property.CreateList<GradientStop>();
}
