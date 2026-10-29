using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip zooms in or out about the centre as it dissolves into the incoming clip,
/// which settles back from the same scale.
/// </summary>
[Display(Name = nameof(GraphicsStrings.ZoomTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ZoomTransition : ClipTransition
{
    public ZoomTransition()
    {
        ScanProperties<ZoomTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ZoomTransitionDirection> Direction { get; } = Property.CreateExpression(ZoomTransitionDirection.In);

    [Display(Name = nameof(GraphicsStrings.TileBrush_TileMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<GradientSpreadMethod> TileMode { get; } = Property.CreateExpression(GradientSpreadMethod.Decal);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawZoom(Direction == ZoomTransitionDirection.Out, TileMode);
        }
    }
}
