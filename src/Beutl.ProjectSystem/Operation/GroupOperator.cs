using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;

namespace Beutl.Operation;

public sealed class GroupOperator() : PublishOperator<DrawableGroup>([
    (Drawable.TransformProperty, () => new TransformGroup()),
    (Drawable.TransformOriginProperty, RelativePoint.Center),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    (Drawable.BlendModeProperty, BlendMode.SrcOver)
])
{
    public override void Evaluate(OperatorEvaluationContext context)
    {
        var value = Value;
        if (!IsEnabled) return;

        var items = context.FlowRenderables.OfType<Drawable>().ToArray();
        context.FlowRenderables.Clear();
        value.Children.Replace(items);
    }

    public override void Enter()
    {
        base.Enter();
        Value.Children.Clear();
    }
}
