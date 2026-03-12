using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// A brush that draws with a linear gradient.
/// </summary>
[Display(Name = nameof(GraphicsStrings.LinearGradientBrush), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LinearGradientBrush : GradientBrush
{
    public LinearGradientBrush()
    {
        ScanProperties<LinearGradientBrush>();
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.LinearGradientBrush_StartPoint), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> StartPoint { get; } = Property.CreateAnimatable(RelativePoint.TopLeft);

    /// <summary>
    /// Gets or sets the end point for the gradient.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.EndPoint), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> EndPoint { get; } = Property.CreateAnimatable(RelativePoint.BottomRight);
}
