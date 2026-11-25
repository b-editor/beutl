using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Base class for brushes that draw with a gradient.
/// </summary>
public abstract partial class GradientBrush : Brush
{
    public GradientBrush()
    {
        ScanProperties<GradientBrush>();
    }

    [Display(Name = nameof(GraphicsStrings.GradientBrush_SpreadMethod), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.CreateExpression(GradientSpreadMethod.Pad);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_InterpolationSpace), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<GradientInterpolationSpace> InterpolationSpace { get; }
        = Property.CreateExpression(GradientInterpolationSpace.LinearSrgb);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_InterpolatePremultiplied), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> InterpolatePremultiplied { get; } = Property.CreateExpression(false);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_GradientStops), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<GradientStop> GradientStops { get; } = Property.CreateList<GradientStop>();

    public partial class Resource
    {
        public override bool TrySampleColor(Rect bounds, Point point, out Color color)
            => BrushConstructor.TrySamplePaintColor(this, bounds, point, out color);
    }
}
