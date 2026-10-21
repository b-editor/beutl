using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Media;

public sealed partial class FallbackBrush : Brush, IFallback;

/// <summary>
/// Describes how an area is painted.
/// </summary>
[FallbackType(typeof(FallbackBrush))]
[PresenterType(typeof(BrushPresenter))]
public abstract partial class Brush : EngineObject
{
    public Brush()
    {
        ScanProperties<Brush>();
    }

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Transform?> Transform { get; } = Property.CreateWithExpression<Transform?>();

    [Display(Name = nameof(GraphicsStrings.TransformOrigin), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);
}
