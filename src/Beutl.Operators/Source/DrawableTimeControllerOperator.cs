using System.ComponentModel.DataAnnotations;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Operation;
using Beutl.Serialization;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.TimeController), ResourceType = typeof(Strings))]
public sealed class DrawableTimeControllerOperator : PublishOperator<DrawableTimeController>
{
    protected override void FillProperties()
    {
        AddProperty(Value.OffsetPosition);
        AddProperty(Value.Speed);
        AddProperty(Value.AdjustTimeRange);
        AddProperty(Value.FrameRate);
        AddProperty(Value.Reverse);
        // Loop, HoldFirstFrame, HoldLastFrameはSourceOperator経由で使う場合は意味をなさないためコメントアウト
        // AddProperty(Value.Loop);
        // AddProperty(Value.HoldFirstFrame);
        // AddProperty(Value.HoldLastFrame);
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (!IsEnabled)
        {
            Value.Target.Expression = null;
            return;
        }

        Drawable? item = context.FlowRenderables.OfType<Drawable>().FirstOrDefault();
        if (item != null)
        {
            context.FlowRenderables.Remove(item);
            if ((Value.Target.Expression is ReferenceExpression<Drawable> refExp && refExp.ObjectId != item.Id)
                || Value.Target.Expression is null)
            {
                Value.Target.Expression = new ReferenceExpression<Drawable>(item.Id);
            }
        }
        else
        {
            Value.Target.Expression = null;
        }

        base.Evaluate(context);
    }

    public override void Enter()
    {
        base.Enter();
        Value.Target.Expression = null;
    }

    public override void Exit()
    {
        base.Exit();
        Value.Target.Expression = null;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        Value.Target.Expression = null;
        base.Serialize(context);
    }
}
