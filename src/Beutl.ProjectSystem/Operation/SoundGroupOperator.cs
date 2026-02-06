using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Operation;

[Display(Name = nameof(Strings.SoundGroup), ResourceType = typeof(Strings))]
public sealed class SoundGroupOperator : PublishOperator<SoundGroup>
{
    protected override void FillProperties()
    {
        // GainとEffectのみ追加（グループ全体に適用される）
        // OffsetPositionとSpeedは子要素で個別に設定するため追加しない
        AddProperty(Value.Gain, 100f);
        AddProperty(Value.Effect, new AudioEffectGroup());
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (!IsEnabled)
        {
            Value.Children.Clear();
            return;
        }

        Sound[] items = context.FlowRenderables.OfType<Sound>().ToArray();
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
