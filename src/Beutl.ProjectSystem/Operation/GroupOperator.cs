using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Serialization;

namespace Beutl.Operation;

public sealed class GroupOperator : PublishOperator<DrawableGroup>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.TransformOrigin, RelativePoint.Center);
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode, BlendMode.SrcOver);
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (!IsEnabled)
        {
            Value.Children.Clear();
            return;
        }

        Drawable[] items = context.FlowRenderables.OfType<Drawable>().ToArray();
        context.FlowRenderables.Clear();
        Value.Children.Replace(items);
        base.Evaluate(context);
    }

    public override void Enter()
    {
        base.Enter();
        Value.Children.Clear();
    }

    public override void Exit()
    {
        base.Exit();
        Value.Children.Clear();
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        Value.Children.Clear();
        base.Serialize(context);
    }
}
