using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// A brush that draws with a linear gradient.
/// </summary>
public sealed partial class LinearGradientBrush : GradientBrush
{
    public LinearGradientBrush()
    {
        ScanProperties<LinearGradientBrush>();
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.StartPoint), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> StartPoint { get; } = Property.CreateAnimatable(RelativePoint.TopLeft);

    /// <summary>
    /// Gets or sets the end point for the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.EndPoint), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> EndPoint { get; } = Property.CreateAnimatable(RelativePoint.BottomRight);
}
