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
    public IExpressionProperty<Transform?> Transform { get; } = Property.CreateExpression<Transform?>();

    [Display(Name = nameof(GraphicsStrings.TransformOrigin), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    public partial class Resource
    {
        /// <summary>
        /// Tries to sample the brush color at a point without a render pass.
        /// </summary>
        /// <param name="bounds">The bounds the brush fills.</param>
        /// <param name="point">The sample position in the same coordinate space as <paramref name="bounds"/>.</param>
        /// <param name="color">The sampled color, including brush opacity, when sampling succeeds.</param>
        /// <returns><see langword="true"/> if a color was sampled. The default implementation returns <see langword="false"/>.</returns>
        public virtual bool TrySampleColor(Rect bounds, Point point, out Color color)
        {
            color = default;
            return false;
        }
    }
}
