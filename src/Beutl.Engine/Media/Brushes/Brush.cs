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

    /// <summary>
    /// Gets or sets the opacity of the brush.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    /// <summary>
    /// Gets or sets the transform of the brush.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();

    /// <summary>
    /// Gets or sets the origin of the brush <see cref="Transform"/>
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.TransformOrigin), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);
}
